using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OpenGIS.Utils.Engine.Enums;
using OpenGIS.Utils.Engine.Model.Layer;
using OpenGIS.Utils.Geometry;
using OpenGISToolbox.Tools;
using Xunit;

namespace OpenGISToolbox.Tests;

/// <summary>
/// 规模与几何边界测试（数据运行时生成，保持数据无关）：
/// 12000 点固定种子点集上的计数守恒（CountPointsInPolygon）、最近邻暴力交叉验证、
/// 空间连接全量保留与网格派生；带洞/自交/多部件/空几何等边界几何的
/// 面积保真、检查修复与写出跳过行为。
/// </summary>
public class ScaleEdgeTests : IDisposable
{
    private readonly string _dir = TestEnv.Dir("scale-edge");

    // ─── 12k 点集（进程内缓存，多个用例共享同一份） ───

    private static (string Path, List<(double X, double Y)> Points)? _points10k;

    private (string Path, List<(double X, double Y)> Points) GetPoints10k()
    {
        if (_points10k is { } cached) return cached;

        var path = Path.Combine(_dir, "points_10k.shp");
        var pts = new List<(double, double)>(12000);
        var rng = new Random(42);
        TestEnv.WriteLayer(l =>
        {
            l.GeometryType = GeometryType.POINT;
            l.Wkid = 4326;
            l.AddField(new OguField { Name = "id", DataType = FieldDataType.INTEGER });
            l.AddField(new OguField { Name = "name", DataType = FieldDataType.STRING, Length = 20 });
            l.AddField(new OguField { Name = "val", DataType = FieldDataType.DOUBLE });
            l.AddField(new OguField { Name = "cat", DataType = FieldDataType.STRING, Length = 4 });
            for (var i = 0; i < 12000; i++)
            {
                var x = -180 + rng.NextDouble() * 360;
                var y = -85 + rng.NextDouble() * 170;
                pts.Add((x, y));
                l.AddFeature(new OguFeature { Fid = i, Wkt = P(x, y) }
                    .Also(f =>
                    {
                        f.SetValue("id", i);
                        f.SetValue("name", "P" + i.ToString("00000", CultureInfo.InvariantCulture));
                        f.SetValue("val", i * 0.5);
                        f.SetValue("cat", "ABCD"[i % 4].ToString());
                    }));
            }
        }, path);

        _points10k = (path, pts);
        return _points10k.Value;
    }

    // ─── fixtures ───

    private string MakeQuadrantsGeoJson(string name = "quadrants.geojson")
    {
        var path = Path.Combine(_dir, name);
        TestEnv.WriteLayer(l =>
        {
            l.GeometryType = GeometryType.POLYGON;
            l.Wkid = 4326;
            l.AddField(new OguField { Name = "q", DataType = FieldDataType.STRING, Length = 8 });
            var fid = 0;
            void Box(string label, int x0, int y0, int x1, int y1)
            {
                var wkt = $"POLYGON (({x0} {y0}, {x1} {y0}, {x1} {y1}, {x0} {y1}, {x0} {y0}))";
                l.AddFeature(new OguFeature { Fid = fid++, Wkt = wkt }.Also(f => f.SetValue("q", label)));
            }
            Box("NE", 0, 0, 180, 85);
            Box("NW", -180, 0, 0, 85);
            Box("SE", 0, -85, 180, 0);
            Box("SW", -180, -85, 0, 0);
        }, path);
        return path;
    }

    private string MakePolygonEdgeShp(string name = "geom_polygon_edge.shp")
    {
        var path = Path.Combine(_dir, name);
        TestEnv.WriteLayer(l =>
        {
            l.GeometryType = GeometryType.POLYGON;
            l.Wkid = 4326;
            l.AddField(new OguField { Name = "id", DataType = FieldDataType.INTEGER });
            l.AddField(new OguField { Name = "label", DataType = FieldDataType.STRING, Length = 24 });
            void Add(int id, string label, string wkt) =>
                l.AddFeature(new OguFeature { Fid = id - 1, Wkt = wkt }
                    .Also(f => { f.SetValue("id", id); f.SetValue("label", label); }));

            Add(1, "tiny-tri", "POLYGON ((0 0, 0.0001 0, 0.00005 0.0001, 0 0))");
            Add(2, "square", "POLYGON ((0 0, 1 0, 1 1, 0 1, 0 0))");
            Add(3, "donut", "POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0), (4 4, 4 6, 6 6, 6 4, 4 4))");
            Add(4, "bowtie", "POLYGON ((0 0, 2 2, 2 0, 0 2, 0 0))");
            Add(5, "two-parts", "MULTIPOLYGON (((0 0, 1 0, 1 1, 0 1, 0 0)), ((5 5, 6 5, 6 6, 5 6, 5 5)))");
            Add(6, "u-shape", "POLYGON ((0 0, 3 0, 3 1, 1 1, 1 2, 3 2, 3 3, 0 3, 0 0))");
        }, path);
        return path;
    }

