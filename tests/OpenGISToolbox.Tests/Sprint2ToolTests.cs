using System;
using System.Collections.Generic;
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
/// Sprint-2 analysis tools: Extract by Location, Nearest Neighbor, Count Points
/// in Polygon and Create Grid. Expectations are derived from known synthetic
/// geometry laid out on a metric UTM grid, so results are checked independently
/// of what GDAL reports.
/// </summary>
public class Sprint2ToolTests : IDisposable
{
    private readonly string _dir = TestEnv.Dir("sprint2");

    // ─── fixtures ───
    private static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);
    private static string Poly(double x0, double y0, double x1, double y1) =>
        $"POLYGON (({F(x0)} {F(y0)},{F(x1)} {F(y0)},{F(x1)} {F(y1)},{F(x0)} {F(y1)},{F(x0)} {F(y0)}))";
    private static string Pt(double x, double y) => $"POINT ({F(x)} {F(y)})";

    private string MakeTwoSquares(string name)
    {
        var path = Path.Combine(_dir, name);
        TestEnv.WriteLayer(l =>
        {
            l.GeometryType = GeometryType.POLYGON; l.Wkid = 32650;
            l.AddField(new OguField { Name = "tag", DataType = FieldDataType.STRING, Length = 10 });
            l.AddFeature(new OguFeature { Fid = 0, Wkt = Poly(0, 0, 10, 10) }.Also(f => f.SetValue("tag", "A")));
            l.AddFeature(new OguFeature { Fid = 1, Wkt = Poly(100, 0, 110, 10) }.Also(f => f.SetValue("tag", "B")));
        }, path);
        return path;
    }

    private string MakeRefSquare(string name, double x0, double y0, double x1, double y1)
    {
        var path = Path.Combine(_dir, name);
        TestEnv.WriteLayer(l =>
        {
            l.GeometryType = GeometryType.POLYGON; l.Wkid = 32650;
            l.AddFeature(new OguFeature { Fid = 0, Wkt = Poly(x0, y0, x1, y1) });
        }, path);
        return path;
    }

    private static string TagOf(OguFeature f) => f.GetValue("tag")?.ToString() ?? "";

    // ─── 2.1 Extract by location ───

    [Fact]
    public async Task ExtractByLocation_Intersects_Selects_Only_Overlapping()
    {
        var target = MakeTwoSquares("targets.shp");
        var reference = MakeRefSquare("ref_near.shp", 5, 5, 15, 15); // overlaps A only
        var output = Path.Combine(_dir, "extract_int.geojson");
        var result = await TestEnv.RunAsync(new ExtractByLocationTool(), new Dictionary<string, string>
        {
            ["input"] = target, ["reference"] = reference, ["output"] = output, ["predicate"] = "Intersects"
        });
        TestEnv.AssertSucceeded(result);

        var layer = TestEnv.ReadLayer(output);
        Assert.Equal(1, layer.GetFeatureCount());
        Assert.Equal("A", TagOf(layer.Features[0]));
    }

    [Fact]
    public async Task ExtractByLocation_Disjoint_Selects_Complement()
    {
        var target = MakeTwoSquares("targets_dj.shp");
        var reference = MakeRefSquare("ref_dj.shp", 5, 5, 15, 15);
        var output = Path.Combine(_dir, "extract_dj.geojson");
        var result = await TestEnv.RunAsync(new ExtractByLocationTool(), new Dictionary<string, string>
        {
            ["input"] = target, ["reference"] = reference, ["output"] = output, ["predicate"] = "Disjoint"
        });
        TestEnv.AssertSucceeded(result);
        var layer = TestEnv.ReadLayer(output);
        Assert.Equal(1, layer.GetFeatureCount());
        Assert.Equal("B", TagOf(layer.Features[0]));
    }

    [Fact]
    public async Task ExtractByLocation_Empty_Result_Succeeds()
    {
        var target = MakeTwoSquares("targets_e.shp");
        var reference = MakeRefSquare("ref_far.shp", 1000, 1000, 1010, 1010);
        var output = Path.Combine(_dir, "extract_empty.geojson");
        var result = await TestEnv.RunAsync(new ExtractByLocationTool(), new Dictionary<string, string>
        {
            ["input"] = target, ["reference"] = reference, ["output"] = output, ["predicate"] = "Intersects"
        });
        TestEnv.AssertSucceeded(result);
        Assert.Equal(0, TestEnv.ReadLayer(output).GetFeatureCount());
    }

    // ─── 2.2 Nearest neighbor ───

    [Fact]
    public async Task NearestNeighbor_Picks_Closest_And_Records_Distance()
    {
        var target = Path.Combine(_dir, "nn_target.geojson");
        TestEnv.WriteLayer(l =>
        {
            l.GeometryType = GeometryType.POINT; l.Wkid = 32650;
            l.AddFeature(new OguFeature { Fid = 0, Wkt = Pt(0, 0) });
        }, target);

        var reference = Path.Combine(_dir, "nn_ref.geojson");
        TestEnv.WriteLayer(l =>
        {
            l.GeometryType = GeometryType.POINT; l.Wkid = 32650;
            l.AddFeature(new OguFeature { Fid = 0, Wkt = Pt(10, 0) }); // dist 10
            l.AddFeature(new OguFeature { Fid = 1, Wkt = Pt(0, 5) });  // dist 5 (nearest)
        }, reference);

        var output = Path.Combine(_dir, "nn_out.geojson");
        var result = await TestEnv.RunAsync(new NearestNeighborTool(), new Dictionary<string, string>
        {
            ["input"] = target, ["reference"] = reference, ["output"] = output
        });
        TestEnv.AssertSucceeded(result);

        var layer = TestEnv.ReadLayer(output);
        var feat = layer.Features[0];
        Assert.Equal(1, Convert.ToInt32(feat.GetValue("nn_fid"), CultureInfo.InvariantCulture));
        Assert.InRange(Convert.ToDouble(feat.GetValue("nn_dist"), CultureInfo.InvariantCulture), 4.999, 5.001);
    }

    // ─── 2.3 Count points in polygon ───

    [Fact]
    public async Task CountPointsInPolygon_Writes_Per_Polygon_Tallies()
    {
        var polygons = Path.Combine(_dir, "cp_polys.geojson");
        TestEnv.WriteLayer(l =>
        {
            l.GeometryType = GeometryType.POLYGON; l.Wkid = 32650;
            l.AddField(new OguField { Name = "tag", DataType = FieldDataType.STRING, Length = 10 });
            l.AddFeature(new OguFeature { Fid = 0, Wkt = Poly(0, 0, 10, 10) }.Also(f => f.SetValue("tag", "A")));
            l.AddFeature(new OguFeature { Fid = 1, Wkt = Poly(10, 0, 20, 10) }.Also(f => f.SetValue("tag", "B")));
        }, polygons);

        var points = Path.Combine(_dir, "cp_pts.geojson");
        TestEnv.WriteLayer(l =>
        {
            l.GeometryType = GeometryType.POINT; l.Wkid = 32650;
            l.AddFeature(new OguFeature { Fid = 0, Wkt = Pt(1, 1) });   // A
            l.AddFeature(new OguFeature { Fid = 1, Wkt = Pt(2, 2) });   // A
            l.AddFeature(new OguFeature { Fid = 2, Wkt = Pt(15, 5) });  // B
            l.AddFeature(new OguFeature { Fid = 3, Wkt = Pt(50, 50) }); // none
        }, points);

        var output = Path.Combine(_dir, "cp_out.geojson");
        var result = await TestEnv.RunAsync(new CountPointsInPolygonTool(), new Dictionary<string, string>
        {
            ["input"] = polygons, ["points"] = points, ["output"] = output, ["field"] = "pt_count"
        });
        TestEnv.AssertSucceeded(result);

        var layer = TestEnv.ReadLayer(output);
        var byTag = layer.Features.ToDictionary(TagOf, f => Convert.ToInt32(f.GetValue("pt_count"), CultureInfo.InvariantCulture));
        Assert.Equal(2, byTag["A"]);
        Assert.Equal(1, byTag["B"]);
        Assert.Equal(3, byTag.Values.Sum()); // one point outside every polygon
    }

    // ─── 2.4 Create grid ───

    [Fact]
    public async Task CreateGrid_Manual_Extent_Has_Exact_Cell_Count_And_Area()
    {
        var output = Path.Combine(_dir, "grid.geojson");
        var result = await TestEnv.RunAsync(new CreateGridTool(), new Dictionary<string, string>
        {
            ["output"] = output,
            ["xmin"] = "0", ["ymin"] = "0", ["xmax"] = "100", ["ymax"] = "50",
            ["rows"] = "5", ["cols"] = "10", ["wkid"] = "32650"
        });
        TestEnv.AssertSucceeded(result);

        var layer = TestEnv.ReadLayer(output);
        Assert.Equal(50, layer.GetFeatureCount()); // 5 × 10
        double totalArea = layer.Features.Sum(f => GeometryUtil.AreaWkt(f.Wkt!));
        Assert.InRange(totalArea, 4999, 5001); // extent area 100 × 50
        // All cells valid and non-empty.
        Assert.All(layer.Features, f => Assert.True(GeometryUtil.AreaWkt(f.Wkt!) > 0));
    }

    [Fact]
    public async Task CreateGrid_Derives_Extent_From_Input_Layer()
    {
        var extentLayer = MakeTwoSquares("grid_extent.shp"); // bbox x[0,110] y[0,10]
        var output = Path.Combine(_dir, "grid_from_layer.geojson");
        var result = await TestEnv.RunAsync(new CreateGridTool(), new Dictionary<string, string>
        {
            ["input"] = extentLayer, ["output"] = output, ["rows"] = "2", ["cols"] = "2"
        });
        TestEnv.AssertSucceeded(result);
        var layer = TestEnv.ReadLayer(output);
        Assert.Equal(4, layer.GetFeatureCount());
        double totalArea = layer.Features.Sum(f => GeometryUtil.AreaWkt(f.Wkt!));
        Assert.InRange(totalArea, 1099, 1101); // bbox 110 wide × 10 tall (wkid 32650, planar units)
    }

    public void Dispose() { /* %TEMP% scratch is OS-cleaned */ }
}
