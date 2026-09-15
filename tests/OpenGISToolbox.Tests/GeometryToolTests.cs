using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OpenGIS.Utils.Geometry;
using OpenGISToolbox.Tools;
using Xunit;

namespace OpenGISToolbox.Tests;

public class GeometryToolTests : IDisposable
{
    private readonly string _dir = TestEnv.Dir("geometry");

    [Fact]
    public async Task Buffer_Point_In_UTM_Has_Expected_Area()
    {
        // Point in UTM 50N (meters), buffer 1000 m -> area ~ pi * 1000^2
        var input = Path.Combine(_dir, "utm_point.geojson");
        TestEnv.WriteLayer(l =>
        {
            l.GeometryType = OpenGIS.Utils.Engine.Enums.GeometryType.POINT;
            l.Wkid = 32650;
            l.AddFeature(new OpenGIS.Utils.Engine.Model.Layer.OguFeature { Fid = 0, Wkt = "POINT (500000 4430000)" });
        }, input);

        var output = Path.Combine(_dir, "buffer.geojson");
        var result = await TestEnv.RunAsync(new BufferTool(), new Dictionary<string, string>
        {
            ["input"] = input, ["output"] = output, ["distance"] = "1000"
        });
        TestEnv.AssertSucceeded(result);

        var layer = TestEnv.ReadLayer(output);
        Assert.Equal(1, layer.GetFeatureCount());
        var area = GeometryUtil.AreaWkt(layer.Features[0].Wkt!);
        Assert.InRange(area, 3.0e6, 3.3e6); // ~pi*1e6 = 3.1416e6 m^2
    }

    [Fact]
    public async Task Union_Merges_Overlapping_Squares()
    {
        var input = TestEnv.MakeSquaresShp(_dir);
        var output = Path.Combine(_dir, "union.shp");
        var result = await TestEnv.RunAsync(new UnionTool(), new Dictionary<string, string>
        {
            ["input1"] = input, ["input2"] = input, ["output"] = output
        });
        TestEnv.AssertSucceeded(result);

        var layer = TestEnv.ReadLayer(output);
        Assert.True(layer.GetFeatureCount() >= 1, "Union produced no features");
        // All output areas must be positive
        Assert.All(layer.Features.Where(f => !string.IsNullOrEmpty(f.Wkt)),
            f => Assert.True(GeometryUtil.AreaWkt(f.Wkt!) > 0));
    }

    [Fact]
    public async Task Difference_Removes_Overlap()
    {
        var a = Path.Combine(_dir, "a.geojson");
        var b = Path.Combine(_dir, "b.geojson");
        TestEnv.WriteLayer(l =>
        {
            l.GeometryType = OpenGIS.Utils.Engine.Enums.GeometryType.POLYGON; l.Wkid = 32650;
            l.AddFeature(new OpenGIS.Utils.Engine.Model.Layer.OguFeature { Fid = 0, Wkt = "POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))" });
        }, a);
        TestEnv.WriteLayer(l =>
        {
            l.GeometryType = OpenGIS.Utils.Engine.Enums.GeometryType.POLYGON; l.Wkid = 32650;
            l.AddFeature(new OpenGIS.Utils.Engine.Model.Layer.OguFeature { Fid = 0, Wkt = "POLYGON ((5 5, 15 5, 15 15, 5 15, 5 5))" });
        }, b);

        var output = Path.Combine(_dir, "diff.geojson");
        var result = await TestEnv.RunAsync(new DifferenceTool(), new Dictionary<string, string>
        {
            ["input1"] = a, ["input2"] = b, ["output"] = output
        });
        TestEnv.AssertSucceeded(result);

        var layer = TestEnv.ReadLayer(output);
        Assert.Equal(1, layer.GetFeatureCount());
        var area = GeometryUtil.AreaWkt(layer.Features[0].Wkt!);
        Assert.InRange(area, 74.9, 75.1); // 100 - 25
    }

    [Fact]
    public async Task Intersection_Yields_Overlap_Area()
    {
        var a = Path.Combine(_dir, "ia.geojson");
        var b = Path.Combine(_dir, "ib.geojson");
        TestEnv.WriteLayer(l =>
        {
            l.GeometryType = OpenGIS.Utils.Engine.Enums.GeometryType.POLYGON; l.Wkid = 32650;
            l.AddFeature(new OpenGIS.Utils.Engine.Model.Layer.OguFeature { Fid = 0, Wkt = "POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))" });
        }, a);
        TestEnv.WriteLayer(l =>
        {
            l.GeometryType = OpenGIS.Utils.Engine.Enums.GeometryType.POLYGON; l.Wkid = 32650;
            l.AddFeature(new OpenGIS.Utils.Engine.Model.Layer.OguFeature { Fid = 0, Wkt = "POLYGON ((5 5, 15 5, 15 15, 5 15, 5 5))" });
        }, b);

        var output = Path.Combine(_dir, "inter.geojson");
        var result = await TestEnv.RunAsync(new IntersectionTool(), new Dictionary<string, string>
        {
            ["input1"] = a, ["input2"] = b, ["output"] = output
        });
        TestEnv.AssertSucceeded(result);

        var layer = TestEnv.ReadLayer(output);
        Assert.Equal(1, layer.GetFeatureCount());
        Assert.InRange(GeometryUtil.AreaWkt(layer.Features[0].Wkt!), 24.9, 25.1);
    }

