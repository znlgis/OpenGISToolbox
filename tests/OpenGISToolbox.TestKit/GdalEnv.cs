using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using OpenGIS.Utils.DataSource;
using OpenGIS.Utils.Engine.Enums;
using OpenGIS.Utils.Engine.Model.Layer;
using OpenGISToolbox.Models;
using OpenGISToolbox.Tools;

namespace OpenGISToolbox.TestKit;

/// <summary>
/// Shared runtime bootstrap for real-data tests and harnesses:
/// GDAL environment hygiene, tool invocation without any UI, path helpers.
/// </summary>
public static class GdalEnv
{
    static GdalEnv()
    {
        // Avoid conflicts with a system-wide GDAL (OSGeo4W): its plugin/data dirs
        // break the bundled MaxRev runtime. Same hygiene as Program.Main of the app.
        Environment.SetEnvironmentVariable("GDAL_DRIVER_PATH", null);
        Environment.SetEnvironmentVariable("GDAL_DATA", null);
        Environment.SetEnvironmentVariable("PROJ_LIB", null);
        Environment.SetEnvironmentVariable("PROJ_DATA", null);
        // Tests are culture-invariant by design (numeric parsing, WKT formatting).
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
    }

    /// <summary>Initializes the bundled GDAL runtime exactly once.</summary>
    public static readonly Lazy<bool> Initialized = new(() =>
    {
        MaxRev.Gdal.Core.GdalBase.ConfigureAll();
        return true;
    });

    /// <summary>Touch the static ctor + GDAL init.</summary>
    public static void Ensure() => _ = Initialized.Value;

    /// <summary>Scratch working root; per-run unique, under %TEMP%.</summary>
    public static string ScratchRoot { get; } =
        Path.Combine(Path.GetTempPath(), "ogt-realdata-" + Guid.NewGuid().ToString("N")[..8]);

    public static string Dir(params string[] parts)
    {
        var p = Path.Combine(ScratchRoot, Path.Combine(parts));
        Directory.CreateDirectory(p);
        return p;
    }

    /// <summary>Runs a tool through the same ToolInfo pipeline the UI uses. No assertions.</summary>
    public static async Task<ToolResult> RunToolAsync(ToolBase tool, Dictionary<string, string> parameters)
    {
        Ensure();
        var info = tool.ToToolInfo();
        return await info.ExecuteAsync!(parameters, null, CancellationToken.None);
    }

    /// <summary>Runs an already-registered ToolInfo from the registry.</summary>
    public static async Task<ToolResult> RunToolAsync(Models.ToolInfo info, Dictionary<string, string> parameters)
    {
        Ensure();
        return await info.ExecuteAsync!(parameters, null, CancellationToken.None);
    }

    public static DataFormatType DetectFormat(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".shp" => DataFormatType.SHP,
        ".geojson" or ".json" => DataFormatType.GEOJSON,
        ".gpkg" => DataFormatType.GEOPACKAGE,
        ".kml" => DataFormatType.KML,
        ".dxf" => DataFormatType.DXF,
        ".gdb" => DataFormatType.FILEGDB,
        _ => throw new NotSupportedException("Unknown format: " + Path.GetExtension(path))
    };

    public static OguLayer ReadLayer(string path)
    {
        Ensure();
        return OguLayerUtil.ReadLayer(DetectFormat(path), path);
    }

    /// <summary>True if a GDAL driver with the given name exists in the bundled runtime.</summary>
    public static bool HasGdalDriver(string driverName)
    {
        Ensure();
        return OSGeo.OGR.Ogr.GetDriverByName(driverName) != null;
    }

    /// <summary>
    /// Probes whether a writable FileGDB can actually be created with the bundled drivers.
    /// Returns the working driver name ("OpenFileGDB" / "FileGDB") or null when unsupported.
    /// </summary>
    public static string? ProbeFileGdbWrite(string probeDir)
    {
        Ensure();
        foreach (var driverName in new[] { "OpenFileGDB", "FileGDB" })
        {
            var driver = OSGeo.OGR.Ogr.GetDriverByName(driverName);
            if (driver == null) continue;
            var gdbPath = Path.Combine(probeDir, $"probe_{driverName}_{Guid.NewGuid():N}.gdb");
            try
            {
                using var ds = driver.CreateDataSource(gdbPath, null);
                if (ds == null) continue;
                using var layer = ds.CreateLayer("probe_pt", null, OSGeo.OGR.wkbGeometryType.wkbPoint, null);
                if (layer != null) return driverName;
            }
            catch (Exception)
            {
                // driver present but cannot create; try next
            }
        }
        return null;
    }
}
