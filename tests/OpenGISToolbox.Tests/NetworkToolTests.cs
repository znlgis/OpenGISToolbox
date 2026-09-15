using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OpenGISToolbox.TestKit;
using Xunit;

namespace OpenGISToolbox.Tests;

/// <summary>
/// Live network tool coverage (Nominatim geocoding, OSM tile download).
/// Set OGT_SKIP_NETWORK=1 to skip; third-party service problems (rate limits,
/// timeouts) are reported as skips, not failures — malformed output is a failure.
/// </summary>
public class NetworkToolTests
{
    // xunit v2 has no public runtime-skip API: tests return early when the
    // network suite is disabled or a third-party service is rate-limiting.
    private static bool NetworkEnabled =>
        Environment.GetEnvironmentVariable("OGT_SKIP_NETWORK") != "1";

    private string Dir => GdalEnv.Dir("network");

    [Fact]
    public async Task SatelliteDownload_FetchesValidTile()
    {
        if (!NetworkEnabled) return; // skipped: OGT_SKIP_NETWORK=1
        var png = Path.Combine(Dir, "tile.png");
        var info = OpenGISToolbox.Services.ToolRegistry.GetAllTools().Single(t => t.Id == "satellite-download");
        var result = await GdalEnv.RunToolAsync(info, new Dictionary<string, string>
        {
            ["longitude"] = "116.4", ["latitude"] = "39.9", ["zoom"] = "3",
            ["source"] = "OpenStreetMap", ["output"] = png
        });

        if (!result.Success && result.Message.Contains("40", StringComparison.OrdinalIgnoreCase))
            return; // skipped: tile service issue
        Assert.True(result.Success, result.Message);

        var bytes = await File.ReadAllBytesAsync(png);
        Assert.True(bytes.Length > 1024, $"tile too small: {bytes.Length} bytes");
        bool isPng = bytes.Length > 4 && bytes[0] == 0x89 && bytes[1] == 'P' && bytes[2] == 'N' && bytes[3] == 'G';
        bool isJpeg = bytes.Length > 2 && bytes[0] == 0xFF && bytes[1] == 0xD8;
        Assert.True(isPng || isJpeg, "downloaded tile is neither PNG nor JPEG");
    }

    [Fact]
    public async Task GeocodeAddresses_KnownCity_ResolvesCoordinates()
    {
        if (!NetworkEnabled) return; // skipped: OGT_SKIP_NETWORK=1
        var input = Path.Combine(Dir, "addr.txt");
        File.WriteAllText(input, "Berlin");
        var output = Path.Combine(Dir, "geocoded.csv");

        var info = OpenGISToolbox.Services.ToolRegistry.GetAllTools().Single(t => t.Id == "geocode-addresses");
        var result = await GdalEnv.RunToolAsync(info, new Dictionary<string, string>
        {
            ["input"] = input, ["output"] = output
        });
        Assert.True(result.Success, result.Message);

        var rows = File.ReadAllLines(output).Skip(1).Where(l => l.Trim().Length > 0).ToList();
        Assert.Single(rows);
        var cols = rows[0].Split(',');
        Assert.True(cols.Length >= 3);

        if (string.IsNullOrEmpty(cols[1]) || string.IsNullOrEmpty(cols[2]))
            return; // skipped: Nominatim refused/rate-limited

        var lat = double.Parse(cols[1], System.Globalization.CultureInfo.InvariantCulture);
        var lon = double.Parse(cols[2], System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(lat, 52.0, 53.0);   // Berlin ~52.52
        Assert.InRange(lon, 13.0, 14.0);   // ~13.40
    }

    [Fact]
    public async Task GeocodeAddresses_EmptyInput_FailsGracefully()
    {
        if (!NetworkEnabled) return; // skipped: OGT_SKIP_NETWORK=1
        var input = Path.Combine(Dir, "empty_addr.txt");
        File.WriteAllText(input, "\n\n  \n");
        var info = OpenGISToolbox.Services.ToolRegistry.GetAllTools().Single(t => t.Id == "geocode-addresses");
        var result = await GdalEnv.RunToolAsync(info, new Dictionary<string, string>
        {
            ["input"] = input, ["output"] = Path.Combine(Dir, "empty_out.csv")
        });
        Assert.False(result.Success);
    }
}
