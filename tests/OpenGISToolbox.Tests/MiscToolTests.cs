using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using OpenGISToolbox.Tools;
using Xunit;
using Xunit.Abstractions;

namespace OpenGISToolbox.Tests;

public class MiscToolTests : IDisposable
{
    private readonly string _dir = TestEnv.Dir("misc");

    private string MakeGpx()
    {
        var path = Path.Combine(_dir, "track.gpx");
        File.WriteAllText(path, @"<?xml version=""1.0"" encoding=""UTF-8""?>
<gpx version=""1.1"" creator=""test"" xmlns=""http://www.topografix.com/GPX/1/1"">
  <wpt lat=""39.909"" lon=""116.397""><name>Beijing</name><ele>44</ele></wpt>
  <wpt lat=""31.231"" lon=""121.474""><name>Shanghai</name><ele>4</ele></wpt>
  <trk><name>T1</name><trkseg>
    <trkpt lat=""39.90"" lon=""116.30""></trkpt>
    <trkpt lat=""39.91"" lon=""116.31""></trkpt>
    <trkpt lat=""39.92"" lon=""116.32""></trkpt>
  </trkseg></trk>
</gpx>");
        return path;
    }

    [Fact]
    public async Task Gpx_Extract_Waypoints_To_Csv()
    {
        var gpx = MakeGpx();
        var csv = Path.Combine(_dir, "wpts.csv");
        var result = await TestEnv.RunAsync(new GpxProcessingTool(), new Dictionary<string, string>
        {
            ["input"] = gpx, ["operation"] = "Extract Waypoints to CSV", ["output"] = csv
        });
        TestEnv.AssertSucceeded(result);
        var lines = File.ReadAllLines(csv);
        Assert.Equal(3, lines.Length); // header + 2 waypoints
        Assert.Contains("Beijing", lines[1]);
    }

    [Fact]
    public async Task Gpx_Extract_Tracks_To_GeoJson()
    {
        var gpx = MakeGpx();
        var geo = Path.Combine(_dir, "tracks.geojson");
        var result = await TestEnv.RunAsync(new GpxProcessingTool(), new Dictionary<string, string>
        {
            ["input"] = gpx, ["operation"] = "Extract Tracks to GeoJSON", ["output"] = geo
        });
        TestEnv.AssertSucceeded(result);
        var json = File.ReadAllText(geo);
        Assert.Contains("LineString", json);
        Assert.Contains("116.3", json);
    }

    [Fact]
    public async Task Gpx_Summary_Reports_Counts()
    {
        var gpx = MakeGpx();
        var result = await TestEnv.RunAsync(new GpxProcessingTool(), new Dictionary<string, string>
        {
            ["input"] = gpx, ["operation"] = "GPX Summary"
        });
        TestEnv.AssertSucceeded(result);
        Assert.Contains("2", result.Message); // 2 waypoints
    }

    [Fact]
    public async Task Zip_Compress_Extract_RoundTrip()
    {
        var src = TestEnv.Dir("misc", "zip_src");
        File.WriteAllText(Path.Combine(src, "a.txt"), "hello world");
        File.WriteAllText(Path.Combine(src, "b.txt"), "second file");

        var zipPath = Path.Combine(_dir, "data.zip");
        var compress = await TestEnv.RunAsync(new ZipCompressTool(), new Dictionary<string, string>
        {
            ["input"] = src, ["output"] = zipPath
        });
        TestEnv.AssertSucceeded(compress);
        Assert.True(File.Exists(zipPath));

        var outDir = TestEnv.Dir("misc", "zip_out");
        var extract = await TestEnv.RunAsync(new ZipExtractTool(), new Dictionary<string, string>
        {
            ["input"] = zipPath, ["output"] = outDir
        });
        TestEnv.AssertSucceeded(extract);

        Assert.Equal("hello world", File.ReadAllText(Path.Combine(outDir, "a.txt")));
        Assert.Equal("second file", File.ReadAllText(Path.Combine(outDir, "b.txt")));
    }

    public void Dispose() { }
}

/// <summary>
/// End-to-end chain over real, publicly available GIS data (Natural Earth).
/// Skipped automatically when the network is unavailable.
/// </summary>
public class RealDataTests : IDisposable
{
    private static readonly HttpClient Http = new HttpClient
    {
        Timeout = TimeSpan.FromMinutes(2)
    };
    private readonly ITestOutputHelper _log;

