using System.Net;
using OpenGISToolbox.TestKit;

namespace OpenGISToolbox.RealDataHarness.Sections;

/// <summary>
/// Live network checks (Nominatim geocoding, OSM tile download).
/// Skipped entirely when OGT_SKIP_NETWORK=1. HTTP-level problems are Warnings
/// (the third-party services can rate-limit), parse failures are Failures.
/// </summary>
public static class Network
{
    public static async Task Run()
    {
        Program.Checks.BeginSection("8. Network tools (live)");

        if (Environment.GetEnvironmentVariable("OGT_SKIP_NETWORK") == "1")
        {
            Program.Checks.Skip("geocode", "OGT_SKIP_NETWORK=1");
            Program.Checks.Skip("satellite tile", "OGT_SKIP_NETWORK=1");
            return;
        }

        var dir = GdalEnv.Dir("harness", "network");

        await Program.Checks.RunAsync("geocode-addresses", async () =>
        {
            var input = Synth.MakeAddressCsv(dir);
            var output = Path.Combine(dir, "geocoded.csv");
            var result = await GdalEnv.RunToolAsync(Program.Registry["geocode-addresses"], new()
            {
                ["input"] = input, ["output"] = output
            });
            if (!result.Success)
            {
                Program.Checks.Warn("geocode-addresses", $"service issue: {result.Message}");
                return;
            }
            var rows = File.ReadAllLines(output).Skip(1).Where(l => l.Trim().Length > 0).ToList();
            if (rows.Count != 2)
            {
                Program.Checks.Fail("geocode output rows", $"expected 2 rows, got {rows.Count}");
                return;
            }
            var anyGeocoded = rows.Any(r =>
            {
                var cols = r.Split(',');
                return cols.Length >= 3 &&
                       double.TryParse(cols[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lat) &&
                       double.TryParse(cols[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lon) &&
                       Math.Abs(lat) <= 90 && Math.Abs(lon) <= 180 && (lat != 0 || lon != 0);
            });
            if (anyGeocoded) Program.Checks.Pass("geocode parsed lat/lon");
            else Program.Checks.Warn("geocode parsed lat/lon", "no coordinates returned (Nominatim likely rate-limited)");
        });

        await Program.Checks.RunAsync("satellite-download", async () =>
        {
            var png = Path.Combine(dir, "tile.png");
            try
            {
                await Program.RunRegisteredAsync("satellite-download", new()
                {
                    ["longitude"] = "116.4", ["latitude"] = "39.9", ["zoom"] = "3",
                    ["source"] = "OpenStreetMap", ["output"] = png
                }, "satellite-download");
            }
            catch (WebException)
            {
                Program.Checks.Warn("satellite-download", "tile service unreachable");
                return;
            }
            if (!File.Exists(png)) { Program.Checks.Fail("satellite tile file", "missing"); return; }
            var bytes = await File.ReadAllBytesAsync(png);
            bool isPng = bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == 'P' && bytes[2] == 'N' && bytes[3] == 'G';
            bool isJpeg = bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8;
            Program.Require(bytes.Length > 1024 && (isPng || isJpeg), "tile decodes as PNG/JPEG >1KB",
                $"size={bytes.Length} magic={bytes[..Math.Min(4, bytes.Length)]}");
        });
    }
}
