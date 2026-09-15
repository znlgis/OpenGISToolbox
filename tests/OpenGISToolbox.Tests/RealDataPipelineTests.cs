using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OpenGISToolbox.TestKit;
using Xunit;

namespace OpenGISToolbox.Tests;

/// <summary>
/// End-to-end tests on real-world vector data (Natural Earth 1:50m by default,
/// or any .shp/.dbf set via OGT_REAL_DATA_DIR). Expectations are derived
/// independently from file headers — never hardcoded per dataset.
/// When the data source cannot be obtained (offline, no directory) the data-
/// dependent assertions are skipped via early return, matching the project's
/// existing RealDataTests convention (xunit v2 has no public skip API).
/// </summary>
public class RealDataPipelineTests
{
    private static readonly Lazy<RealDataCatalog?> CatalogLazy = new(() =>
    {
        try { return RealDataCatalog.Resolve(null); }
        catch (Exception) { return null; }
    });

    private static RealDataCatalog? Catalog => CatalogLazy.Value;

    private static RealLayer? Polygon => Catalog?.Polygon;

    private string Dir => GdalEnv.Dir("realdata");

    [Fact]
    public void Catalog_Integrity_CountsAndTypes_AgreeAcrossIndependentParsers()
    {
        var catalog = Catalog;
        if (catalog is null) return; // skipped: no real data source available

        Assert.NotEmpty(catalog.Layers);
        foreach (var layer in catalog.Layers)
        {
            var gdalLayer = GdalEnv.ReadLayer(layer.ShpPath);
            Assert.Equal(layer.Shp.Records, layer.Dbf.Records);              // .shp chain vs .dbf header
            Assert.Equal(layer.Shp.Records, (int)gdalLayer.GetFeatureCount()); // vs GDAL reader
            if (layer.GeoJson != null)
                Assert.Equal(layer.GeoJson.FeatureCount, layer.Shp.Records); // vs source JSON

            var first = gdalLayer.Features.FirstOrDefault(f => !string.IsNullOrEmpty(f.Wkt));
            Assert.NotNull(first);
            Assert.StartsWith(layer.Shp.ShapeTypeName, first!.Wkt, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData("shp-to-geojson", ".geojson")]
    [InlineData("shp-to-gpkg", ".gpkg")]
    [InlineData("shp-to-kml", ".kml")]
    public async Task Conversion_OfRealLayer_PreservesAllFeatures(string toolId, string ext)
    {
        var layer = Polygon;
        if (layer is null) return; // skipped

        var output = Path.Combine(Dir, toolId + ext);
        var info = OpenGISToolbox.Services.ToolRegistry.GetAllTools().Single(t => t.Id == toolId);
        var result = await GdalEnv.RunToolAsync(info, new Dictionary<string, string>
        {
            ["input"] = layer!.ShpPath, ["output"] = output
        });
        Assert.True(result.Success, result.Message);
        Assert.True(File.Exists(output));

        var converted = GdalEnv.ReadLayer(output);
        Assert.Equal(layer!.ExpectedCount, (int)converted.GetFeatureCount());
    }

    [Fact]
    public async Task GeoJsonConversion_Output_ParsesIndependentlyAsGeoJson()
    {
        var layer = Polygon;
        if (layer is null) return; // skipped

        var output = Path.Combine(Dir, "countries.geojson");
        var info = OpenGISToolbox.Services.ToolRegistry.GetAllTools().Single(t => t.Id == "shp-to-geojson");
        Assert.True((await GdalEnv.RunToolAsync(info, new Dictionary<string, string>
        {
            ["input"] = layer!.ShpPath, ["output"] = output
        })).Success);

        var gj = GeoJsonFile.Read(output);
        Assert.Equal(layer!.ExpectedCount, gj.FeatureCount);
    }

    [Fact]
    public async Task AttributeQuery_OnRealData_ExactExpectedCount_FromDbfScan()
    {
        var layer = Polygon;
        if (layer is null) return; // skipped
        var pair = layer!.PickAsciiWherePair();
        if (pair == null) return; // skipped: no encoding-stable repeated value

        var output = Path.Combine(Dir, "query.shp");
        var info = OpenGISToolbox.Services.ToolRegistry.GetAllTools().Single(t => t.Id == "attribute-query");
        var result = await GdalEnv.RunToolAsync(info, new Dictionary<string, string>
        {
            ["input"] = layer!.ShpPath, ["output"] = output,
            ["whereClause"] = $"{pair.Value.Field} = '{pair.Value.Value.Replace("'", "''")}'"
        });
        Assert.True(result.Success, result.Message);

        var queried = GdalEnv.ReadLayer(output);
        Assert.Equal(pair.Value.Matches, (int)queried.GetFeatureCount());
    }

    [Fact]
    public async Task Reproject_RoundTrip_OnRealData_CoordinatesPreserved()
    {
        var layer = Polygon;
        if (layer is null) return; // skipped

        var metric = Path.Combine(Dir, "reproject_3857.shp");
        var back = Path.Combine(Dir, "reproject_back.shp");
        var reproject = OpenGISToolbox.Services.ToolRegistry.GetAllTools().Single(t => t.Id == "reproject");

        Assert.True((await GdalEnv.RunToolAsync(reproject, new Dictionary<string, string>
        {
            ["input"] = layer!.ShpPath, ["output"] = metric,
            ["sourceWkid"] = "4326", ["targetWkid"] = "3857"
        })).Success);
        var forward = GdalEnv.ReadLayer(metric);
        Assert.Equal(layer!.ExpectedCount, (int)forward.GetFeatureCount());

        Assert.True((await GdalEnv.RunToolAsync(reproject, new Dictionary<string, string>
        {
            ["input"] = metric, ["output"] = back,
            ["sourceWkid"] = "3857", ["targetWkid"] = "4326"
        })).Success);

        var srcFp = LayerCompare.GeometryFingerprint(GdalEnv.ReadLayer(layer!.ShpPath));
        var rtFp = LayerCompare.GeometryFingerprint(GdalEnv.ReadLayer(back));
        Assert.Equal(srcFp.Count, rtFp.Count);
        var relErr = Math.Max(
            Math.Abs(rtFp.SumX - srcFp.SumX) / Math.Max(1, Math.Abs(srcFp.SumX)),
            Math.Abs(rtFp.SumY - srcFp.SumY) / Math.Max(1, Math.Abs(srcFp.SumY)));
        Assert.True(relErr < 1e-6, $"round trip coordinate drift too large: {relErr:E2}");
    }

    [Fact]
    public async Task SpatialFilter_FullExtent_KeepsAll_FarExtent_KeepsNone()
    {
        var layer = Polygon;
        if (layer is null) return; // skipped

        var filter = OpenGISToolbox.Services.ToolRegistry.GetAllTools().Single(t => t.Id == "spatial-filter");

        var all = Path.Combine(Dir, "filter_all.shp");
        Assert.True((await GdalEnv.RunToolAsync(filter, new Dictionary<string, string>
        {
            ["input"] = layer!.ShpPath, ["output"] = all, ["extentWkt"] = layer!.BboxAsExtentWkt(1.0)
        })).Success);
        Assert.Equal(layer!.ExpectedCount, (int)GdalEnv.ReadLayer(all).GetFeatureCount());

        var yrange = Math.Max(1e-9, layer!.Bbox[3] - layer!.Bbox[1]);
        var shift = 3 * yrange + 10;
        var far = $"POLYGON (({layer!.Bbox[0]} {layer!.Bbox[1] + shift}, {layer!.Bbox[2]} {layer!.Bbox[1] + shift}, {layer!.Bbox[2]} {layer!.Bbox[3] + shift}, {layer!.Bbox[0]} {layer!.Bbox[3] + shift}, {layer!.Bbox[0]} {layer!.Bbox[1] + shift}))";
        var none = Path.Combine(Dir, "filter_none.shp");
        Assert.True((await GdalEnv.RunToolAsync(filter, new Dictionary<string, string>
        {
            ["input"] = layer!.ShpPath, ["output"] = none, ["extentWkt"] = far
        })).Success);
        Assert.Equal(0, (int)GdalEnv.ReadLayer(none).GetFeatureCount());
    }

    [Fact]
    public async Task SplitLayer_OnRealData_PiecesSumToSourceCount()
    {
        var layer = Polygon;
        if (layer is null) return; // skipped
        var splitField = layer!.PickSplitField();
        if (splitField == null) return; // skipped: no fully-populated ASCII field

        var outDir = Path.Combine(Dir, "split_out");
        var info = OpenGISToolbox.Services.ToolRegistry.GetAllTools().Single(t => t.Id == "split-layer");
        var result = await GdalEnv.RunToolAsync(info, new Dictionary<string, string>
        {
            ["input"] = layer!.ShpPath, ["fieldName"] = splitField.Value.Field, ["outputFolder"] = outDir
        });
        Assert.True(result.Success, result.Message);

        var total = Directory
            .EnumerateFiles(outDir, "*.shp")
            .Where(shp => File.Exists(Path.ChangeExtension(shp, ".dbf")))
            .Sum(shp => ShpFile.Read(shp).Records);
        Assert.Equal(layer!.ExpectedCount, total);
    }

    [Fact]
    public async Task CalculateArea_OnRealData_ReportsPositiveTotal()
    {
        var layer = Polygon;
        if (layer is null) return; // skipped

        var info = OpenGISToolbox.Services.ToolRegistry.GetAllTools().Single(t => t.Id == "calculate-area");
        var result = await GdalEnv.RunToolAsync(info, new Dictionary<string, string> { ["input"] = layer!.ShpPath });
        Assert.True(result.Success, result.Message);
        Assert.Contains("Total Area", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Total Area: 0.000000", result.Message);
    }

    [Fact]
    public async Task MergeLayers_RealLayerWithItself_DoublesFeatureCount()
    {
        var layer = Polygon;
        if (layer is null) return; // skipped

        var copy = Path.Combine(Dir, "merge_copy.shp");
        foreach (var ext in new[] { ".shp", ".dbf", ".shx", ".prj", ".cpg" })
        {
            var f = Path.ChangeExtension(layer!.ShpPath, ext);
            if (File.Exists(f)) File.Copy(f, Path.ChangeExtension(copy, ext), overwrite: true);
        }
        var output = Path.Combine(Dir, "merged.shp");
        var info = OpenGISToolbox.Services.ToolRegistry.GetAllTools().Single(t => t.Id == "merge-layers");
        var result = await GdalEnv.RunToolAsync(info, new Dictionary<string, string>
        {
            ["input1"] = layer!.ShpPath, ["input2"] = copy, ["output"] = output
        });
        Assert.True(result.Success, result.Message);
        Assert.Equal(2 * layer!.ExpectedCount, (int)GdalEnv.ReadLayer(output).GetFeatureCount());
    }
}
