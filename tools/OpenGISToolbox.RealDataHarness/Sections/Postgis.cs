using OpenGISToolbox.TestKit;

namespace OpenGISToolbox.RealDataHarness.Sections;

/// <summary>
/// PostGIS round trips via the app's own export/import tools, with third-party
/// truth from raw SQL (Npgsql) and file-header parsing. Covers the known
/// pitfalls: FID fidelity on write (⑥), heap-order drift on read (①),
/// column-name case folding on attribute compare (②).
/// </summary>
public static class Postgis
{
    public static async Task Run()
    {
        Program.Checks.BeginSection("6. PostGIS round trip");

        var log = new Progress<string>(s => Console.WriteLine($"  [pg] {s}"));
        var probe = PgProbe.TryResolve(log);
        if (probe == null)
        {
            Program.Checks.Skip("postgis sweep", "no reachable PostgreSQL/PostGIS (set OGT_TEST_PG_* or start the local container)");
            await PostgisGracefulFailures(unavailable: true);
            return;
        }

        foreach (var layer in Program.Catalog.Layers.Where(l => l.Kind is ShpFile.Kind.Polygon or ShpFile.Kind.Point).Take(2))
        {
            var table = $"ogt_test_{Slug(layer.Name)}_{Guid.NewGuid():N}";
            if (table.Length > 47) table = table[..47];
            try
            {
                await Program.Checks.RunAsync($"{layer.Name}: export to PG", async () =>
                {
                    await Program.RunRegisteredAsync("postgis-export", new()
                    {
                        ["input"] = layer.ShpPath, ["connectionString"] = probe.GdalConnectionString,
                        ["tableName"] = table
                    }, "postgis-export");
                    var pgCount = await probe.CountAsync(table);
                    Program.Require(pgCount == layer.ExpectedCount, $"{layer.Name}: SQL count == header count",
                        $"pg={pgCount}, header={layer.ExpectedCount}");
                    var fids = await probe.FidsAsync(table);
                    Program.Require(fids.Count == layer.ExpectedCount, $"{layer.Name}: FID row count",
                        $"{fids.Count}");
                    // Pitfall ⑥: zero-based source FIDs must survive without sequence collision.
                    var fidSet = fids.Select(f => (int)f).ToHashSet();
                    var srcFids = GdalEnv.ReadLayer(layer.ShpPath).Features.Select(f => f.Fid).ToHashSet();
                    Program.Require(fidSet.SetEquals(srcFids), $"{layer.Name}: FID fidelity (no collision/rebase)",
                        $"pg sample={string.Join(",", fids.Take(5))} src sample={string.Join(",", srcFids.Take(5))}");
                    var summary = await probe.GeomSummaryAsync(table);
                    Program.Require(summary.Contains(layer.Shp.ShapeTypeName, StringComparison.OrdinalIgnoreCase),
                        $"{layer.Name}: PG geometry type", summary);
                });

                await Program.Checks.RunAsync($"{layer.Name}: import back from PG", async () =>
                {
                    var outPath = Path.Combine(GdalEnv.Dir("harness", "postgis"), $"{layer.Name}_pg.shp");
                    await Program.RunRegisteredAsync("postgis-import", new()
                    {
                        ["connectionString"] = probe.GdalConnectionString, ["tableName"] = table,
                        ["output"] = outPath
                    }, "postgis-import");
                    var shp = ShpFile.Read(outPath);
                    var dbf = DbfFile.Read(Path.ChangeExtension(outPath, ".dbf"));
                    Program.Require(shp.Records == layer.ExpectedCount && dbf.Records == layer.ExpectedCount,
                        $"{layer.Name}: read-back header counts", $"shp={shp.Records} dbf={dbf.Records}");

                    // Row order after a PG round trip is not guaranteed (heap order,
                    // pitfall ①), so compare order-independent geometry fingerprints.
                    var srcFp = LayerCompare.GeometryFingerprint(GdalEnv.ReadLayer(layer.ShpPath));
                    var rtFp = LayerCompare.GeometryFingerprint(GdalEnv.ReadLayer(outPath));
                    var relErr = Math.Max(
                        Math.Abs(rtFp.SumX - srcFp.SumX) / Math.Max(1, Math.Abs(srcFp.SumX)),
                        Math.Abs(rtFp.SumY - srcFp.SumY) / Math.Max(1, Math.Abs(srcFp.SumY)));
                    Program.Require(srcFp.Count == rtFp.Count && relErr < 1e-6,
                        $"{layer.Name}: geometry fingerprint preserved",
                        $"count {srcFp.Count}->{rtFp.Count}, coord rel err {relErr:E2}");

                    // Attributes: multiset per field, case-insensitive names (PG folds
                    // identifiers to lower case, pitfall ②).
                    var srcAttrs = LayerCompare.AttributeMultiset(GdalEnv.ReadLayer(layer.ShpPath), new[] { "ogc_fid" });
                    var rtAttrs = LayerCompare.AttributeMultiset(GdalEnv.ReadLayer(outPath), new[] { "ogc_fid" });
                    var badFields = new List<string>();
                    foreach (var (field, bag) in srcAttrs)
                    {
                        if (!rtAttrs.TryGetValue(field, out var other))
                        {
                            badFields.Add($"{field}: missing");
                            continue;
                        }
                        var diff = bag.Keys.Count(k => !other.TryGetValue(k, out var n) || n != bag[k]);
                        diff += other.Keys.Count(k => !bag.ContainsKey(k));
                        if (diff > 0) badFields.Add($"{field}: {diff} value-group diffs");
                    }
                    Program.RequireWithWarn(badFields.Count == 0, $"{layer.Name}: attributes survive round trip",
                        string.Join("; ", badFields.Take(4)));
                });
            }
            finally
            {
                try { await probe.DropAsync(table); } catch { /* best effort */ }
            }
        }

        await PostgisGracefulFailures(unavailable: false);
    }

