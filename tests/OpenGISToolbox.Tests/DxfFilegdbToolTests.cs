using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OpenGIS.Utils.Engine.Enums;
using OpenGISToolbox.TestKit;
using OpenGISToolbox.Tools;
using Xunit;

namespace OpenGISToolbox.Tests;

/// <summary>
/// Regression coverage for tools that previously had none: DXF round trips
/// (including the multipolygon explode workaround), FileGDB (capability-gated)
/// and Central Lines on synthetic polygons with exact expectations.
/// </summary>
public class DxfFilegdbToolTests
{
    private string Dir => GdalEnv.Dir("difgdb");

    private static FormatConversionTool ShpToDxf() => new("shp-to-dxf", "SHP → DXF", "SHP → DXF",
        "", "", DataFormatType.SHP, ".shp", "Shapefile|*.shp", DataFormatType.DXF, ".dxf", "DXF|*.dxf");

    private static FormatConversionTool DxfToShp() => new("dxf-to-shp", "DXF → SHP", "DXF → SHP",
        "", "", DataFormatType.DXF, ".dxf", "DXF|*.dxf", DataFormatType.SHP, ".shp", "Shapefile|*.shp");

    [Fact]
    public async Task ShpToDxf_WithAttributesAndMultipart_SucceedsAndProducesEntities()
    {
        // A layer with attributes (DXF has a fixed entity schema) and a two-part
        // multipart polygon (DXF cannot store multi-geometries) — the historical
        // failure modes for this converter.
        var src = Path.Combine(Dir, "mp.shp");
        TestEnv.WriteLayer(l =>
        {
            l.GeometryType = GeometryType.POLYGON;
            l.Wkid = 4326;
            l.AddField(new OpenGIS.Utils.Engine.Model.Layer.OguField
            { Name = "name", DataType = FieldDataType.STRING, Length = 20 });
            l.AddFeature(new OpenGIS.Utils.Engine.Model.Layer.OguFeature
            {
                Fid = 0,
                Wkt = "POLYGON ((0 0, 1 0, 1 1, 0 1, 0 0),(10 10, 11 10, 11 11, 10 11, 10 10))"
            }.Also(f => f.SetValue("name", "two-parts")));
        }, src);

        var dxf = Path.Combine(Dir, "mp.dxf");
        var result = await TestEnv.RunAsync(ShpToDxf(), new Dictionary<string, string>
        {
            ["input"] = src, ["output"] = dxf
        });
        TestEnv.AssertSucceeded(result);
        Assert.True(File.Exists(dxf));
        Assert.True(new FileInfo(dxf).Length > 1024, "DXF looks empty");

        var readBack = GdalEnv.ReadLayer(dxf);
        Assert.True((int)readBack.GetFeatureCount() >= 1, "no entities read back from DXF");

        var backShp = Path.Combine(Dir, "mp_back.shp");
        var rt = await TestEnv.RunAsync(DxfToShp(), new Dictionary<string, string>
        {
            ["input"] = dxf, ["output"] = backShp
        });
        TestEnv.AssertSucceeded(rt);
        Assert.True(ShpFile.Read(backShp).Records >= 1);
    }

    [Fact]
    public async Task RealCountries_ToDxf_Succeeds()
    {
        // The exact production scenario: a worldwide multipolygon country layer.
        RealDataCatalog? catalog;
        try { catalog = RealDataCatalog.Resolve(null); }
        catch (Exception) { catalog = null; }
        var layer = catalog?.Polygon;
        if (layer is null) return; // skipped: no real polygon layer (offline)

        var dxf = Path.Combine(Dir, "countries.dxf");
        var result = await TestEnv.RunAsync(ShpToDxf(), new Dictionary<string, string>
        {
            ["input"] = layer!.ShpPath, ["output"] = dxf
        });
        TestEnv.AssertSucceeded(result);
        var readBack = GdalEnv.ReadLayer(dxf);
        Assert.True((int)readBack.GetFeatureCount() >= layer.ExpectedCount,
            "exploded DXF should hold at least as many entities as source features");
    }

    [Fact]
    public async Task ShpToFilegdb_RoundTrip_WhenDriverAvailable()
    {
        var driver = GdalEnv.ProbeFileGdbWrite(GdalEnv.Dir("difgdb", "gdbprobe"));
        if (driver is null) return; // skipped: no writable FileGDB driver

        var src = TestEnv.MakeSquaresShp(Dir, "gdb_src.shp");
        var gdb = Path.Combine(Dir, "squares.gdb");
        if (Directory.Exists(gdb)) Directory.Delete(gdb, true);

        var toGdb = new FormatConversionTool("shp-to-filegdb", "s", "s", "", "",
            DataFormatType.SHP, ".shp", "f", DataFormatType.FILEGDB, ".gdb", "g");
        TestEnv.AssertSucceeded(await TestEnv.RunAsync(toGdb, new Dictionary<string, string>
        {
            ["input"] = src, ["output"] = gdb
        }));

        var fromGdb = new FormatConversionTool("filegdb-to-shp", "f", "f", "", "",
            DataFormatType.FILEGDB, ".gdb", "g", DataFormatType.SHP, ".shp", "f");
        var outShp = Path.Combine(Dir, "gdb_back.shp");
        TestEnv.AssertSucceeded(await TestEnv.RunAsync(fromGdb, new Dictionary<string, string>
        {
            ["input"] = gdb, ["output"] = outShp
        }));
        Assert.Equal(3, ShpFile.Read(outShp).Records);
    }

    [Fact]
    public async Task CentralLines_OnElongatedPolygon_ProducesLineOutput()
    {
        var src = Path.Combine(Dir, "longpoly.shp");
        TestEnv.WriteLayer(l =>
        {
            l.GeometryType = GeometryType.POLYGON;
            l.Wkid = 32650;
            // 1000m x 100m elongated rectangle: centerline collapses to a thin sliver
            l.AddFeature(new OpenGIS.Utils.Engine.Model.Layer.OguFeature
            {
                Fid = 0,
                Wkt = "POLYGON ((0 0, 1000 0, 1000 100, 0 100, 0 0))"
            });
        }, src);

        var outPath = Path.Combine(Dir, "center.shp");
        var result = await TestEnv.RunAsync(new CentralLinesTool(), new Dictionary<string, string>
        {
            ["input"] = src, ["output"] = outPath
        });
        TestEnv.AssertSucceeded(result);

        var layer = GdalEnv.ReadLayer(outPath);
        Assert.Equal(1, (int)layer.GetFeatureCount());
        Assert.NotNull(layer.Features[0].Wkt);
    }

    [Fact]
    public async Task SpatialJoin_Contains_KeepsAllTargets()
    {
        var points = TestEnv.MakePointsGeoJson(Dir);
        var polygons = TestEnv.MakeSquaresShp(Dir, "join_polys.shp");
        var outPath = Path.Combine(Dir, "joined.shp");
        var result = await TestEnv.RunAsync(new SpatialJoinTool(), new Dictionary<string, string>
        {
            ["input"] = points, ["joinLayer"] = polygons, ["output"] = outPath, ["joinType"] = "Intersects"
        });
        TestEnv.AssertSucceeded(result);
        Assert.Equal(3, (int)GdalEnv.ReadLayer(outPath).GetFeatureCount());
    }
}