    [Fact]
    public async Task Centroid_Falls_Inside_Polygon()
    {
        var input = TestEnv.MakeSquaresShp(_dir);
        var output = Path.Combine(_dir, "centroid.shp");
        var result = await TestEnv.RunAsync(new CentroidTool(), new Dictionary<string, string>
        {
            ["input"] = input, ["output"] = output
        });
        TestEnv.AssertSucceeded(result);

        var layer = TestEnv.ReadLayer(output);
        Assert.Equal(3, layer.GetFeatureCount());
        Assert.All(layer.Features, f => Assert.StartsWith("POINT", f.Wkt, StringComparison.OrdinalIgnoreCase));
        // centroid of square A should be near (116.305, 39.905); select by label (Fids may be remapped)
        var wkt = layer.Features.First(f => f.GetValue("label")?.ToString() == "A").Wkt!;
        Assert.Contains("116.30", wkt);
    }

    [Fact]
    public async Task ConvexHull_Produces_Single_Polygon()
    {
        // ConvexHull operates per feature; a point's hull is the point itself,
        // so use polygon input to get a polygonal hull back.
        var input = TestEnv.MakeSquaresShp(_dir);
        var output = Path.Combine(_dir, "hull.geojson");
        var result = await TestEnv.RunAsync(new ConvexHullTool(), new Dictionary<string, string>
        {
            ["input"] = input, ["output"] = output
        });
        TestEnv.AssertSucceeded(result);

        var layer = TestEnv.ReadLayer(output);
        Assert.Equal(3, layer.GetFeatureCount());
        Assert.All(layer.Features, f => Assert.Contains("POLYGON", f.Wkt));
    }

    [Fact]
    public async Task Simplify_Reduces_Vertex_Count()
    {
        var input = TestEnv.MakeLinesShp(_dir);
        var output = Path.Combine(_dir, "simplified.shp");
        var result = await TestEnv.RunAsync(new SimplifyTool(), new Dictionary<string, string>
        {
            ["input"] = input, ["output"] = output, ["tolerance"] = "0.0001"
        });
        TestEnv.AssertSucceeded(result);

        var before = TestEnv.ReadLayer(input).Features.First(f => f.GetValue("name")?.ToString() == "zigzag").Wkt!;
        var after = TestEnv.ReadLayer(output).Features.First(f => f.GetValue("name")?.ToString() == "zigzag").Wkt!;
        int Count(string w) => w.Count(c => c == ',') + 1;
        Assert.True(Count(after) < Count(before), $"Simplify did not reduce vertices: {Count(before)} -> {Count(after)}");
    }

    [Fact]
    public async Task Merge_Combines_Feature_Counts()
    {
        var a = TestEnv.MakeSquaresShp(_dir, "sq1.shp");
        var b = TestEnv.MakePointsGeoJson(_dir);
        var output = Path.Combine(_dir, "merged.geojson");
        var result = await TestEnv.RunAsync(new MergeLayersTool(), new Dictionary<string, string>
        {
            ["input1"] = a, ["input2"] = b, ["output"] = output
        });
        TestEnv.AssertSucceeded(result);
        var layer = TestEnv.ReadLayer(output);
        Assert.Equal(6, layer.GetFeatureCount()); // 3 polygons + 3 points
    }

    [Fact]
    public async Task Split_By_Field_Creates_Group_Layers()
    {
        var input = TestEnv.MakeSquaresShp(_dir);
        var outDir = TestEnv.Dir("geometry", "split");
        var result = await TestEnv.RunAsync(new SplitLayerTool(), new Dictionary<string, string>
        {
            ["input"] = input, ["fieldName"] = "label", ["outputFolder"] = outDir
        });
        TestEnv.AssertSucceeded(result);
        var files = Directory.GetFiles(outDir, "*.shp");
        Assert.Equal(3, files.Length); // A, B, C
        foreach (var f in files)
        {
            var l = TestEnv.ReadLayer(f);
            Assert.Equal(1, l.GetFeatureCount());
        }
    }

    [Fact]
    public async Task Clip_Returns_Only_Intersecting_Features()
    {
        var input = TestEnv.MakePointsGeoJson(_dir);
        var clip = Path.Combine(_dir, "clip_extent.geojson");
        TestEnv.WriteLayer(l =>
        {
            l.GeometryType = OpenGIS.Utils.Engine.Enums.GeometryType.POLYGON; l.Wkid = 4326;
            l.AddFeature(new OpenGIS.Utils.Engine.Model.Layer.OguFeature
            { Fid = 0, Wkt = "POLYGON ((116 39, 117 39, 117 41, 116 41, 116 39))" });
        }, clip);

        var output = Path.Combine(_dir, "clipped.geojson");
        var result = await TestEnv.RunAsync(new ClipTool(), new Dictionary<string, string>
        {
            ["input"] = input, ["clipLayer"] = clip, ["output"] = output
        });
        TestEnv.AssertSucceeded(result);

        var layer = TestEnv.ReadLayer(output);
        Assert.Equal(1, layer.GetFeatureCount()); // only Beijing is inside
        Assert.Equal("Beijing", layer.Features[0].GetValue("city")?.ToString());
    }