    static async Task PostgisGracefulFailures(bool unavailable)
    {
        var bogusConn = unavailable
            ? "PG:host=127.0.0.1 port=1 dbname=nobody user=nobody password=none"
            : new PgProbe().GdalConnectionString;

        await Program.Checks.RunAsync("import nonexistent table fails gracefully", async () =>
        {
            var outPath = Path.Combine(GdalEnv.Dir("harness", "postgis"), "bogus.shp");
            var result = await GdalEnv.RunToolAsync(Program.Registry["postgis-import"], new()
            {
                ["connectionString"] = bogusConn, ["tableName"] = "ogt_test_no_such_table_zz", ["output"] = outPath
            });
            Program.Require(!result.Success, "nonexistent table rejected", "tool reported success");
        });

        await Program.Checks.RunAsync("import bad connection fails gracefully", async () =>
        {
            var outPath = Path.Combine(GdalEnv.Dir("harness", "postgis"), "bogus2.shp");
            var result = await GdalEnv.RunToolAsync(Program.Registry["postgis-import"], new()
            {
                ["connectionString"] = "PG:host=127.0.0.1 port=1 dbname=x user=x password=x",
                ["tableName"] = "whatever", ["output"] = outPath
            });
            Program.Require(!result.Success, "bad connection rejected", "tool reported success");
        });

        await Program.Checks.RunAsync("export to corrupt connection fails gracefully", async () =>
        {
            var layer = Program.Catalog.Layers.FirstOrDefault();
            if (layer == null) { Program.Checks.Skip("export corrupt conn", "no layer"); return; }
            var result = await GdalEnv.RunToolAsync(Program.Registry["postgis-export"], new()
            {
                ["input"] = layer.ShpPath, ["connectionString"] = "not-a-connection-string",
                ["tableName"] = "ogt_test_bogus"
            });
            Program.Require(!result.Success, "export corrupt conn rejected", "tool reported success");
        });
    }

    static string Slug(string name) =>
        new string(name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
}
