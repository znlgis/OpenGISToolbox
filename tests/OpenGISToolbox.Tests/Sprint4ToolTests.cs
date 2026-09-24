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
/// Sprint-4 managed Voronoi / Delaunay. Checks the two properties that matter and
/// that a hand implementation can easily get wrong: Voronoi cells form an exact,
/// overlap-free tiling of the extent with each site in its own (and no other) cell,
/// and the Delaunay mesh covers the convex hull area with non-degenerate triangles.
/// </summary>
public class Sprint4ToolTests : IDisposable
{
    private readonly string _dir = TestEnv.Dir("sprint4");

    private static string P(double x, double y) =>
        $"POINT ({x.ToString("R", CultureInfo.InvariantCulture)} {y.ToString("R", CultureInfo.InvariantCulture)})";

    private string MakePoints(string name, (string Tag, double X, double Y)[] pts)
    {
        var path = Path.Combine(_dir, name);
        TestEnv.WriteLayer(l =>
        {
            l.GeometryType = GeometryType.POINT; l.Wkid = 32650;
            l.AddField(new OguField { Name = "tag", DataType = FieldDataType.STRING, Length = 10 });
            int fid = 0;
            foreach (var (tag, x, y) in pts)
                l.AddFeature(new OguFeature { Fid = fid, Wkt = P(x, y) }.Also(f => f.SetValue("tag", tag)));
            fid++;
        }, path);
        return path;
    }

    // ─── Voronoi ───

    [Fact]
    public async Task Voronoi_Corners_Tile_Extent_And_Partition_By_Nearest_Site()
    {
        var pts = new[]
        {
            ("A", 0.0, 0.0), ("B", 1.0, 0.0), ("C", 1.0, 1.0), ("D", 0.0, 1.0)
        };
        var input = MakePoints("vor_corners.geojson", pts);
        var output = Path.Combine(_dir, "vor_out.geojson");

        var result = await TestEnv.RunAsync(new VoronoiTool(), new Dictionary<string, string>
        {
            ["input"] = input, ["output"] = output
        });
        TestEnv.AssertSucceeded(result);

        var layer = TestEnv.ReadLayer(output);
        Assert.Equal(4, layer.GetFeatureCount());

        // Tiling: cell areas sum to the extent area (1×1 = 1).
        double total = layer.Features.Sum(f => GeometryUtil.AreaWkt(f.Wkt!));
        Assert.InRange(total, 0.999, 1.001);

        // Nearest-site partition: each site in its own cell, in no other cell.
        var cellsByTag = layer.Features.ToDictionary(
            f => f.GetValue("tag")?.ToString() ?? "", f => f.Wkt!);
        foreach (var (tag, x, y) in pts)
        {
            var own = cellsByTag[tag];
            var self = P(x, y);
            Assert.True(GeometryUtil.IntersectsWkt(own, self), $"cell {tag} should touch its own site");
            foreach (var (otherTag, ox, oy) in pts)
            {
                if (otherTag == tag) continue;
                Assert.False(GeometryUtil.ContainsWkt(own, P(ox, oy)),
                    $"cell {tag} must not contain foreign site {otherTag}");
            }
        }
    }

    [Fact]
    public async Task Voronoi_Internal_Intersection_Gives_Known_Cell_Areas()
    {
        // Two sites left/right: the bisector is x = 0.5, extent 1×1 ⇒ two 0.5 cells.
        var input = MakePoints("vor_two.geojson", new[] { ("L", 0.0, 0.5), ("R", 1.0, 0.5) });
        var output = Path.Combine(_dir, "vor_two_out.geojson");
        var result = await TestEnv.RunAsync(new VoronoiTool(), new Dictionary<string, string>
        {
            ["input"] = input, ["output"] = output
        });
        TestEnv.AssertSucceeded(result);

        var layer = TestEnv.ReadLayer(output);
        Assert.Equal(2, layer.GetFeatureCount());
        var areas = layer.Features.Select(f => GeometryUtil.AreaWkt(f.Wkt!)).OrderBy(a => a).ToArray();
        Assert.All(areas, a => Assert.InRange(a, 0.49, 0.51));
    }

    // ─── Delaunay ───

    [Fact]
    public async Task Delaunay_ThreePoints_One_Triangle_With_Known_Area()
    {
        var input = MakePoints("del_three.geojson", new[]
        {
            ("a", 0.0, 0.0), ("b", 4.0, 0.0), ("c", 0.0, 3.0)
        });
        var output = Path.Combine(_dir, "del_three_out.geojson");
        var result = await TestEnv.RunAsync(new DelaunayTool(), new Dictionary<string, string>
        {
            ["input"] = input, ["output"] = output
        });
        TestEnv.AssertSucceeded(result);

        var layer = TestEnv.ReadLayer(output);
        Assert.Equal(1, layer.GetFeatureCount());
        Assert.InRange(GeometryUtil.AreaWkt(layer.Features[0].Wkt!), 5.99, 6.01); // 4*3/2
    }

    [Fact]
    public async Task Delaunay_Square_Corners_Area_Equals_Convex_Hull()
    {
        var input = MakePoints("del_square.geojson", new[]
        {
            ("A", 0.0, 0.0), ("B", 10.0, 0.0), ("C", 10.0, 10.0), ("D", 0.0, 10.0)
        });
        var output = Path.Combine(_dir, "del_square_out.geojson");
        var result = await TestEnv.RunAsync(new DelaunayTool(), new Dictionary<string, string>
        {
            ["input"] = input, ["output"] = output
        });
        TestEnv.AssertSucceeded(result);

        var layer = TestEnv.ReadLayer(output);
        Assert.Equal(2, layer.GetFeatureCount()); // square splits into two triangles
        double total = layer.Features.Sum(f => GeometryUtil.AreaWkt(f.Wkt!));
        Assert.InRange(total, 99.9, 100.1); // convex hull (square) area = 100
        Assert.All(layer.Features, f => Assert.True(GeometryUtil.AreaWkt(f.Wkt!) > 0));
    }

    public void Dispose() { /* %TEMP% scratch is OS-cleaned */ }
}
