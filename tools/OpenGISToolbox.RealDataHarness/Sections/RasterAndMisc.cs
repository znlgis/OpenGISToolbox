using OpenGISToolbox.TestKit;

namespace OpenGISToolbox.RealDataHarness.Sections;

/// <summary>Raster, GPS, CSV, ZIP tools — synthetic fixtures with exact expectations.</summary>
public static class RasterAndMisc
{
    public static async Task Run()
    {
        Program.Checks.BeginSection("7. Raster / GPS / CSV / ZIP sweep");

        var dir = GdalEnv.Dir("harness", "raster_misc");
        var tif = Synth.MakeGeoTiff(dir);

        await Program.Checks.RunAsync("raster-calculator threshold", async () =>
        {
            var outPath = Path.Combine(dir, "thresh.tif");
            await Program.RunRegisteredAsync("raster-calculator", new()
            {
                ["input"] = tif, ["output"] = outPath,
                ["operation"] = "Threshold (Band1 > Value)", ["value"] = "32"
            }, "raster-calculator threshold");
            var data = Synth.ReadBand(outPath);
            var ones = data.Count(v => v == 1);
            Program.Require(ones == 32, "threshold >32 yields exactly 32 ones", $"got {ones} of {data.Length}");
        });

        await Program.Checks.RunAsync("raster-calculator NDVI", async () =>
        {
            var outPath = Path.Combine(dir, "ndvi.tif");
            await Program.RunRegisteredAsync("raster-calculator", new()
            {
                ["input"] = tif, ["output"] = outPath,
                ["operation"] = "NDVI (Band4-Band3)/(Band4+Band3)"
            }, "raster-calculator ndvi");
            var data = Synth.ReadBand(outPath);
            // red=2, nir=8 → NDVI = (8-2)/(8+2) = 0.6 for every pixel
            Program.Require(data.All(v => Math.Abs(v - 0.6) < 1e-6), "NDVI constant 0.6",
                $"min={data.Min():F4} max={data.Max():F4}");
        });

        await Program.Checks.RunAsync("raster-calculator scale/offset", async () =>
        {
            var scaleOut = Path.Combine(dir, "scale.tif");
            await Program.RunRegisteredAsync("raster-calculator", new()
            {
                ["input"] = tif, ["output"] = scaleOut,
                ["operation"] = "Scale (Band1 * Factor)", ["value"] = "3"
            }, "raster-calculator scale");
            var scaled = Synth.ReadBand(scaleOut);
            var original = Synth.ReadBand(tif);
            Program.Require(scaled.Zip(original).All(p =>
                p.First == 0 ? true : Math.Abs(p.First - p.Second * 3) < 1e-6),
                "scale multiplies band1 by 3", "values off");

            var offsetOut = Path.Combine(dir, "offset.tif");
            await Program.RunRegisteredAsync("raster-calculator", new()
            {
                ["input"] = tif, ["output"] = offsetOut,
                ["operation"] = "Offset (Band1 + Value)", ["value"] = "10"
            }, "raster-calculator offset");
            var offset = Synth.ReadBand(offsetOut);
            // band1 = (i+1)%256, so offset wraps 255→265 unless clamped; check exact +10 arithmetic
            Program.Require(offset.Zip(original).All(p => Math.Abs(p.First - (p.Second + 10)) < 1e-6),
                "offset adds 10", "values off");
        });

        await Program.Checks.RunAsync("raster-format-convert", async () =>
        {
            var outPath = Path.Combine(dir, "converted.tif");
            await Program.RunRegisteredAsync("raster-format-convert", new()
            {
                ["input"] = tif, ["output"] = outPath
            }, "raster-format-convert");
            Program.Require(File.Exists(outPath) && new FileInfo(outPath).Length > 0,
                "converted raster exists", "missing");
            GdalEnv.Ensure();
            using var ds = OSGeo.GDAL.Gdal.Open(outPath, OSGeo.GDAL.Access.GA_ReadOnly);
            Program.Require(ds.RasterXSize == 8 && ds.RasterYSize == 8 && ds.RasterCount == 4,
                "converted raster dimensions", $"{ds.RasterXSize}x{ds.RasterYSize} b{ds.RasterCount}");
        });

        // CSV → vector with exact expected count (one malformed row must be skipped).
        await Program.Checks.RunAsync("csv-to-vector", async () =>
        {
            var csv = Path.Combine(dir, "pts.csv");
            File.WriteAllLines(csv, new[]
            {
                "name,lon,lat",
                "P1,116.40,39.90", "P2,121.47,31.23", "P3,113.26,23.13",
                "P4,notanumber,0", "P5,,",
            });
            var outPath = Path.Combine(dir, "csv_pts.shp");
            await Program.RunRegisteredAsync("csv-to-vector", new()
            {
                ["input"] = csv, ["output"] = outPath, ["delimiter"] = ",",
                ["xField"] = "lon", ["yField"] = "lat", ["wkid"] = "4326"
            }, "csv-to-vector");
            var shp = ShpFile.Read(outPath);
            Program.Require(shp.Records == 3 && shp.GeometryKind == ShpFile.Kind.Point,
                "csv → 3 valid points", $"got {shp.Records} {shp.ShapeTypeName}");
            var dbf = DbfFile.Read(Path.ChangeExtension(outPath, ".dbf"));
            Program.Require(dbf.Records == 3, "csv dbf record count", $"{dbf.Records}");
        });

        // GPX: waypoint CSV / track GeoJSON / summary with exact counts.
        await Program.Checks.RunAsync("gpx ops", async () =>
        {
            var gpx = Synth.MakeGpx(dir, waypoints: 3, trackPoints: 5);

            var csvOut = Path.Combine(dir, "wpts.csv");
            await Program.RunRegisteredAsync("gpx-processing", new()
            {
                ["input"] = gpx, ["operation"] = "Extract Waypoints to CSV", ["output"] = csvOut
            }, "gpx waypoints");
            var lines = File.ReadAllLines(csvOut).Where(l => l.Trim().Length > 0).ToList();
            Program.Require(lines.Count == 4, "gpx waypoints csv = header + 3 rows", $"{lines.Count} lines");

            var trkOut = Path.Combine(dir, "tracks.geojson");
            await Program.RunRegisteredAsync("gpx-processing", new()
            {
                ["input"] = gpx, ["operation"] = "Extract Tracks to GeoJSON", ["output"] = trkOut
            }, "gpx tracks");
            var gj = GeoJsonFile.Read(trkOut);
            Program.Require(gj.FeatureCount >= 1, "gpx tracks exported", $"{gj.FeatureCount} features");

            var summary = await Program.RunRegisteredAsync("gpx-processing", new()
            {
                ["input"] = gpx, ["operation"] = "GPX Summary"
            }, "gpx summary");
            Program.Require(summary.Message.Contains("3") && summary.Message.Contains("5"),
                "gpx summary mentions 3 wpts / 5 trkpts", summary.Message.Replace('\n', ' ')[..Math.Min(140, summary.Message.Length)]);
        });

        // ZIP round trip over a real shapefile set.
        var layer = Program.Catalog.Polygon ?? Program.Catalog.Layers.FirstOrDefault();
        if (layer != null)
        {
            await Program.Checks.RunAsync("zip round trip", async () =>
            {
                var stage = Path.Combine(dir, "zip_src_" + layer.Name);
                Directory.CreateDirectory(stage);
                foreach (var ext in new[] { ".shp", ".dbf", ".shx", ".prj", ".cpg" })
                {
                    var f = Path.ChangeExtension(layer.ShpPath, ext);
                    if (File.Exists(f)) File.Copy(f, Path.Combine(stage, layer.Name + ext));
                }
                var zip = Path.Combine(dir, layer.Name + ".zip");
                await Program.RunRegisteredAsync("zip-compress", new()
                {
                    ["input"] = stage, ["output"] = zip
                }, "zip-compress");
                Program.Require(File.Exists(zip) && new FileInfo(zip).Length > 100, "zip produced", "missing/small");

                var extractDir = Path.Combine(dir, "unzipped_" + layer.Name);
                await Program.RunRegisteredAsync("zip-extract", new()
                {
                    ["input"] = zip, ["output"] = extractDir
                }, "zip-extract");
                var extracted = Directory.EnumerateFiles(extractDir, "*.shp", SearchOption.AllDirectories).ToList();
                Program.Require(extracted.Count == 1, "zip extracted one shp", $"{extracted.Count}");
                if (extracted.Count == 1)
                {
                    var shp = ShpFile.Read(extracted[0]);
                    Program.Require(shp.Records == layer.ExpectedCount, "zip round trip count",
                        $"{shp.Records} vs {layer.ExpectedCount}");
                }
            });
        }
    }
}
