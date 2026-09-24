using System.Text;
using OpenGISToolbox.Models;
using OpenGISToolbox.TestKit;

namespace OpenGISToolbox.RealDataHarness;

/// <summary>
/// Data-agnostic end-to-end harness: exercises every registered toolbox tool
/// against real vector data whose expectations are parsed directly from the
/// data files (DBF/SHP/GeoJSON headers), plus the PostGIS round-trip, raster,
/// GPS and optional network paths. Exit code 1 when any check FAILs.
///
/// Environment:
///   OGT_REAL_DATA_DIR   directory with real .shp/.dbf pairs (else Natural Earth download cache)
///   OGT_REAL_DATA_CACHE cache location for the Natural Earth anchor set
///   OGT_TEST_PG_*         PostGIS endpoint overrides (defaults 127.0.0.1:5432 postgres)
///   OGT_SKIP_NETWORK=1  skip live network checks (geocode / satellite tiles)
/// </summary>
public static class Program
{
    public static CheckRunner Checks = new();
    public static RealDataCatalog Catalog = null!;
    public static Dictionary<string, ToolInfo> Registry = new();
    public static string Scratch = null!;

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        GdalEnv.Ensure();

        var log = new Progress<string>(s => Console.WriteLine($"  [data] {s}"));
        Console.WriteLine("OpenGISToolbox real-data harness");
        Console.WriteLine($"scratch: {GdalEnv.ScratchRoot}");

        Catalog = RealDataCatalog.Resolve(log)
                  ?? throw new InvalidOperationException(
                      "No real data available: set OGT_REAL_DATA_DIR or allow Natural Earth download.");
        Registry = ToolRegistrySnapshot();
        Scratch = GdalEnv.Dir("harness");

        Console.WriteLine($"source: {Catalog.SourceDescription}");
        Console.WriteLine($"layers: {string.Join(", ", Catalog.Layers.Select(l => $"{l.Name}[{l.Kind} x{l.ExpectedCount}]"))}");

        await Sections.CatalogIntegrity.Run();
        await Sections.Conversion.Run();
        await Sections.Geometry.Run();
        await Sections.Analysis.Run();
        await Sections.Coordinates.Run();
        await Sections.Postgis.Run();
        await Sections.RasterAndMisc.Run();
        await Sections.Network.Run();
        await Sections.ErrorMatrix.Run();
        await Sections.Sprint1.Run();
        await Sections.Sprint2.Run();
        await Sections.Sprint3.Run();

        Checks.PrintSummary();
        return Checks.ExitCode;
    }

    private static Dictionary<string, ToolInfo> ToolRegistrySnapshot()
    {
        // ToolRegistry pulls in Models only — no Avalonia bootstrap needed.
        return OpenGISToolbox.Services.ToolRegistry.GetAllTools().ToDictionary(t => t.Id);
    }

    /// <summary>Runs a registered tool and fails the check when it reports failure.</summary>
    public static async Task<ToolResult> RunRegisteredAsync(string id, Dictionary<string, string> parameters,
        string caseName, bool expectSuccess = true)
    {
        var info = Registry[id];
        var result = await GdalEnv.RunToolAsync(info, parameters);
        if (expectSuccess && !result.Success)
            Checks.Fail(caseName, $"tool {id} reported failure: {result.Message}");
        else if (!expectSuccess && result.Success)
            Checks.Fail(caseName, $"tool {id} unexpectedly succeeded");
        return result;
    }

    public static void Require(bool condition, string caseName, string failDetail)
    {
        if (condition) Checks.Pass(caseName);
        else Checks.Fail(caseName, failDetail);
    }

    public static void RequireWithWarn(bool condition, string caseName, string warnDetail)
    {
        if (condition) Checks.Pass(caseName);
        else Checks.Warn(caseName, warnDetail);
    }
}
