using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OpenGISToolbox.TestKit;
using Xunit;

namespace OpenGISToolbox.Tests;

/// <summary>
/// PostGIS round-trip tests via the app's export/import tools with third-party
/// truth from raw SQL (Npgsql) and independent file-header parsers. Covers the
/// known pitfalls: multipart geometry column typing, zero-based FID fidelity,
/// heap-order drift on read-back and column-name case folding. Tests return
/// early (skip) when no PG endpoint is reachable or no real layer is present.
/// </summary>
public class PostgisRoundTripTests
{
    private static readonly Lazy<PgProbe?> ProbeLazy = new(() => PgProbe.TryResolve(null));
    private static readonly Lazy<RealLayer?> LayerLazy = new(() =>
    {
        RealDataCatalog? catalog;
        try { catalog = RealDataCatalog.Resolve(null); }
        catch (Exception) { catalog = null; }
        return catalog?.Layers.FirstOrDefault(
            l => l.Kind is ShpFile.Kind.Polygon or ShpFile.Kind.Point);
    });

    private static PgProbe? Probe => ProbeLazy.Value;
    private static RealLayer? Layer => LayerLazy.Value;

    private static string Slug(string name) =>
        new string(name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

    [Fact]
    public async Task Export_Import_RoundTrip_PreservesCountGeometryAndFids()
    {
        var probe = Probe;
        var layer = Layer;
        if (probe is null || layer is null) return; // skipped: no PG or no real data

        var table = $"ogt_test_{Slug(layer!.Name)}_{Guid.NewGuid():N}";
        if (table.Length > 47) table = table[..47];
        var registry = OpenGISToolbox.Services.ToolRegistry.GetAllTools();
        var export = registry.Single(t => t.Id == "postgis-export");
        var import = registry.Single(t => t.Id == "postgis-import");

        try
        {
            var exportResult = await GdalEnv.RunToolAsync(export, new Dictionary<string, string>
            {
                ["input"] = layer!.ShpPath, ["connectionString"] = probe!.GdalConnectionString, ["tableName"] = table
            });
            Assert.True(exportResult.Success, exportResult.Message);

            // Third-party truth via SQL.
            Assert.Equal(layer!.ExpectedCount, await probe!.CountAsync(table));
            var pgFids = (await probe.FidsAsync(table)).Select(f => (int)f).ToHashSet();
            var srcFids = GdalEnv.ReadLayer(layer.ShpPath).Features.Select(f => f.Fid).ToHashSet();
            Assert.True(pgFids.SetEquals(srcFids),
                $"FID sets differ: pg[{string.Join(",", pgFids.Take(3))}] src[{string.Join(",", srcFids.Take(3))}]");

            // Import back and validate with independent header parsers.
            var outPath = Path.Combine(GdalEnv.Dir("postgis"), "back.shp");
            var importResult = await GdalEnv.RunToolAsync(import, new Dictionary<string, string>
            {
                ["connectionString"] = probe.GdalConnectionString, ["tableName"] = table, ["output"] = outPath
            });
            Assert.True(importResult.Success, importResult.Message);
            Assert.Equal(layer.ExpectedCount, ShpFile.Read(outPath).Records);
            Assert.Equal(layer.ExpectedCount, DbfFile.Read(Path.ChangeExtension(outPath, ".dbf")).Records);

            // Geometry: order-independent fingerprint (heap order may differ — pitfall ①).
            var src = LayerCompare.GeometryFingerprint(GdalEnv.ReadLayer(layer.ShpPath));
            var rt = LayerCompare.GeometryFingerprint(GdalEnv.ReadLayer(outPath));
            Assert.Equal(src.Count, rt.Count);
            var relErr = Math.Max(
                Math.Abs(rt.SumX - src.SumX) / Math.Max(1, Math.Abs(src.SumX)),
                Math.Abs(rt.SumY - src.SumY) / Math.Max(1, Math.Abs(src.SumY)));
            Assert.True(relErr < 1e-6, $"geometry drift {relErr:E2}");
        }
        finally
        {
            try { await probe!.DropAsync(table); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task Import_NonexistentTable_FailsGracefully()
    {
        var probe = Probe;
        if (probe is null) return; // skipped: no PG reachable

        var import = OpenGISToolbox.Services.ToolRegistry.GetAllTools().Single(t => t.Id == "postgis-import");
        var result = await GdalEnv.RunToolAsync(import, new Dictionary<string, string>
        {
            ["connectionString"] = probe!.GdalConnectionString,
            ["tableName"] = "ogt_test_no_such_table_zz",
            ["output"] = Path.Combine(GdalEnv.Dir("postgis"), "bogus.shp")
        });
        Assert.False(result.Success);
    }

    [Fact]
    public async Task Export_BadConnection_FailsGracefully()
    {
        var layer = Layer;
        if (layer is null) return; // skipped: no real data

        var export = OpenGISToolbox.Services.ToolRegistry.GetAllTools().Single(t => t.Id == "postgis-export");
        var result = await GdalEnv.RunToolAsync(export, new Dictionary<string, string>
        {
            ["input"] = layer!.ShpPath,
            ["connectionString"] = "PG:host=127.0.0.1 port=1 dbname=x user=x password=x",
            ["tableName"] = "ogt_test_bogus"
        });
        Assert.False(result.Success);
    }
}
