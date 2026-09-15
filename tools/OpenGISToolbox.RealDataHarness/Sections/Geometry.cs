using OpenGISToolbox.TestKit;
using OpenGIS.Utils.Geometry;

namespace OpenGISToolbox.RealDataHarness.Sections;

/// <summary>Geometry processing tools against the real polygon/line/point layers.</summary>
public static class Geometry
{
    public static async Task Run()
    {
        Program.Checks.BeginSection("3. Geometry processing sweep");

        var poly = Program.Catalog.Polygon;
        var pt = Program.Catalog.Point;
        var line = Program.Catalog.Line;
        var dir = GdalEnv.Dir("harness", "geometry");

        if (poly == null)
        {
            Program.Checks.Skip("geometry sweep", "no polygon layer available");
            return;
        }

        // Buffer: count preserved, total area strictly grows (degrees² units — monotonicity only).
        await Program.Checks.RunAsync("buffer", async () =>
        {
            var outPath = Path.Combine(dir, "buffer.shp");
            await Program.RunRegisteredAsync("buffer", new()
            {
                ["input"] = poly.ShpPath, ["output"] = outPath, ["distance"] = "0.05"
            }, "buffer");
            var l = GdalEnv.ReadLayer(outPath);
            Program.Require((int)l.GetFeatureCount() == poly.ExpectedCount, "buffer count",
                $"got {l.GetFeatureCount()}, want {poly.ExpectedCount}");
            double srcArea = SumArea(poly), bufArea = l.Features
                .Where(f => !string.IsNullOrEmpty(f.Wkt)).Sum(f => GeometryUtil.AreaWkt(f.Wkt!));
            Program.Require(bufArea > srcArea, "buffer grows total area",
                $"src={srcArea:F6} buffered={bufArea:F6}");
        });

        // Centroid: count preserved, points inside source bbox.
        await Program.Checks.RunAsync("centroid", async () =>
        {
            var outPath = Path.Combine(dir, "centroid.shp");
            await Program.RunRegisteredAsync("centroid", new()
            {
                ["input"] = poly.ShpPath, ["output"] = outPath
            }, "centroid");
            var l = GdalEnv.ReadLayer(outPath);
            Program.Require((int)l.GetFeatureCount() == poly.ExpectedCount, "centroid count",
                $"got {l.GetFeatureCount()}");
            var bad = l.Features.Where(f => !string.IsNullOrEmpty(f.Wkt)).Where(f =>
            {
                var c = LayerCompare.ParseCoords(f.Wkt!).FirstOrDefault();
                return c.X < poly.Bbox[0] - 1 || c.X > poly.Bbox[2] + 1 ||
                       c.Y < poly.Bbox[1] - 1 || c.Y > poly.Bbox[3] + 1;
            }).Count();
            Program.Require(bad == 0, "centroids inside expanded bbox", $"{bad} outside");
        });

        // Convex hull: count preserved, per-hull area >= source area (anchored by Fid).
        await Program.Checks.RunAsync("convex-hull", async () =>
        {
            var outPath = Path.Combine(dir, "hull.shp");
            await Program.RunRegisteredAsync("convex-hull", new()
            {
                ["input"] = poly.ShpPath, ["output"] = outPath
            }, "convex-hull");
            var src = LayerCompare.ByFid(GdalEnv.ReadLayer(poly.ShpPath));
            var hull = LayerCompare.ByFid(GdalEnv.ReadLayer(outPath));
            Program.Require(hull.Count == src.Count, "convex-hull count", $"{hull.Count} vs {src.Count}");
            var violations = src.Keys.Count(fid =>
                hull.TryGetValue(fid, out var h) && !string.IsNullOrEmpty(h.Wkt) &&
                !string.IsNullOrEmpty(src[fid].Wkt) &&
                GeometryUtil.AreaWkt(h.Wkt!) + 1e-9 < GeometryUtil.AreaWkt(src[fid].Wkt!));
            Program.Require(violations == 0, "hull area >= source area (Fid-anchored)", $"{violations} violations");
        });

        // Simplify: count preserved, total vertex count shrinks.
        await Program.Checks.RunAsync("simplify", async () =>
        {
            var outPath = Path.Combine(dir, "simplified.shp");
            await Program.RunRegisteredAsync("simplify", new()
            {
                ["input"] = poly.ShpPath, ["output"] = outPath, ["tolerance"] = "0.05"
            }, "simplify");
            var l = GdalEnv.ReadLayer(outPath);
            Program.Require((int)l.GetFeatureCount() == poly.ExpectedCount, "simplify count",
                $"got {l.GetFeatureCount()}");
            var srcVerts = VertexCount(poly.ShpPath);
            var outVerts = l.Features.Where(f => f.Wkt != null).Sum(f => LayerCompare.ParseCoords(f.Wkt!).Count);
            Program.RequireWithWarn(outVerts < srcVerts, "simplify reduces vertices",
                $"out={outVerts} src={srcVerts} (tolerance may be below detail level)");
        });

        // Fix / check validity on real data.
        await Program.Checks.RunAsync("fix-geometries", async () =>
        {
            var outPath = Path.Combine(dir, "fixed.shp");
            await Program.RunRegisteredAsync("fix-geometries", new()
            {
                ["input"] = poly.ShpPath, ["output"] = outPath
            }, "fix-geometries");
            var l = GdalEnv.ReadLayer(outPath);
            Program.Require((int)l.GetFeatureCount() >= poly.ExpectedCount, "fix-geometries keeps features",
                $"got {l.GetFeatureCount()} >= want {poly.ExpectedCount}");
        });

        await Program.Checks.RunAsync("check-geometry", async () =>
        {
            var result = await Program.RunRegisteredAsync("check-geometry", new() { ["input"] = poly.ShpPath },
                "check-geometry");
            Program.Require(result.Message.Length > 0, "check-geometry reports", "empty message");
        });

        // Merge same-schema copy: exactly double the count.
        await Program.Checks.RunAsync("merge", async () =>
        {
            var copy = CopyLayer(poly, dir, "copy2");
            var outPath = Path.Combine(dir, "merged.shp");
            await Program.RunRegisteredAsync("merge-layers", new()
            {
                ["input1"] = poly.ShpPath, ["input2"] = copy, ["output"] = outPath
            }, "merge-layers");
            var l = GdalEnv.ReadLayer(outPath);
            Program.Require((int)l.GetFeatureCount() == 2 * poly.ExpectedCount, "merge count == 2N",
                $"got {l.GetFeatureCount()}, want {2 * poly.ExpectedCount}");
        });

        // Split by a low-cardinality ASCII field: per-file counts must sum to N.
        var splitField = poly.PickSplitField();
        if (splitField == null)
        {
            Program.Checks.Skip("split", "no fully-populated ASCII character field on polygon layer");
        }
        else
        {
            await Program.Checks.RunAsync("split", async () =>
            {
                var outDir = Path.Combine(dir, "split_out");
                var result = await Program.RunRegisteredAsync("split-layer", new()
                {
                    ["input"] = poly.ShpPath, ["fieldName"] = splitField.Value.Field, ["outputFolder"] = outDir
                }, "split-layer");
                int total = 0;
                foreach (var shp in Directory.EnumerateFiles(outDir, "*.shp"))
                {
                    var dbfPath = Path.ChangeExtension(shp, ".dbf");
                    if (File.Exists(dbfPath)) total += ShpFile.Read(shp).Records;
                }
                Program.Require(total == poly.ExpectedCount, "split pieces sum to N",
                    $"sum={total}, want {poly.ExpectedCount}, distinct={splitField.Value.DistinctValues}");
                Program.Require(result.Success, "split success flag", "reported failure");
            });
        }

        // Clip / Intersection / Difference with a synthetic square at the layer's bbox centre.
        var square = poly.MakeSquareAroundCenterGeoJson(3.0, Path.Combine(dir, "square.geojson"));
        await Program.Checks.RunAsync("clip", async () =>
        {
            var outPath = Path.Combine(dir, "clipped.shp");
            await Program.RunRegisteredAsync("clip", new()
            {
                ["input"] = poly.ShpPath, ["clipLayer"] = square, ["output"] = outPath
            }, "clip");
            var l = GdalEnv.ReadLayer(outPath);
            Program.Require((int)l.GetFeatureCount() <= poly.ExpectedCount, "clip subset",
                $"got {l.GetFeatureCount()} <= {poly.ExpectedCount} (0 if centre is open ocean)");
        });

        await Program.Checks.RunAsync("intersection", async () =>
        {
            var outPath = Path.Combine(dir, "intersection.shp");
            await Program.RunRegisteredAsync("intersection", new()
            {
                ["input1"] = poly.ShpPath, ["input2"] = square, ["output"] = outPath
            }, "intersection");
            var l = GdalEnv.ReadLayer(outPath);
            Program.Require((int)l.GetFeatureCount() <= poly.ExpectedCount, "intersection subset",
                $"got {l.GetFeatureCount()}");
        });

        await Program.Checks.RunAsync("difference", async () =>
        {
            var outPath = Path.Combine(dir, "difference.shp");
            await Program.RunRegisteredAsync("difference", new()
            {
                ["input1"] = poly.ShpPath, ["input2"] = square, ["output"] = outPath
            }, "difference");
            var l = GdalEnv.ReadLayer(outPath);
            Program.Require((int)l.GetFeatureCount() >= 1, "difference produced output", $"got {l.GetFeatureCount()}");
        });

        // Spatial join points → polygons (left-join: every target feature retained).
        if (pt != null)
        {
            await Program.Checks.RunAsync("spatial-join", async () =>
            {
                var outPath = Path.Combine(dir, "joined.shp");
                await Program.RunRegisteredAsync("spatial-join", new()
                {
                    ["input"] = pt.ShpPath, ["joinLayer"] = poly.ShpPath,
                    ["output"] = outPath, ["joinType"] = "Intersects"
                }, "spatial-join");
                var l = GdalEnv.ReadLayer(outPath);
                Program.Require((int)l.GetFeatureCount() == pt.ExpectedCount, "spatial-join keeps all targets",
                    $"got {l.GetFeatureCount()}, want {pt.ExpectedCount}");
                var joinedField = poly.Dbf.Fields.Select(f => f.Name)
                    .Concat(pt.Dbf.Fields.Select(f => f.Name)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var anyEnriched = l.Features.Any(f => f.Attributes
                    .Any(kv => !pt.Dbf.Fields.Any(df => string.Equals(df.Name, kv.Key, StringComparison.OrdinalIgnoreCase))
                               && kv.Value?.Value != null));
                Program.RequireWithWarn(anyEnriched, "spatial-join carries joined attributes",
                    "no attributes beyond the target schema (points outside polygons?)");
                _ = joinedField;
            });
        }

        // Central lines on real polygons (negative-buffer approximation).
        await Program.Checks.RunAsync("central-lines", async () =>
        {
            var outPath = Path.Combine(dir, "central.shp");
            await Program.RunRegisteredAsync("central-lines", new()
            {
                ["input"] = poly.ShpPath, ["output"] = outPath
            }, "central-lines");
            var l = GdalEnv.ReadLayer(outPath);
            Program.Require((int)l.GetFeatureCount() == poly.ExpectedCount, "central-lines count",
                $"got {l.GetFeatureCount()}, want {poly.ExpectedCount}");
        });

        // Line layer extras.
        if (line != null)
        {
            await Program.Checks.RunAsync("line tools on real lines", async () =>
            {
                var outPath = Path.Combine(dir, "line_centroid.shp");
                var srcNonNull = GdalEnv.ReadLayer(line.ShpPath).Features.Count(f => !string.IsNullOrEmpty(f.Wkt));
                await Program.RunRegisteredAsync("centroid", new()
                {
                    ["input"] = line.ShpPath, ["output"] = outPath
                }, "centroid(line)");
                var l = GdalEnv.ReadLayer(outPath);
                // Null-geometry records have no centroid and are legitimately skipped.
                Program.Require((int)l.GetFeatureCount() == srcNonNull, "centroid on lines count",
                    $"got {l.GetFeatureCount()}, want {srcNonNull} (of {line.ExpectedCount} incl nulls)");
            });
        }
    }

    static double SumArea(RealLayer layer) =>
        GdalEnv.ReadLayer(layer.ShpPath).Features
            .Where(f => !string.IsNullOrEmpty(f.Wkt)).Sum(f => GeometryUtil.AreaWkt(f.Wkt!));

    static int VertexCount(string shpPath) =>
        GdalEnv.ReadLayer(shpPath).Features.Where(f => f.Wkt != null)
            .Sum(f => LayerCompare.ParseCoords(f.Wkt!).Count);

    static string CopyLayer(RealLayer layer, string dir, string newName)
    {
        var target = Path.Combine(dir, newName + ".shp");
        foreach (var ext in new[] { ".shp", ".dbf", ".shx", ".prj", ".cpg" })
        {
            var src = Path.ChangeExtension(layer.ShpPath, ext);
            if (File.Exists(src)) File.Copy(src, Path.ChangeExtension(target, ext), overwrite: true);
        }
        return target;
    }
}
