using OpenGISToolbox.TestKit;
using OpenGIS.Utils.Geometry;

namespace OpenGISToolbox.RealDataHarness.Sections;

/// <summary>
/// Sprint-2 analysis tools against real layers. Cross-checks avoid trusting the
/// same code path: self-relations give known set sizes; count-by-polygon is
/// verified against a no-bbox-prefilter brute force; the grid is checked against
/// the layer's own header bounding box.
/// </summary>
public static class Sprint2
{
    public static async Task Run()
    {
        Program.Checks.BeginSection("11. Sprint-2 analysis sweep");

        var poly = Program.Catalog.Polygon;
        var pt = Program.Catalog.Point;
        var dir = GdalEnv.Dir("harness", "sprint2");

        if (poly == null)
        {
            Program.Checks.Skip("sprint-2 sweep", "no polygon layer available");
            return;
        }

        // Extract by location, self-relation: Intersects(self) keeps everything,
        // Disjoint(self) keeps nothing — both independent of geometry specifics.
        int srcNonNull = GdalEnv.ReadLayer(poly.ShpPath).Features
            .Count(f => !string.IsNullOrEmpty(f.Wkt));

        await Program.Checks.RunAsync("extract-by-location intersects", async () =>
        {
            var outPath = Path.Combine(dir, "ebl_int.shp");
            await Program.RunRegisteredAsync("extract-by-location", new()
            {
                ["input"] = poly.ShpPath, ["reference"] = poly.ShpPath,
                ["output"] = outPath, ["predicate"] = "Intersects"
            }, "ebl-int");
            var l = GdalEnv.ReadLayer(outPath);
            Program.Require((int)l.GetFeatureCount() == srcNonNull, "intersects-self keeps all",
                $"got {l.GetFeatureCount()}, want {srcNonNull}");
        });

        await Program.Checks.RunAsync("extract-by-location disjoint", async () =>
        {
            var outPath = Path.Combine(dir, "ebl_dj.shp");
            await Program.RunRegisteredAsync("extract-by-location", new()
            {
                ["input"] = poly.ShpPath, ["reference"] = poly.ShpPath,
                ["output"] = outPath, ["predicate"] = "Disjoint"
            }, "ebl-dj");
            var l = GdalEnv.ReadLayer(outPath);
            Program.Require((int)l.GetFeatureCount() == 0, "disjoint-self selects none",
                $"got {l.GetFeatureCount()}");
        });

        // Count points in polygon: tool (bbox-prefiltered) vs no-prefilter brute force.
        if (pt != null)
        {
            await Program.Checks.RunAsync("count-points-in-polygon", async () =>
            {
                var outPath = Path.Combine(dir, "cpip.shp");
                await Program.RunRegisteredAsync("count-points-in-polygon", new()
                {
                    ["input"] = poly.ShpPath, ["points"] = pt.ShpPath,
                    ["output"] = outPath, ["field"] = "pt_count"
                }, "count-points-in-polygon");

                var polys = GdalEnv.ReadLayer(poly.ShpPath);
                var points = GdalEnv.ReadLayer(pt.ShpPath);
                var pointWkts = points.Features.Where(f => !string.IsNullOrEmpty(f.Wkt))
                    .Select(f => f.Wkt!).ToList();

                // Independent tally without any bounding-box prefilter.
                long brute = 0;
                foreach (var polyF in polys.Features)
                {
                    if (string.IsNullOrEmpty(polyF.Wkt)) continue;
                    foreach (var pw in pointWkts)
                        try { if (GeometryUtil.ContainsWkt(polyF.Wkt!, pw)) brute++; }
                        catch { /* degenerate: not a hit */ }
                }

                var l = GdalEnv.ReadLayer(outPath);
                long toolTotal = l.Features
                    .Sum(f => Convert.ToInt64(f.GetValue("pt_count") ?? 0,
                        System.Globalization.CultureInfo.InvariantCulture));
                Program.Require(toolTotal == brute, "tool total == no-prefilter brute force",
                    $"tool={toolTotal} brute={brute} (points={pointWkts.Count})");
            });

            // Nearest neighbour against the same point layer: each point's own
            // feature is at distance 0 → nn_dist ≈ 0 and nn_fid resolves.
            await Program.Checks.RunAsync("nearest-neighbor self", async () =>
            {
                var outPath = Path.Combine(dir, "nn.shp");
                await Program.RunRegisteredAsync("nearest-neighbor", new()
                {
                    ["input"] = pt.ShpPath, ["reference"] = pt.ShpPath, ["output"] = outPath
                }, "nearest-neighbor");
                var l = GdalEnv.ReadLayer(outPath);
                int srcCount = GdalEnv.ReadLayer(pt.ShpPath).Features
                    .Count(f => !string.IsNullOrEmpty(f.Wkt));
                Program.Require((int)l.GetFeatureCount() == srcCount, "nn keeps all target features",
                    $"got {l.GetFeatureCount()}, want {srcCount}");
                double maxDist = l.Features
                    .Where(f => f.GetValue("nn_fid") != null)
                    .Select(f => Convert.ToDouble(f.GetValue("nn_dist") ?? 0,
                        System.Globalization.CultureInfo.InvariantCulture))
                    .DefaultIfEmpty(0).Max();
                Program.Require(maxDist < 1e-9, "self-nearest distance ≈ 0",
                    $"maxDist={maxDist:E3}");
            });
        }
        else
        {
            Program.Checks.Skip("count-points-in-polygon", "no point layer available");
            Program.Checks.Skip("nearest-neighbor self", "no point layer available");
        }

        // Create grid over the layer's own header bbox: rows×cols cells, area == extent.
        await Program.Checks.RunAsync("create-grid manual extent", async () =>
        {
            var b = poly.Bbox;
            var outPath = Path.Combine(dir, "grid.shp");
            await Program.RunRegisteredAsync("create-grid", new()
            {
                ["output"] = outPath,
                ["xmin"] = b[0].ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                ["ymin"] = b[1].ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                ["xmax"] = b[2].ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                ["ymax"] = b[3].ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                ["rows"] = "3", ["cols"] = "4"
            }, "create-grid");
            var l = GdalEnv.ReadLayer(outPath);
            Program.Require((int)l.GetFeatureCount() == 12, "grid cell count == rows×cols",
                $"got {l.GetFeatureCount()}");
            double total = l.Features.Where(f => !string.IsNullOrEmpty(f.Wkt))
                .Sum(f => GeometryUtil.AreaWkt(f.Wkt!));
            double want = (b[2] - b[0]) * (b[3] - b[1]);
            Program.Require(Math.Abs(total - want) <= Math.Abs(want) * 1e-6 + 1e-9,
                "grid total area == extent area", $"got {total:R}, want {want:R}");
        });

        await Program.Checks.RunAsync("create-grid from layer extent", async () =>
        {
            var outPath = Path.Combine(dir, "grid_layer.shp");
            await Program.RunRegisteredAsync("create-grid", new()
            {
                ["input"] = poly.ShpPath, ["output"] = outPath, ["rows"] = "2", ["cols"] = "2"
            }, "create-grid-layer");
            var l = GdalEnv.ReadLayer(outPath);
            Program.Require((int)l.GetFeatureCount() == 4, "layer-derived grid has 4 cells",
                $"got {l.GetFeatureCount()}");
        });
    }
}
