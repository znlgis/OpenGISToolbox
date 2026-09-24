using OpenGISToolbox.TestKit;
using OSGeo.GDAL;

namespace OpenGISToolbox.RealDataHarness.Sections;

/// <summary>
/// Sprint-3 raster tools exercised through the registry against synthetic
/// terrain/rasters with a known geotransform (no real DEM ships with the anchor
/// set), plus a contour round-trip. Verifies the tools are wired, run on the
/// bundled MaxRev runtime, and produce sane outputs.
/// </summary>
public static class Sprint3
{
    public static async Task Run()
    {
        Program.Checks.BeginSection("12. Sprint-3 raster sweep");
        GdalEnv.Ensure();
        var dir = GdalEnv.Dir("harness", "sprint3");

        var dem = Synth.MakePlanarDem(dir, "dem.tif", 24, 12, epsg: 32650);
        var constRaster = Synth.MakeConstantRaster(dir, "const.tif", 4, 4, value: 7, originX: 0, originY: 0, epsg: 32650);

        // DEM slope: 45° planar ramp → interior ≈ 45, dimensions preserved.
        await Program.Checks.RunAsync("dem-terrain slope", async () =>
        {
            var outPath = Path.Combine(dir, "slope.tif");
            await Program.RunRegisteredAsync("dem-terrain", new()
            {
                ["input"] = dem, ["output"] = outPath, ["algorithm"] = "Slope", ["slopeFormat"] = "degrees"
            }, "dem-terrain");
            var (w, h, values, _) = Synth.ReadRaster(outPath);
            Program.Require(w == 24 && h == 12, "slope keeps dimensions", $"{w}x{h}");
            var interior = new List<double>();
            for (var r = 1; r < h - 1; r++)
                for (var c = 1; c < w - 1; c++)
                    if (!double.IsNaN(values[r * w + c])) interior.Add(values[r * w + c]);
            Program.Require(interior.Count > 0 && interior.Min() >= 40 && interior.Max() <= 50,
                "planar ramp slope ≈ 45°", $"[{interior.Min():F2},{interior.Max():F2}]");
        });

        // Contours from the DEM: a handful of valid line features.
        await Program.Checks.RunAsync("contour", async () =>
        {
            var outPath = Path.Combine(dir, "contours.geojson");
            await Program.RunRegisteredAsync("contour", new()
            {
                ["input"] = dem, ["output"] = outPath, ["interval"] = "5", ["base"] = "0"
            }, "contour");
            var l = GdalEnv.ReadLayer(outPath);
            int n = (int)l.GetFeatureCount();
            Program.Require(n >= 2, "contour produced lines", $"n={n}");
            var nonLine = l.Features.Count(f => !string.IsNullOrEmpty(f.Wkt)
                && !f.Wkt!.Contains("LINESTRING", StringComparison.OrdinalIgnoreCase));
            Program.Require(nonLine == 0, "all contours are lines", $"{nonLine} non-line");
        });

        // Warp reprojects UTM → geographic (sub-degree pixel size).
        await Program.Checks.RunAsync("raster-warp", async () =>
        {
            var outPath = Path.Combine(dir, "warp.tif");
            await Program.RunRegisteredAsync("raster-warp", new()
            {
                ["input"] = dem, ["output"] = outPath, ["targetWkid"] = "4326", ["resampling"] = "nearest"
            }, "raster-warp");
            var (w, h, _, gt) = Synth.ReadRaster(outPath);
            Program.Require(w > 0 && h > 0 && Math.Abs(gt[1]) < 1.0,
                "reprojected to degrees", $"{w}x{h} px={gt[1]:E3}");
        });

        // Clip shrinks the extent.
        await Program.Checks.RunAsync("raster-clip", async () =>
        {
            var outPath = Path.Combine(dir, "clip.tif");
            await Program.RunRegisteredAsync("raster-clip", new()
            {
                ["input"] = constRaster, ["output"] = outPath,
                ["xmin"] = "1", ["ymin"] = "-3", ["xmax"] = "3", ["ymax"] = "-1"
            }, "raster-clip");
            var (w, h, _, _) = Synth.ReadRaster(outPath);
            Program.Require(w > 0 && h > 0 && w < 4 && h < 4, "clip shrinks 4x4", $"{w}x{h}");
        });

        // Mosaic covers the union (width grows beyond one tile).
        await Program.Checks.RunAsync("raster-mosaic", async () =>
        {
            var a = Synth.MakeConstantRaster(dir, "ma.tif", 4, 4, value: 5, originX: 0, originY: 0, epsg: 32650);
            var b = Synth.MakeConstantRaster(dir, "mb.tif", 4, 4, value: 6, originX: 4, originY: 0, epsg: 32650);
            var outPath = Path.Combine(dir, "mosaic.tif");
            await Program.RunRegisteredAsync("raster-mosaic", new()
            {
                ["input1"] = a, ["input2"] = b, ["output"] = outPath, ["resampling"] = "nearest"
            }, "raster-mosaic");
            var (w, _, _, _) = Synth.ReadRaster(outPath);
            Program.Require(w > 4, "mosaic covers union width", $"w={w}");
        });

        // Zonal statistics over a constant raster covering all cells.
        await Program.Checks.RunAsync("zonal-statistics", async () =>
        {
            var polys = Path.Combine(dir, "zone_poly.geojson");
            File.WriteAllText(polys, """
                {"type":"FeatureCollection","features":[{"type":"Feature",
                "properties":{"tag":"all"},"geometry":{"type":"Polygon",
                "coordinates":[[[0,0],[4,0],[4,-4],[0,-4],[0,0]]]}}]}
                """);
            var outPath = Path.Combine(dir, "zone_out.geojson");
            await Program.RunRegisteredAsync("zonal-statistics", new()
            {
                ["polygons"] = polys, ["raster"] = constRaster, ["output"] = outPath, ["band"] = "1"
            }, "zonal-statistics");
            var l = GdalEnv.ReadLayer(outPath);
            var feat = l.Features[0];
            int count = Convert.ToInt32(feat.GetValue("zone_count"), System.Globalization.CultureInfo.InvariantCulture);
            double mean = Convert.ToDouble(feat.GetValue("zone_mean"), System.Globalization.CultureInfo.InvariantCulture);
            Program.Require(count == 16, "zonal count == 16 cells", $"count={count}");
            Program.Require(Math.Abs(mean - 7) < 1e-6, "zonal mean == 7", $"mean={mean}");
        });
    }
}