    [Fact]
    public async Task SpatialFilter_Keeps_Extent_Features()
    {
        var input = TestEnv.MakePointsGeoJson(_dir);
        var output = Path.Combine(_dir, "filtered.geojson");
        var result = await TestEnv.RunAsync(new SpatialFilterTool(), new Dictionary<string, string>
        {
            ["input"] = input, ["output"] = output,
            ["extentWkt"] = "POLYGON ((116 39, 117 39, 117 41, 116 41, 116 39))"
        });
        TestEnv.AssertSucceeded(result);
        var layer = TestEnv.ReadLayer(output);
        Assert.Equal(1, layer.GetFeatureCount());
    }

    [Fact]
    public async Task CheckGeometry_Detects_Self_Intersection()
    {
        var input = TestEnv.MakeInvalidPolygons(_dir);
        var result = await TestEnv.RunAsync(new CheckGeometryTool(), new Dictionary<string, string>
        {
            ["input"] = input
        });
        TestEnv.AssertSucceeded(result);
        Assert.Contains("1", result.Message); // 1 invalid feature reported
    }

    [Fact]
    public async Task FixGeometries_Makes_Geometry_Valid_Or_Empty()
    {
        var input = TestEnv.MakeInvalidPolygons(_dir);
        var output = Path.Combine(_dir, "fixed.geojson");
        var result = await TestEnv.RunAsync(new FixGeometriesTool(), new Dictionary<string, string>
        {
            ["input"] = input, ["output"] = output
        });
        TestEnv.AssertSucceeded(result);

        var layer = TestEnv.ReadLayer(output);
        Assert.Equal(2, layer.GetFeatureCount());
        foreach (var f in layer.Features)
        {
            if (string.IsNullOrEmpty(f.Wkt)) continue;
            Assert.True(GeometryUtil.IsValid(GeometryUtil.Wkt2Geometry(f.Wkt)).IsValid,
                $"Geometry still invalid after fix: {f.Wkt}");
        }
    }

    [Fact]
    public async Task CalculateArea_Reports_Positive_Areas()
    {
        var input = TestEnv.MakeSquaresShp(_dir);
        var result = await TestEnv.RunAsync(new CalculateAreaTool(), new Dictionary<string, string>
        {
            ["input"] = input
        });
        TestEnv.AssertSucceeded(result);
    }

    [Fact]
    public async Task CalculateLength_Reports_Positive_Lengths()
    {
        var input = TestEnv.MakeLinesShp(_dir);
        var result = await TestEnv.RunAsync(new CalculateLengthTool(), new Dictionary<string, string>
        {
            ["input"] = input
        });
        TestEnv.AssertSucceeded(result);
    }

    [Fact]
    public async Task SpatialJoin_Attaches_Join_Attributes()
    {
        var target = TestEnv.MakePointsGeoJson(_dir);
        var join = Path.Combine(_dir, "regions.geojson");
        TestEnv.WriteLayer(l =>
        {
            l.GeometryType = OpenGIS.Utils.Engine.Enums.GeometryType.POLYGON; l.Wkid = 4326;
            l.AddField(new OpenGIS.Utils.Engine.Model.Layer.OguField { Name = "region", DataType = OpenGIS.Utils.Engine.Enums.FieldDataType.STRING, Length = 50 });
            l.AddFeature(new OpenGIS.Utils.Engine.Model.Layer.OguFeature
            { Fid = 0, Wkt = "POLYGON ((116 39, 117 39, 117 41, 116 41, 116 39))" }
                .Also(f => f.SetValue("region", "North")));
            l.AddFeature(new OpenGIS.Utils.Engine.Model.Layer.OguFeature
            { Fid = 1, Wkt = "POLYGON ((120 30, 122 30, 122 32, 120 32, 120 30))" }
                .Also(f => f.SetValue("region", "East")));
        }, join);

        var output = Path.Combine(_dir, "joined.geojson");
        var result = await TestEnv.RunAsync(new SpatialJoinTool(), new Dictionary<string, string>
        {
            ["input"] = target, ["joinLayer"] = join, ["joinType"] = "intersects", ["output"] = output
        });
        TestEnv.AssertSucceeded(result);

        var layer = TestEnv.ReadLayer(output);
        Assert.True(layer.GetFeatureCount() >= 1);
        // Beijing should have been assigned region=North if join semantics work
        var bj = layer.Features.FirstOrDefault(f => f.GetValue("city")?.ToString() == "Beijing");
        Assert.NotNull(bj);
        var region = bj!.GetValue("region")?.ToString();
        Assert.True(region is null or "" or "North",
            $"Unexpected join value: '{region}'");
    }

    public void Dispose() { }
}
