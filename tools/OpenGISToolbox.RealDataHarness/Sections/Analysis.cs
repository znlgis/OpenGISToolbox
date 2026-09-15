using System.Globalization;
using System.Text.RegularExpressions;
using OpenGISToolbox.TestKit;

namespace OpenGISToolbox.RealDataHarness.Sections;

/// <summary>Attribute/spatial analysis tools with exact expectations from DBF byte-level parsing.</summary>
public static class Analysis
{
    public static async Task Run()
    {
        Program.Checks.BeginSection("4. Analysis sweep");

        var poly = Program.Catalog.Polygon;
        var line = Program.Catalog.Line;
        var dir = GdalEnv.Dir("harness", "analysis");

        if (poly == null)
        {
            Program.Checks.Skip("analysis sweep", "no polygon layer");
            return;
        }

        // Spatial filter: padded full bbox keeps everything; a far-away extent keeps nothing.
        await Program.Checks.RunAsync("spatial-filter full/padding", async () =>
        {
            var outPath = Path.Combine(dir, "filter_all.shp");
            await Program.RunRegisteredAsync("spatial-filter", new()
            {
                ["input"] = poly.ShpPath, ["output"] = outPath,
                ["extentWkt"] = poly.BboxAsExtentWkt(1.0)
            }, "spatial-filter(all)");
            var l = GdalEnv.ReadLayer(outPath);
            Program.Require((int)l.GetFeatureCount() == poly.ExpectedCount, "spatial-filter padded bbox == N",
                $"got {l.GetFeatureCount()}, want {poly.ExpectedCount}");
        });

        await Program.Checks.RunAsync("spatial-filter far-away", async () =>
        {
            var outPath = Path.Combine(dir, "filter_none.shp");
            // Stack a same-width strip above the layer bbox, shifted beyond any real
            // geometry (all features lie within [ymin,ymax] by definition of the bbox).
            var yrange = Math.Max(1e-9, poly.Bbox[3] - poly.Bbox[1]);
            var shift = 3 * yrange + 10;
            var (x0, y0, x1, y1) = (poly.Bbox[0], poly.Bbox[1] + shift, poly.Bbox[2], poly.Bbox[3] + shift);
            var wkt = string.Format(CultureInfo.InvariantCulture,
                "POLYGON (({0} {1}, {2} {1}, {2} {3}, {0} {3}, {0} {1}))", x0, y0, x1, y1);
            await Program.RunRegisteredAsync("spatial-filter", new()
            {
                ["input"] = poly.ShpPath, ["output"] = outPath, ["extentWkt"] = wkt
            }, "spatial-filter(none)");
            var l = GdalEnv.ReadLayer(outPath);
            Program.Require((int)l.GetFeatureCount() == 0, "spatial-filter far-away == 0",
                $"got {l.GetFeatureCount()}");
        });

        // Attribute query with an exact expected count from the DBF byte scan.
        var pair = poly.PickAsciiWherePair();
        if (pair == null)
        {
            Program.Checks.Skip("attribute-query exactness", "no ASCII-safe repeated character value");
        }
        else
        {
            await Program.Checks.RunAsync("attribute-query", async () =>
            {
                var (field, value, matches) = pair.Value;
                var outPath = Path.Combine(dir, "query.shp");
                await Program.RunRegisteredAsync("attribute-query", new()
                {
                    ["input"] = poly.ShpPath, ["output"] = outPath,
                    ["whereClause"] = $"{field} = '{value.Replace("'", "''")}'"
                }, "attribute-query");
                var l = GdalEnv.ReadLayer(outPath);
                Program.Require((int)l.GetFeatureCount() == matches,
                    $"attribute-query {field}='{value}' exact count",
                    $"got {l.GetFeatureCount()}, want {matches} (DBF scan)");

                var allOut = Path.Combine(dir, "query_all.shp");
                await Program.RunRegisteredAsync("attribute-query", new()
                {
                    ["input"] = poly.ShpPath, ["output"] = allOut, ["whereClause"] = "1=1"
                }, "attribute-query(1=1)");
                var all = GdalEnv.ReadLayer(allOut);
                Program.Require((int)all.GetFeatureCount() == poly.ExpectedCount, "attribute-query 1=1 == N",
                    $"got {all.GetFeatureCount()}");

                var noneOut = Path.Combine(dir, "query_none.shp");
                await Program.RunRegisteredAsync("attribute-query", new()
                {
                    ["input"] = poly.ShpPath, ["output"] = noneOut,
                    ["whereClause"] = $"{field} = 'NO_SUCH_VALUE_ZZ_9x7'"
                }, "attribute-query(nomatch)");
                var none = GdalEnv.ReadLayer(noneOut);
                Program.Require((int)none.GetFeatureCount() == 0, "attribute-query no match == 0",
                    $"got {none.GetFeatureCount()}");
            });
        }

        // Calculate area / length are report tools: parse the totals back out.
        await Program.Checks.RunAsync("calculate-area", async () =>
        {
            var result = await Program.RunRegisteredAsync("calculate-area", new() { ["input"] = poly.ShpPath },
                "calculate-area");
            var m = Regex.Match(result.Message, @"Total Area:\s*([0-9.,]+)", RegexOptions.IgnoreCase);
            Program.Require(m.Success, "calculate-area total present", result.Message[..Math.Min(120, result.Message.Length)]);
            if (m.Success)
            {
                var total = double.Parse(m.Groups[1].Value.Replace(",", ""), CultureInfo.InvariantCulture);
                Program.Require(total > 0, "calculate-area total > 0", $"total={total}");
            }
        });

        if (line != null)
        {
            await Program.Checks.RunAsync("calculate-length", async () =>
            {
                var result = await Program.RunRegisteredAsync("calculate-length", new() { ["input"] = line.ShpPath },
                    "calculate-length");
                var m = Regex.Match(result.Message, @"Total Length:\s*([0-9.,]+)", RegexOptions.IgnoreCase);
                Program.Require(m.Success, "calculate-length total present",
                    result.Message[..Math.Min(120, result.Message.Length)]);
                if (m.Success)
                {
                    var total = double.Parse(m.Groups[1].Value.Replace(",", ""), CultureInfo.InvariantCulture);
                    Program.Require(total > 0, "calculate-length total > 0", $"total={total}");
                }
            });
        }
    }
}