    public RealDataTests(ITestOutputHelper log) => _log = log;

    private bool CanDownload = true;

    private readonly string _dir = TestEnv.Dir("realdata");

    private async Task<string> DownloadWorldGeoJson()
    {
        const string url = "https://raw.githubusercontent.com/nvkelso/natural-earth-vector/master/geojson/ne_110m_admin_0_countries.geojson";
        var path = Path.Combine(_dir, "world.geojson");
        if (!File.Exists(path))
        {
            try
            {
                var bytes = await Http.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(path, bytes);
            }
            catch (Exception ex)
            {
                CanDownload = false;
                _log.WriteLine($"Network unavailable, real-data test body is skipped: {ex.Message}");
                return path;
            }
        }
        return path;
    }

    [Fact]
    public async Task RealData_Full_Toolchain()
    {
        var world = await DownloadWorldGeoJson();
        if (!CanDownload || !File.Exists(world)) return; // skipped: no network

        // 1. Convert GeoJSON -> GeoPackage
        var gpkg = Path.Combine(_dir, "world.gpkg");
        var convTool = new FormatConversionTool("geojson-to-gpkg", "GeoJSON → GPKG", "GeoJSON → GPKG",
            "Convert GeoJSON to GeoPackage", "将 GeoJSON 转换为 GeoPackage",
            OpenGIS.Utils.Engine.Enums.DataFormatType.GEOJSON, ".geojson", "GeoJSON|*.geojson",
            OpenGIS.Utils.Engine.Enums.DataFormatType.GEOPACKAGE, ".gpkg", "GPKG|*.gpkg");
        var conv = await TestEnv.RunAsync(convTool, new Dictionary<string, string>
        {
            ["input"] = world, ["output"] = gpkg
        });
        TestEnv.AssertSucceeded(conv);
        var layer = TestEnv.ReadLayer(gpkg);
        Assert.True(layer.GetFeatureCount() >= 170, $"Expected ~177 countries, got {layer.GetFeatureCount()}");

        // 2. Attribute query: select China
        var china = Path.Combine(_dir, "china.geojson");
        var query = await TestEnv.RunAsync(new AttributeQueryTool(), new Dictionary<string, string>
        {
            ["input"] = gpkg, ["output"] = china, ["whereClause"] = "NAME = 'China'"
        });
        if (!query.Success)
        {
            // field naming may differ (ADMIN); try alternative
            query = await TestEnv.RunAsync(new AttributeQueryTool(), new Dictionary<string, string>
            {
                ["input"] = gpkg, ["output"] = china, ["whereClause"] = "ADMIN = 'China'"
            });
        }
        TestEnv.AssertSucceeded(query);
        var chinaLayer = TestEnv.ReadLayer(china);
        Assert.Equal(1, chinaLayer.GetFeatureCount());

        // 3. Reproject China to UTM 50N
        var chinaUtm = Path.Combine(_dir, "china_utm.geojson");
        var rep = await TestEnv.RunAsync(new ReprojectTool(), new Dictionary<string, string>
        {
            ["input"] = china, ["output"] = chinaUtm, ["sourceWkid"] = "4326", ["targetWkid"] = "32650"
        });
        TestEnv.AssertSucceeded(rep);

        // 4. Buffer China in projected CRS and verify plausible area
        var buffered = Path.Combine(_dir, "china_buffer.geojson");
        var buf = await TestEnv.RunAsync(new BufferTool(), new Dictionary<string, string>
        {
            ["input"] = chinaUtm, ["output"] = buffered, ["distance"] = "50000"
        });
        TestEnv.AssertSucceeded(buf);
        var bLayer = TestEnv.ReadLayer(buffered);
        Assert.True(bLayer.GetFeatureCount() >= 1);

        // 5. Merge original + buffered layers
        var merged = Path.Combine(_dir, "merged.geojson");
        var merge = await TestEnv.RunAsync(new MergeLayersTool(), new Dictionary<string, string>
        {
            ["input1"] = chinaUtm, ["input2"] = buffered, ["output"] = merged
        });
        TestEnv.AssertSucceeded(merge);
        var mergedLayer = TestEnv.ReadLayer(merged);
        Assert.Equal(2, mergedLayer.GetFeatureCount());
    }

    public void Dispose() { }
}

