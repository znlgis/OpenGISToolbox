using OpenGISToolbox.TestKit;
using OpenGIS.Utils.Geometry;
using OpenGISToolbox.Tools;

namespace OpenGISToolbox.RealDataHarness.Sections;

/// <summary>
/// Sprint-1 feature-expansion tools exercised against real layers with
/// expectations derived independently of GDAL (file-header counts, WKT vertex
/// scans, source-area sums), following the same data-agnostic discipline as the
/// other sections.
/// </summary>
public static class Sprint1
{
    public static async Task Run()
    {
        Program.Checks.BeginSection("10. Sprint-1 vector geometry sweep");

        var poly = Program.Catalog.Polygon;
        var line = Program.Catalog.Line;
        var dir = GdalEnv.Dir("harness", "sprint1");

        if (poly == null)
        {
            Program.Checks.Skip("sprint-1 sweep", "no polygon layer available");
            return;
        }

        // Dissolve by a low-cardinality field: output groups == distinct values, all valid.
        var splitField = poly.PickSplitField();
        if (splitField == null)
        {
            Program.Checks.Skip("dissolve", "no fully-populated ASCII character field on polygon layer");
        }
        else
        {
            await Program.Checks.RunAsync("dissolve", async () =>
            {
                var outPath = Path.Combine(dir, "dissolved.shp");
                await Program.RunRegisteredAsync("dissolve", new()
                {
                    ["input"] = poly.ShpPath, ["output"] = outPath, ["field"] = splitField.Value.Field
                }, "dissolve");
                var l = GdalEnv.ReadLayer(outPath);
                Program.Require((int)l.GetFeatureCount() == splitField.Value.DistinctValues,
                    "dissolve groups == distinct field values",
                    $"got {l.GetFeatureCount()}, want {splitField.Value.DistinctValues}");
                var invalid = l.Features.Where(f => !string.IsNullOrEmpty(f.Wkt))
                    .Count(f => !GeometryUtil.IsValid(GeometryUtil.Wkt2Geometry(f.Wkt!)).IsValid);
                Program.Require(invalid == 0, "dissolve outputs valid", $"{invalid} invalid");
            });
        }

        // Multipart → Singlepart: part count never shrinks below feature count.
        await Program.Checks.RunAsync("multipart-to-single", async () =>
        {
            var outPath = Path.Combine(dir, "exploded.shp");
            await Program.RunRegisteredAsync("multipart-to-single", new()
            {
                ["input"] = poly.ShpPath, ["output"] = outPath
            }, "multipart-to-single");
            var src = GdalEnv.ReadLayer(poly.ShpPath);
            var l = GdalEnv.ReadLayer(outPath);
            int srcNonNull = src.Features.Count(f => !string.IsNullOrEmpty(f.Wkt));
            Program.Require((int)l.GetFeatureCount() >= srcNonNull, "explode keeps >= source features",
                $"got {l.GetFeatureCount()}, src {srcNonNull}");
            // No multipart geometry survives the explode.
            var stillMulti = l.Features.Count(f => !string.IsNullOrEmpty(f.Wkt)
                && GeometryOps.IsMultipart(f.Wkt!));
            Program.Require(stillMulti == 0, "explode leaves no multipart geometries", $"{stillMulti} remain");
        });

        // Singlepart → Multipart (ungrouped): everything collapses to one feature.
        await Program.Checks.RunAsync("single-to-multipart", async () =>
        {
            var outPath = Path.Combine(dir, "collected.shp");
            await Program.RunRegisteredAsync("single-to-multipart", new()
            {
                ["input"] = poly.ShpPath, ["output"] = outPath, ["field"] = ""
            }, "single-to-multipart");
            var l = GdalEnv.ReadLayer(outPath);
            Program.Require((int)l.GetFeatureCount() == 1, "collect-all yields a single feature",
                $"got {l.GetFeatureCount()}");
        });

        // Extract vertices: point count == independent WKT vertex scan of the source.
        await Program.Checks.RunAsync("extract-vertices", async () =>
        {
            var outPath = Path.Combine(dir, "vertices.shp");
            await Program.RunRegisteredAsync("extract-vertices", new()
            {
                ["input"] = poly.ShpPath, ["output"] = outPath
            }, "extract-vertices");
            var src = GdalEnv.ReadLayer(poly.ShpPath);
            int expectedVerts = src.Features
                .Where(f => !string.IsNullOrEmpty(f.Wkt))
                .Sum(f => LayerCompare.ParseCoords(f.Wkt!).Count);
            var l = GdalEnv.ReadLayer(outPath);
            Program.Require((int)l.GetFeatureCount() == expectedVerts, "vertex count == source WKT scan",
                $"got {l.GetFeatureCount()}, want {expectedVerts}");
        });

        // Polygon to line: one boundary feature per polygon.
        await Program.Checks.RunAsync("polygon-to-line", async () =>
        {
            var outPath = Path.Combine(dir, "polylines.shp");
            await Program.RunRegisteredAsync("polygon-to-line", new()
            {
                ["input"] = poly.ShpPath, ["output"] = outPath
            }, "polygon-to-line");
            var src = GdalEnv.ReadLayer(poly.ShpPath);
            int srcNonNull = src.Features.Count(f => !string.IsNullOrEmpty(f.Wkt));
            var l = GdalEnv.ReadLayer(outPath);
            Program.Require((int)l.GetFeatureCount() == srcNonNull, "polygon-to-line keeps feature count",
                $"got {l.GetFeatureCount()}, want {srcNonNull}");
            var anyLine = l.Features.Any(f => !string.IsNullOrEmpty(f.Wkt)
                && f.Wkt!.Contains("LINESTRING", StringComparison.OrdinalIgnoreCase));
            Program.Require(anyLine, "polygon-to-line emits lines", "no LINESTRING output");
        });

        // Line to polygon on a real line layer (subset may be degenerate → warn).
        if (line != null)
        {
            await Program.Checks.RunAsync("line-to-polygon", async () =>
            {
                var outPath = Path.Combine(dir, "line_polys.shp");
                var result = await Program.RunRegisteredAsync("line-to-polygon", new()
                {
                    ["input"] = line.ShpPath, ["output"] = outPath
                }, "line-to-polygon");
                Program.Require(result.Success, "line-to-polygon runs", "reported failure");
                var l = GdalEnv.ReadLayer(outPath);
                Program.RequireWithWarn((int)l.GetFeatureCount() >= 1, "line-to-polygon produced output",
                    "0 polygons (source lines likely not closed rings)");
            });
        }

        // Symmetric difference of a layer with itself: area ~ 0 (A Δ A = ∅).
        await Program.Checks.RunAsync("sym-difference-self", async () =>
        {
            var outPath = Path.Combine(dir, "symdiff.shp");
            await Program.RunRegisteredAsync("sym-difference", new()
            {
                ["input1"] = poly.ShpPath, ["input2"] = poly.ShpPath, ["output"] = outPath
            }, "sym-difference");
            var l = GdalEnv.ReadLayer(outPath);
            double area = l.Features.Where(f => !string.IsNullOrEmpty(f.Wkt))
                .Sum(f => GeometryUtil.AreaWkt(f.Wkt!));
            Program.RequireWithWarn(area < 1e-6, "A Δ A ≈ 0 area",
                $"area={area:E3} (tiny residual from overlay is acceptable)");
        });

        // Batch format conversion: every polygon layer converts with count preserved.
        await Program.Checks.RunAsync("batch-convert", async () =>
        {
            var inDir = Path.Combine(dir, "batch_in");
            var outDir = Path.Combine(dir, "batch_out");
            Directory.CreateDirectory(inDir);
            foreach (var ext in new[] { ".shp", ".dbf", ".shx", ".prj", ".cpg" })
            {
                var src = Path.ChangeExtension(poly.ShpPath, ext);
                if (File.Exists(src)) File.Copy(src, Path.Combine(inDir, Path.GetFileName(src)), overwrite: true);
            }
            await Program.RunRegisteredAsync("batch-convert", new()
            {
                ["inputFolder"] = inDir, ["outputFolder"] = outDir,
                ["sourceFormat"] = "SHP", ["targetFormat"] = "GeoJSON"
            }, "batch-convert");
            var geojson = Path.Combine(outDir, Path.GetFileNameWithoutExtension(poly.ShpPath) + ".geojson");
            Program.Require(File.Exists(geojson), "batch-convert produced GeoJSON", geojson);
            var l = GdalEnv.ReadLayer(geojson);
            Program.Require((int)l.GetFeatureCount() == poly.ExpectedCount, "batch-convert count preserved",
                $"got {l.GetFeatureCount()}, want {poly.ExpectedCount}");
        });
    }
}