    // ─── helpers ───

    private static string P(double x, double y) =>
        "POINT (" + x.ToString("R", CultureInfo.InvariantCulture) + " " + y.ToString("R", CultureInfo.InvariantCulture) + ")";

    private static bool InBox((double X, double Y) p, double x0, double y0, double x1, double y1)
        => p.X > x0 && p.X < x1 && p.Y > y0 && p.Y < y1;

    private static (double X, double Y) FirstPoint(string wkt)
    {
        var s = wkt[(wkt.IndexOf('(') + 1)..];
        var parts = s.Split(new[] { ' ', ',', ')' }, StringSplitOptions.RemoveEmptyEntries);
        return (double.Parse(parts[0], CultureInfo.InvariantCulture), double.Parse(parts[1], CultureInfo.InvariantCulture));
    }

    private static FormatConversionTool ConvToGeoJson()
        => new("boundary-convert", "Boundary Convert", "边界转换",
            "Convert boundary layer", "转换边界图层",
            DataFormatType.SHP, ".shp", "Shapefile|*.shp",
            DataFormatType.GEOJSON, ".geojson", "out|*.geojson");

    // ─── 1. 万级点集：CountPointsInPolygon 计数守恒 ───

    [Fact]
    public async Task CountPointsInPolygon_On_12k_Points_Conserves_Totals()
    {
        var (pointsPath, pts) = GetPoints10k();
        var polysPath = MakeQuadrantsGeoJson();
        var output = Path.Combine(_dir, "count_out.geojson");

        var sw = Stopwatch.StartNew();
        var result = await TestEnv.RunAsync(new CountPointsInPolygonTool(), new Dictionary<string, string>
        {
            ["input"] = polysPath, ["points"] = pointsPath, ["output"] = output, ["field"] = "pt_count"
        });
        sw.Stop();
        TestEnv.AssertSucceeded(result);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(120), $"12k 点计数耗时过长: {sw.Elapsed}");

        var layer = TestEnv.ReadLayer(output);
        Assert.Equal(4, layer.GetFeatureCount());
        var counts = layer.Features.ToDictionary(
            f => f.GetValue("q")!.ToString()!,
            f => Convert.ToInt32(f.GetValue("pt_count"), CultureInfo.InvariantCulture));

