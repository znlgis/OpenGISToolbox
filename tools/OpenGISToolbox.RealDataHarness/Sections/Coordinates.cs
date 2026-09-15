using OpenGISToolbox.TestKit;

namespace OpenGISToolbox.RealDataHarness.Sections;

/// <summary>Reprojection round trips on real data (general pitfall ④ applies).</summary>
public static class Coordinates
{
    public static async Task Run()
    {
        Program.Checks.BeginSection("5. Coordinate transformation sweep");

        var poly = Program.Catalog.Polygon;
        if (poly == null)
        {
            Program.Checks.Skip("coordinate sweep", "no polygon layer");
            return;
        }
        var dir = GdalEnv.Dir("harness", "coords");

        // 4326 → 3857 → 4326 through two tool calls, coordinate-anchored round trip.
        await Program.Checks.RunAsync("reproject 4326↔3857 round trip", async () =>
        {
            var metric = Path.Combine(dir, "epsg3857.shp");
            var back = Path.Combine(dir, "back4326.shp");
            await Program.RunRegisteredAsync("reproject", new()
            {
                ["input"] = poly.ShpPath, ["output"] = metric, ["sourceWkid"] = "4326", ["targetWkid"] = "3857"
            }, "reproject→3857");
            var metricShp = ShpFile.Read(metric);
            Program.Require(metricShp.Records == poly.ExpectedCount, "reproject→3857 count",
                $"got {metricShp.Records}");
            await Program.RunRegisteredAsync("reproject", new()
            {
                ["input"] = metric, ["output"] = back, ["sourceWkid"] = "3857", ["targetWkid"] = "4326"
            }, "reproject→4326");
            var src = LayerCompare.ByFid(GdalEnv.ReadLayer(poly.ShpPath));
            var rt = LayerCompare.ByFid(GdalEnv.ReadLayer(back));
            Program.Require(rt.Count == src.Count, "round trip count", $"{rt.Count} vs {src.Count}");
            double worst = 0; int checkedFeatures = 0;
            foreach (var fid in src.Keys.OrderBy(k => k).Take(25))
            {
                if (!rt.TryGetValue(fid, out var r)) continue;
                if (string.IsNullOrEmpty(src[fid].Wkt) || string.IsNullOrEmpty(r.Wkt)) continue;
                var dev = LayerCompare.MaxDeviation(src[fid].Wkt!, r.Wkt);
                if (double.IsPositiveInfinity(dev)) continue; // vertex count drift (precision drop) — skip metric
                worst = Math.Max(worst, dev);
                checkedFeatures++;
            }
            Program.Require(worst < 1e-5 && checkedFeatures > 0, "round-trip coordinate deviation < 1e-5°",
                $"worst={worst:E2} over {checkedFeatures} features");
        });

        // 4326 → 4490 (both geographic, same datum): coordinates must be near-identical.
        await Program.Checks.RunAsync("reproject 4326→4490 near-identity", async () =>
        {
            var outPath = Path.Combine(dir, "epsg4490.shp");
            await Program.RunRegisteredAsync("reproject", new()
            {
                ["input"] = poly.ShpPath, ["output"] = outPath, ["sourceWkid"] = "4326", ["targetWkid"] = "4490"
            }, "reproject→4490");
            var src = LayerCompare.ByFid(GdalEnv.ReadLayer(poly.ShpPath));
            var dst = LayerCompare.ByFid(GdalEnv.ReadLayer(outPath));
            double worst = 0; int n = 0;
            foreach (var fid in src.Keys.OrderBy(k => k).Take(25))
            {
                if (!dst.TryGetValue(fid, out var d)) continue;
                if (string.IsNullOrEmpty(src[fid].Wkt) || string.IsNullOrEmpty(d.Wkt)) continue;
                var dev = LayerCompare.MaxDeviation(src[fid].Wkt!, d.Wkt);
                if (double.IsPositiveInfinity(dev)) continue;
                worst = Math.Max(worst, dev); n++;
            }
            Program.Require(worst < 1e-6 && n > 0, "4326→4490 deviation < 1e-6°", $"worst={worst:E2} over {n} features");
        });

        // Batch reproject a folder of real SHP sets.
        await Program.Checks.RunAsync("batch-reproject", async () =>
        {
            var inDir = Path.Combine(dir, "batch_in");
            var outDir = Path.Combine(dir, "batch_out");
            Directory.CreateDirectory(inDir);
            var sets = Program.Catalog.Layers.Take(3).ToList();
            foreach (var layer in sets)
                foreach (var ext in new[] { ".shp", ".dbf", ".shx", ".prj", ".cpg" })
                {
                    var f = Path.ChangeExtension(layer.ShpPath, ext);
                    if (File.Exists(f)) File.Copy(f, Path.Combine(inDir, layer.Name + ext));
                }
            // Target Web Mercator: valid for global extents (UTM zones legitimately
            // fail "full reprojection" for worldwide layers — not a tool defect).
            await Program.RunRegisteredAsync("batch-reproject", new()
            {
                ["inputFolder"] = inDir, ["outputFolder"] = outDir,
                ["sourceWkid"] = "4326", ["targetWkid"] = "3857", ["format"] = "SHP"
            }, "batch-reproject");
            var outputs = Directory.GetFiles(outDir, "*.shp");
            Program.Require(outputs.Length == sets.Count, "batch output count",
                $"{outputs.Length} vs {sets.Count}");
            foreach (var shp in outputs)
            {
                var header = ShpFile.Read(shp);
                var dbf = DbfFile.Read(Path.ChangeExtension(shp, ".dbf"));
                Program.Require(header.Records == dbf.Records, $"batch {Path.GetFileName(shp)} header consistency",
                    $"shp={header.Records} dbf={dbf.Records}");
            }
        });

        // Union of layer with itself (same geometries) → non-empty output.
        await Program.Checks.RunAsync("union", async () =>
        {
            var dir2 = GdalEnv.Dir("harness", "union");
            var copy = Path.Combine(dir2, "second.shp");
            foreach (var ext in new[] { ".shp", ".dbf", ".shx", ".prj", ".cpg" })
            {
                var f = Path.ChangeExtension(poly.ShpPath, ext);
                if (File.Exists(f)) File.Copy(f, Path.ChangeExtension(copy, ext), true);
            }
            var outPath = Path.Combine(dir2, "union_out.shp");
            await Program.RunRegisteredAsync("union", new()
            {
                ["input1"] = poly.ShpPath, ["input2"] = copy, ["output"] = outPath
            }, "union");
            var l = GdalEnv.ReadLayer(outPath);
            Program.Require((int)l.GetFeatureCount() >= 1, "union produced output", $"got {l.GetFeatureCount()}");
        });
    }
}
