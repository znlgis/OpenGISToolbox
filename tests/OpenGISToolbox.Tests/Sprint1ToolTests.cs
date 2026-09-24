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
/// Sprint-1 feature-expansion tools: Dissolve, Multipart↔Singlepart, Extract
/// Vertices, Polygon↔Line, Symmetric Difference and Batch Format Conversion.
/// Every expectation is derived from known synthetic geometry (metric UTM grid),
/// not from GDAL's own output, so the assertions are an independent cross-check.
/// </summary>
public class Sprint1ToolTests : IDisposable
{
    private readonly string _dir = TestEnv.Dir("sprint1");

    // ─── fixtures ───

    /// <summary>Bare coordinate list for an axis-aligned s×s square starting at (x0,y0).</summary>
    private static string Ring(double x0, double y0, double s = 10)
    {
        string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);
        var x1 = x0 + s; var y1 = y0 + s;
        return $"{F(x0)} {F(y0)},{F(x1)} {F(y0)},{F(x1)} {F(y1)},{F(x0)} {F(y1)},{F(x0)} {F(y0)}";
    }

    private static string Poly(double x0, double y0, double s = 10) => $"POLYGON (({Ring(x0, y0, s)}))";

    /// <summary>MULTIPOLYGON from bare coordinate-ring lists (each ring becomes one polygon).</summary>
    private static string MultiPoly(params string[] rings) =>
        "MULTIPOLYGON (" + string.Join(",", rings.Select(r => $"(({r}))")) + ")";

    /// <summary>Four 10x10 UTM squares in two 2x2 groups (X, Y); within-group squares share an edge.</summary>
    private string MakeGroupedSquares(string name = "grp.shp")
    {
        var path = Path.Combine(_dir, name);
        TestEnv.WriteLayer(l =>
        {
            l.GeometryType = GeometryType.POLYGON;
            l.Wkid = 32650;
            l.AddField(new OguField { Name = "grp", DataType = FieldDataType.STRING, Length = 10 });
            void Add(int fid, double x, double y, string g) =>
                l.AddFeature(new OguFeature { Fid = fid, Wkt = Poly(x, y) }.Also(f => f.SetValue("grp", g)));
            Add(0, 0, 0, "X");
            Add(1, 10, 0, "X");
            Add(2, 0, 10, "Y");
            Add(3, 10, 10, "Y");
        }, path);
        return path;
    }

    // ─── 1.1 Dissolve ───

    [Fact]
    public async Task Dissolve_Merges_Each_Attribute_Group()
    {
        var input = MakeGroupedSquares();
        var output = Path.Combine(_dir, "dissolved.geojson");
        var result = await TestEnv.RunAsync(new DissolveTool(), new Dictionary<string, string>
        {
            ["input"] = input, ["output"] = output, ["field"] = "grp"
        });
        TestEnv.AssertSucceeded(result);

        var layer = TestEnv.ReadLayer(output);
        Assert.Equal(2, layer.GetFeatureCount()); // one per group value

        // Each group = two adjacent 10x10 squares = 200 m² exactly; total preserved = 400.
        var areas = layer.Features.Select(f => GeometryUtil.AreaWkt(f.Wkt!)).OrderBy(a => a).ToList();
        Assert.All(areas, a => Assert.InRange(a, 199.9, 200.1));
        Assert.InRange(areas.Sum(), 399.8, 400.2);
        Assert.All(layer.Features, f => Assert.True(GeometryUtil.IsValid(GeometryUtil.Wkt2Geometry(f.Wkt!)).IsValid));
    }

    [Fact]
    public async Task Dissolve_Missing_Field_Fails_Gracefully()
    {
        var input = MakeGroupedSquares();
        var result = await TestEnv.RunAsync(new DissolveTool(), new Dictionary<string, string>
        {
            ["input"] = input, ["output"] = Path.Combine(_dir, "d2.geojson"), ["field"] = "nope"
        });
        Assert.False(result.Success);
    }

    // ─── 1.2 Multipart ↔ Singlepart ───

    [Fact]
    public async Task MultipartToSingle_Then_SingleToMulti_RoundTrips_PartCount()
    {
        var input = Path.Combine(_dir, "multi.geojson");
        TestEnv.WriteLayer(l =>
        {
            l.GeometryType = GeometryType.MULTIPOLYGON;
            l.Wkid = 32650;
            // one 2-part multipolygon + one 1-part multipolygon => 3 parts total
            l.AddFeature(new OguFeature
            {
                Fid = 0,
                Wkt = MultiPoly(Ring(0, 0), Ring(100, 0))
            });
            l.AddFeature(new OguFeature { Fid = 1, Wkt = MultiPoly(Ring(200, 0)) });
        }, input);

        var explodedPath = Path.Combine(_dir, "exploded.geojson");
        var ex = await TestEnv.RunAsync(new MultipartToSinglepartTool(), new Dictionary<string, string>
        {
            ["input"] = input, ["output"] = explodedPath
        });
        TestEnv.AssertSucceeded(ex);
        var exploded = TestEnv.ReadLayer(explodedPath);
        Assert.Equal(3, exploded.GetFeatureCount());
        Assert.All(exploded.Features, f =>
            Assert.StartsWith("POLYGON", f.Wkt!, StringComparison.OrdinalIgnoreCase));

        var collectedPath = Path.Combine(_dir, "collected.geojson");
        var co = await TestEnv.RunAsync(new SinglepartToMultipartTool(), new Dictionary<string, string>
        {
            ["input"] = explodedPath, ["output"] = collectedPath, ["field"] = ""
        });
        TestEnv.AssertSucceeded(co);
        var collected = TestEnv.ReadLayer(collectedPath);
        Assert.Equal(1, collected.GetFeatureCount());
        // Part count preserved through the round trip (independent of area).
        var geom = GeometryUtil.Wkt2Geometry(collected.Features[0].Wkt!);
        Assert.Equal(3, geom.GetGeometryCount());
        geom.Dispose();
    }

    // ─── 1.3 Derived geometry: vertices / boundary / line→polygon ───

    [Fact]
    public async Task ExtractVertices_EmitsOnePointPerVertex()
    {
        var input = MakeGroupedSquares("verts.shp");
        var output = Path.Combine(_dir, "verts.geojson");
        var result = await TestEnv.RunAsync(new ExtractVerticesTool(), new Dictionary<string, string>
        {
            ["input"] = input, ["output"] = output
        });
        TestEnv.AssertSucceeded(result);

        var layer = TestEnv.ReadLayer(output);
        Assert.Equal(20, layer.GetFeatureCount()); // 4 squares × 5 vertices (ring-closing included)
        Assert.All(layer.Features, f =>
        {
            Assert.StartsWith("POINT", f.Wkt!, StringComparison.OrdinalIgnoreCase);
            var (x, y) = GeometryOps.ExtractCoordinates(f.Wkt).Single();
            Assert.InRange(x, -1, 21);
            Assert.InRange(y, -1, 21);
        });
    }

    [Fact]
    public async Task PolygonToLine_Yields_Ring_Lines()
    {
        var input = Path.Combine(_dir, "pl.geojson");
        TestEnv.WriteLayer(l =>
        {
            l.GeometryType = GeometryType.POLYGON; l.Wkid = 32650;
            l.AddFeature(new OguFeature { Fid = 0, Wkt = Poly(0, 0) });
        }, input);

        var output = Path.Combine(_dir, "pl_lines.geojson");
        var result = await TestEnv.RunAsync(new PolygonToLineTool(), new Dictionary<string, string>
        {
            ["input"] = input, ["output"] = output
        });
        TestEnv.AssertSucceeded(result);
        var layer = TestEnv.ReadLayer(output);
        Assert.Equal(1, layer.GetFeatureCount());
        var boundary = layer.Features[0].Wkt!;
        Assert.Contains("LINESTRING", boundary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(5, GeometryOps.ExtractCoordinates(boundary).Count);
    }

    [Fact]
    public async Task LineToPolygon_Closes_Ring_And_Computes_Known_Area()
    {
        // Closed ring of a 10x10 square (metric) => area exactly 100.
        var input = Path.Combine(_dir, "lp.geojson");
        TestEnv.WriteLayer(l =>
        {
            l.GeometryType = GeometryType.LINESTRING; l.Wkid = 32650;
            l.AddFeature(new OguFeature { Fid = 0, Wkt = $"LINESTRING ({Ring(0, 0)})" });
        }, input);

        var output = Path.Combine(_dir, "lp_polys.geojson");
        var result = await TestEnv.RunAsync(new LineToPolygonTool(), new Dictionary<string, string>
        {
            ["input"] = input, ["output"] = output
        });
        TestEnv.AssertSucceeded(result);
        var layer = TestEnv.ReadLayer(output);
        Assert.Equal(1, layer.GetFeatureCount());
        Assert.InRange(GeometryUtil.AreaWkt(layer.Features[0].Wkt!), 99.9, 100.1);
    }

    // ─── 1.5 Symmetric difference ───

    [Fact]
    public async Task SymDifference_Of_Two_Overlapping_Squares_Has_Expected_Area()
    {
        var a = Path.Combine(_dir, "sa.geojson");
        var b = Path.Combine(_dir, "sb.geojson");
        TestEnv.WriteLayer(l =>
        {
            l.GeometryType = GeometryType.POLYGON; l.Wkid = 32650;
            l.AddFeature(new OguFeature { Fid = 0, Wkt = Poly(0, 0) });
        }, a);
        TestEnv.WriteLayer(l =>
        {
            l.GeometryType = GeometryType.POLYGON; l.Wkid = 32650;
            l.AddFeature(new OguFeature { Fid = 0, Wkt = Poly(5, 5) });
        }, b);

        var output = Path.Combine(_dir, "sym.geojson");
        var result = await TestEnv.RunAsync(new SymDifferenceTool(), new Dictionary<string, string>
        {
            ["input1"] = a, ["input2"] = b, ["output"] = output
        });
        TestEnv.AssertSucceeded(result);
        var layer = TestEnv.ReadLayer(output);
        Assert.Equal(1, layer.GetFeatureCount());
        // 100 + 100 − 2·25 = 150
        Assert.InRange(GeometryUtil.AreaWkt(layer.Features[0].Wkt!), 149.9, 150.1);
    }

    // ─── 1.4 Batch format conversion ───

    [Fact]
    public async Task BatchConvert_Converts_All_Files_And_Preserves_Counts()
    {
        var inDir = TestEnv.Dir("sprint1_batch_in");
        var outDir = TestEnv.Dir("sprint1_batch_out");
        MakeGroupedSquares("layer_a.shp"); // writes under _dir, copy into inDir
        File.Copy(Path.Combine(_dir, "layer_a.shp"), Path.Combine(inDir, "layer_a.shp"), true);
        foreach (var ext in new[] { ".dbf", ".shx", ".prj", ".cpg" })
        {
            var src = Path.Combine(_dir, "layer_a" + ext);
            if (File.Exists(src)) File.Copy(src, Path.Combine(inDir, "layer_a" + ext), true);
        }
        // second independent layer
        var b = Path.Combine(inDir, "layer_b.shp");
        TestEnv.WriteLayer(l =>
        {
            l.GeometryType = GeometryType.POINT; l.Wkid = 32650;
            l.AddFeature(new OguFeature { Fid = 0, Wkt = "POINT (1 2)" });
            l.AddFeature(new OguFeature { Fid = 1, Wkt = "POINT (3 4)" });
        }, b);

        var result = await TestEnv.RunAsync(new BatchConvertTool(), new Dictionary<string, string>
        {
            ["inputFolder"] = inDir, ["outputFolder"] = outDir,
            ["sourceFormat"] = "SHP", ["targetFormat"] = "GeoJSON"
        });
        TestEnv.AssertSucceeded(result);

        var outputs = Directory.GetFiles(outDir, "*.geojson");
        Assert.Equal(2, outputs.Length);
        Assert.Contains(outputs, p => Path.GetFileName(p) == "layer_a.geojson");
        Assert.Equal(4, TestEnv.ReadLayer(Path.Combine(outDir, "layer_a.geojson")).GetFeatureCount());
        Assert.Equal(2, TestEnv.ReadLayer(Path.Combine(outDir, "layer_b.geojson")).GetFeatureCount());
    }

    [Fact]
    public async Task BatchConvert_Throws_When_Source_Equals_Target()
    {
        var inDir = TestEnv.Dir("sprint1_batch_same");
        var result = await TestEnv.RunAsync(new BatchConvertTool(), new Dictionary<string, string>
        {
            ["inputFolder"] = inDir, ["outputFolder"] = inDir,
            ["sourceFormat"] = "SHP", ["targetFormat"] = "SHP"
        });
        Assert.False(result.Success);
    }

    public void Dispose() { /* %TEMP% scratch is OS-cleaned */ }
}