        var expected = new Dictionary<string, int>
        {
            ["NE"] = pts.Count(p => InBox(p, 0, 0, 180, 85)),
            ["NW"] = pts.Count(p => InBox(p, -180, 0, 0, 85)),
            ["SE"] = pts.Count(p => InBox(p, 0, -85, 180, 0)),
            ["SW"] = pts.Count(p => InBox(p, -180, -85, 0, 0)),
        };
        foreach (var (k, v) in expected)
            Assert.Equal(v, counts[k]);
        Assert.Equal(12000, counts.Values.Sum());
    }

    // ─── 2. 万级点集：NearestNeighbor 暴力交叉验证 ───

    [Fact]
    public async Task NearestNeighbor_Matches_BruteForce_On_Sampled_Targets()
    {
        var (refPath, refPts) = GetPoints10k();

        var targets = new List<(double X, double Y)>();
        var rng = new Random(7);
        for (var i = 0; i < 30; i++)
            targets.Add((-180 + rng.NextDouble() * 360, -85 + rng.NextDouble() * 170));

        var targetPath = Path.Combine(_dir, "nn_targets.geojson");
        TestEnv.WriteLayer(l =>
        {
            l.GeometryType = GeometryType.POINT;
            l.Wkid = 4326;
            l.AddField(new OguField { Name = "t", DataType = FieldDataType.INTEGER });
            for (var i = 0; i < targets.Count; i++)
                l.AddFeature(new OguFeature { Fid = i, Wkt = P(targets[i].X, targets[i].Y) }.Also(f => f.SetValue("t", i)));
        }, targetPath);

        var output = Path.Combine(_dir, "nn_out.geojson");
        var sw = Stopwatch.StartNew();
        var result = await TestEnv.RunAsync(new NearestNeighborTool(), new Dictionary<string, string>
        {
            ["input"] = targetPath, ["reference"] = refPath, ["output"] = output
        });
        sw.Stop();
        TestEnv.AssertSucceeded(result);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(120), $"最近邻耗时过长: {sw.Elapsed}");

        var layer = TestEnv.ReadLayer(output);
        Assert.Equal(30, layer.GetFeatureCount());
        var byT = layer.Features.ToDictionary(f => Convert.ToInt32(f.GetValue("t"), CultureInfo.InvariantCulture));

        for (var i = 0; i < targets.Count; i++)
        {
            var (tx, ty) = targets[i];
            var best = double.MaxValue;
            foreach (var (rx, ry) in refPts)
            {
                var dx = rx - tx;
                var dy = ry - ty;
                var d = Math.Sqrt(dx * dx + dy * dy);
                if (d < best) best = d;
            }
            var got = Convert.ToDouble(byT[i].GetValue("nn_dist"), CultureInfo.InvariantCulture);
            Assert.True(Math.Abs(got - best) <= Math.Max(1e-8, best * 1e-8),
                $"target {i}: nn_dist={got.ToString("R", CultureInfo.InvariantCulture)} vs brute={best.ToString("R", CultureInfo.InvariantCulture)}");
        }
    }

    // ─── 3. 万级点集：SpatialJoin 全量保留 ───

    [Fact]
    public async Task SpatialJoin_12k_Points_Retains_All_With_Region()
    {
        var (pointsPath, _) = GetPoints10k();
        var polysPath = MakeQuadrantsGeoJson();
        var output = Path.Combine(_dir, "join_out.geojson");

        var sw = Stopwatch.StartNew();
        var result = await TestEnv.RunAsync(new SpatialJoinTool(), new Dictionary<string, string>
        {
            ["input"] = pointsPath, ["joinLayer"] = polysPath, ["joinType"] = "intersects", ["output"] = output
        });
        sw.Stop();
        TestEnv.AssertSucceeded(result);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(120), $"空间连接耗时过长: {sw.Elapsed}");

        var layer = TestEnv.ReadLayer(output);
        Assert.Equal(12000, layer.GetFeatureCount());

        var sampled = 0;
        foreach (var f in layer.Features)
        {
            var id = Convert.ToInt32(f.GetValue("id"), CultureInfo.InvariantCulture);
            if (id % 2000 != 0) continue;
            var (x, y) = FirstPoint(f.Wkt!);
            var expected = x >= 0 ? (y >= 0 ? "NE" : "SE") : (y >= 0 ? "NW" : "SW");
            Assert.Equal(expected, f.GetValue("q")?.ToString());
            sampled++;
        }
        Assert.True(sampled >= 6, $"仅抽查了 {sampled} 个点");
    }

    // ─── 4. 万级点集：CreateGrid 从输入派生范围 ───

    [Fact]
    public async Task CreateGrid_Derives_Extent_From_12k_Points()
    {
        var (pointsPath, pts) = GetPoints10k();
        var output = Path.Combine(_dir, "grid_10k.geojson");
        var result = await TestEnv.RunAsync(new CreateGridTool(), new Dictionary<string, string>
        {
            ["input"] = pointsPath, ["output"] = output, ["rows"] = "10", ["cols"] = "18"
        });
        TestEnv.AssertSucceeded(result);

        var layer = TestEnv.ReadLayer(output);
        Assert.Equal(180, layer.GetFeatureCount()); // 10 × 18

        double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X);
        double minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
        var expectedArea = (maxX - minX) * (maxY - minY); // 平面近似（度²）
        var total = layer.Features.Sum(f => GeometryUtil.AreaWkt(f.Wkt!));
        Assert.InRange(total, expectedArea * 0.99, expectedArea * 1.01);
    }

    // ─── 5. 几何边界：面积保真（带洞 / 多部件 / U 形） ───

    [Fact]
    public async Task Polygon_Edge_Geometries_Keep_Area_Across_RoundTrip()
    {
        var src = MakePolygonEdgeShp();
        var outPath = Path.Combine(_dir, "poly_edges.geojson");
        var result = await TestEnv.RunAsync(ConvToGeoJson(), new Dictionary<string, string>
        {
            ["input"] = src, ["output"] = outPath
        });
        TestEnv.AssertSucceeded(result);

        var layer = TestEnv.ReadLayer(outPath);
        Assert.Equal(6, layer.GetFeatureCount());
        var byLabel = layer.Features.ToDictionary(f => f.GetValue("label")!.ToString()!, f => f);

        // 带洞多边形：面积 = 外环 100 − 内环 4
        Assert.InRange(GeometryUtil.AreaWkt(byLabel["donut"].Wkt!), 95.99, 96.01);
        // 多部件：两个 1×1 方块
        Assert.InRange(GeometryUtil.AreaWkt(byLabel["two-parts"].Wkt!), 1.99, 2.01);
        Assert.InRange(GeometryUtil.AreaWkt(byLabel["square"].Wkt!), 0.99, 1.01);
        // U 形：3×3 − 2×1 缺口
        Assert.InRange(GeometryUtil.AreaWkt(byLabel["u-shape"].Wkt!), 6.99, 7.01);
    }

    // ─── 6. 几何边界：无效几何检查与修复 ───

    [Fact]
    public async Task Check_And_Fix_Handle_Invalid_Polygon_Edges()
    {
        var src = MakePolygonEdgeShp();

        var check = await TestEnv.RunAsync(new CheckGeometryTool(), new Dictionary<string, string>
        {
            ["input"] = src
        });
        TestEnv.AssertSucceeded(check);
        Assert.Contains("1", check.Message); // 仅 bowtie 自相交无效

        var fixedOut = Path.Combine(_dir, "poly_fixed.geojson");
        var fix = await TestEnv.RunAsync(new FixGeometriesTool(), new Dictionary<string, string>
        {
            ["input"] = src, ["output"] = fixedOut
        });
        TestEnv.AssertSucceeded(fix);

        var layer = TestEnv.ReadLayer(fixedOut);
        Assert.Equal(6, layer.GetFeatureCount());
        foreach (var f in layer.Features)
        {
            if (string.IsNullOrEmpty(f.Wkt)) continue;
            var g = GeometryUtil.Wkt2Geometry(f.Wkt);
            Assert.True(GeometryUtil.IsValid(g).IsValid, $"修复后仍无效: {f.Wkt}");
        }
    }

    // ─── 7. 空几何：写出时跳过而非整层失败 ───

    [Fact]
    public async Task Null_Geometry_Features_Are_Skipped_Without_Failing_The_Layer()
    {
        var outPath = Path.Combine(_dir, "null_skip.geojson");
        TestEnv.WriteLayer(l =>
        {
            l.GeometryType = GeometryType.POINT;
            l.Wkid = 4326;
            l.AddField(new OguField { Name = "name", DataType = FieldDataType.STRING, Length = 20 });
            l.AddFeature(new OguFeature { Fid = 0, Wkt = "POINT (1 1)" }.Also(f => f.SetValue("name", "a")));
            l.AddFeature(new OguFeature { Fid = 1, Wkt = null }); // 空几何：应被跳过
            l.AddFeature(new OguFeature { Fid = 2, Wkt = "POINT (2 2)" }.Also(f => f.SetValue("name", "b")));
        }, outPath);

        var layer = TestEnv.ReadLayer(outPath);
        Assert.Equal(2, layer.GetFeatureCount());
        Assert.Equal(new[] { "a", "b" },
            layer.Features.Select(f => f.GetValue("name")?.ToString()).OrderBy(x => x).ToList());
    }

    // ─── 8. 空图层：转换保持优雅 ───

    [Fact]
    public async Task Empty_Layer_Conversion_Is_Graceful()
    {
        var src = Path.Combine(_dir, "empty_edge.shp");
        TestEnv.WriteLayer(l =>
        {
            l.GeometryType = GeometryType.POINT;
            l.Wkid = 4326;
            l.AddField(new OguField { Name = "id", DataType = FieldDataType.INTEGER });
        }, src);

        var outPath = Path.Combine(_dir, "empty_out.geojson");
        var result = await TestEnv.RunAsync(ConvToGeoJson(), new Dictionary<string, string>
        {
            ["input"] = src, ["output"] = outPath
        });
        TestEnv.AssertSucceeded(result);
        Assert.Equal(0, TestEnv.ReadLayer(outPath).GetFeatureCount());
    }

    public void Dispose() { }
}
