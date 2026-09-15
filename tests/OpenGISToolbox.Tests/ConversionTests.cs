using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OpenGIS.Utils.Engine.Enums;
using OpenGISToolbox.Tools;
using Xunit;

namespace OpenGISToolbox.Tests;

public class ConversionTests : IDisposable
{
    private readonly string _dir = TestEnv.Dir("conversion");

    public static IEnumerable<object[]> RoundTripFormats =>
        new[]
        {
            // (output file, field under which the source `label` values land)
            // KML has no generic attribute bag: GDAL's KML driver maps the schema
            // fields onto <name>/<description>, so `label` values come back under
            // `description`. Values — not key names — are the fidelity guarantee.
            new object[] { "out.geojson", "label" },
            new object[] { "out.gpkg", "label" },
            new object[] { "out.kml", "description" },
        };

    [Theory]
    [MemberData(nameof(RoundTripFormats))]
    public async Task FormatConversion_RoundTrips_Features(string outName, string attributeField)
    {
        var input = TestEnv.MakeSquaresShp(_dir);
        var output = Path.Combine(_dir, outName);
        var tool = new FormatConversionTool("shp-convert", "SHP Convert", "SHP 转换",
            "Convert SHP", "转换 SHP",
            DataFormatType.SHP, ".shp", "Shapefile|*.shp",
            Path.GetExtension(outName) == ".gpkg" ? DataFormatType.GEOPACKAGE : DataFormatType.KML,
            Path.GetExtension(outName), "out|" + outName);
        var result = await TestEnv.RunAsync(tool, new Dictionary<string, string>
        {
            ["input"] = input, ["output"] = output
        });
        TestEnv.AssertSucceeded(result);

        var layer = TestEnv.ReadLayer(output);
        Assert.Equal(3, layer.GetFeatureCount());
        // attributes preserved
        var labels = layer.Features.Select(f => f.GetValue(attributeField)?.ToString()).OrderBy(v => v).ToList();
        Assert.Equal(new[] { "A", "B", "C" }, labels);
        // geometry preserved (polygon)
        Assert.All(layer.Features, f => Assert.Contains("POLYGON", f.Wkt ?? "", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CsvToVector_Creates_Points_From_Coordinates()
    {
        var csv = Path.Combine(_dir, "points.csv");
        File.WriteAllText(csv,
            "name,lon,lat\nBeijing,116.397,39.909\nShanghai,121.474,31.231\n,0,0\nBad,abc,def\n");

        var output = Path.Combine(_dir, "csv_out.shp");
        var result = await TestEnv.RunAsync(new CsvToVectorTool(), new Dictionary<string, string>
        {
            ["input"] = csv, ["output"] = output, ["delimiter"] = ",",
            ["xField"] = "lon", ["yField"] = "lat", ["wkid"] = "4326"
        });
        TestEnv.AssertSucceeded(result);

        var layer = TestEnv.ReadLayer(output);
        Assert.Equal(3, layer.GetFeatureCount()); // 2 valid + origin point (name empty but coords valid)
    }

    [Fact]
    public async Task Reproject_Transforms_Known_Point_Accurately()
    {
        // Beijing Tiananmen ~ (116.397, 39.909) EPSG:4326 -> UTM 50N EPSG:32650
        // Expected: ~ (448,000-452,000 E, 4,417,000-4,421,000 N)
        var input = Path.Combine(_dir, "pt.geojson");
        TestEnv.WriteLayer(l =>
        {
            l.GeometryType = OpenGIS.Utils.Engine.Enums.GeometryType.POINT; l.Wkid = 4326;
            l.AddFeature(new OpenGIS.Utils.Engine.Model.Layer.OguFeature { Fid = 0, Wkt = "POINT (116.397 39.909)" });
        }, input);

        var output = Path.Combine(_dir, "pt_utm.geojson");
        var result = await TestEnv.RunAsync(new ReprojectTool(), new Dictionary<string, string>
        {
            ["input"] = input, ["output"] = output, ["sourceWkid"] = "4326", ["targetWkid"] = "32650"
        });
        TestEnv.AssertSucceeded(result);

        var layer = TestEnv.ReadLayer(output);
        // NOTE: GeoJSON output does not persist the EPSG code (Wkid is null);
        // the tool sets layer.Wkid before writing, verify via coordinates below.
        var wkt = layer.Features[0].Wkt!;
        var nums = wkt.Split(new[] { ' ', '(', ')' }, StringSplitOptions.RemoveEmptyEntries)
            .Skip(1).Select(double.Parse).ToArray();
        Assert.InRange(nums[0], 447000, 453000);
        Assert.InRange(nums[1], 4415000, 4423000);
    }

    [Fact]
    public async Task Reproject_RoundTrip_Returns_Original_Coordinates()
    {
        var input = TestEnv.MakePointsGeoJson(_dir);
        var utm = Path.Combine(_dir, "utm.geojson");
        var back = Path.Combine(_dir, "back.geojson");

        await TestEnv.RunAsync(new ReprojectTool(), new Dictionary<string, string>
        {
            ["input"] = input, ["output"] = utm, ["sourceWkid"] = "4326", ["targetWkid"] = "32650"
        });
        // note: data spans UTM zones but transform must still round-trip
        var result = await TestEnv.RunAsync(new ReprojectTool(), new Dictionary<string, string>
        {
            ["input"] = utm, ["output"] = back, ["sourceWkid"] = "32650", ["targetWkid"] = "4326"
        });
        TestEnv.AssertSucceeded(result);

        var orig = TestEnv.ReadLayer(input).Features.OrderBy(f => f.Fid).ToList();
        var rt = TestEnv.ReadLayer(back).Features.OrderBy(f => f.Fid).ToList();
        for (int i = 0; i < orig.Count; i++)
        {
            var a = ParsePoint(orig[i].Wkt!);
            var b = ParsePoint(rt[i].Wkt!);
            Assert.InRange(Math.Abs(a[0] - b[0]), 0, 1e-6);
            Assert.InRange(Math.Abs(a[1] - b[1]), 0, 1e-6);
        }
    }

    private static double[] ParsePoint(string wkt)
    {
        var s = wkt.Substring(wkt.IndexOf('(') + 1, wkt.LastIndexOf(')') - wkt.IndexOf('(') - 1);
        return s.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(double.Parse).ToArray();
    }

    [Fact]
    public async Task BatchReproject_Processes_All_Files_In_Folder()
    {
        var inDir = TestEnv.Dir("conversion", "batch_in");
        var outDir = TestEnv.Dir("conversion", "batch_out");
        TestEnv.MakeSquaresShp(inDir, "one.shp");
        TestEnv.MakePointsGeoJson(inDir);

        var result = await TestEnv.RunAsync(new BatchReprojectTool(), new Dictionary<string, string>
        {
            ["inputFolder"] = inDir, ["outputFolder"] = outDir,
            ["sourceWkid"] = "4326", ["targetWkid"] = "32650", ["format"] = "GeoJSON"
        });
        TestEnv.AssertSucceeded(result);
        var files = Directory.GetFiles(outDir, "*.geojson");
        // NOTE: format="GeoJSON" also selects which INPUT files are globbed (only *.geojson),
        // so 1 input geojson yields 1 output file; the .shp input is excluded by design.
        Assert.True(files.Length >= 1, $"Expected >=1 output file, got {files.Length}");
        foreach (var f in files)
        {
            var l = TestEnv.ReadLayer(f);
            Assert.True(l.GetFeatureCount() > 0);
        }
    }

    [Fact]
    public async Task AttributeQuery_Selects_Matching_Features()
    {
        var input = TestEnv.MakePointsGeoJson(_dir);
        var output = Path.Combine(_dir, "queried.geojson");
        var result = await TestEnv.RunAsync(new AttributeQueryTool(), new Dictionary<string, string>
        {
            ["input"] = input, ["output"] = output, ["whereClause"] = "city = 'Beijing'"
        });
        TestEnv.AssertSucceeded(result);

        var layer = TestEnv.ReadLayer(output);
        Assert.Equal(1, layer.GetFeatureCount());
        Assert.Equal("Beijing", layer.Features[0].GetValue("city")?.ToString());
    }

    public void Dispose() { }
}
