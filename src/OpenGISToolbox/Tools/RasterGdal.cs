using System;

namespace OpenGISToolbox.Tools;

/// <summary>
/// Shared bootstrap for the GDAL-backed raster tools. Ensures the bundled MaxRev
/// runtime is configured exactly once, lazily, without any dependency on a
/// system-wide GDAL installation (which would otherwise collide per PATH/env).
/// </summary>
internal static class RasterGdal
{
    private static readonly Lazy<bool> Init = new(() =>
    {
        MaxRev.Gdal.Core.GdalBase.ConfigureAll();
        return true;
    });

    public static void Ensure() => _ = Init.Value;

    /// <summary>Common raster input file filter.</summary>
    public const string RasterFilter = "GeoTIFF|*.tif|*.tiff|All files|*.*";
}
