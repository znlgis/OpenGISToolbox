using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OpenGIS.Utils.DataSource;
using OpenGIS.Utils.Engine.Enums;
using OpenGIS.Utils.Engine.Model.Layer;
using OpenGISToolbox.Models;
using OpenGISToolbox.Tools;
using Xunit;

namespace OpenGISToolbox.Tests;

/// <summary>
/// Helpers to run tools and build/verify real GIS data.
/// </summary>
public static class TestEnv
{
    static TestEnv()
    {
        // Avoid conflicts with a system-wide GDAL install (e.g. OSGeo4W):
        // its plugin dir / data dir breaks the bundled MaxRev GDAL runtime.
        Environment.SetEnvironmentVariable("GDAL_DRIVER_PATH", null);
        Environment.SetEnvironmentVariable("GDAL_DATA", null);
        Environment.SetEnvironmentVariable("PROJ_LIB", null);
        Environment.SetEnvironmentVariable("PROJ_DATA", null);
    }

    public static string Root { get; } = Path.Combine(Path.GetTempPath(), "ogistoolbox-tests-" + Guid.NewGuid().ToString("N")[..8]);

    /// <summary>Initializes the bundled GDAL runtime exactly once, before any tool runs.</summary>
    public static readonly Lazy<bool> GdalInitialized = new(() =>
    {
        MaxRev.Gdal.Core.GdalBase.ConfigureAll();
        return true;
    });

    public static string Dir(params string[] parts)
    {
        var p = Path.Combine(Root, Path.Combine(parts));
        Directory.CreateDirectory(p);
        return p;
    }

    public static async Task<ToolResult> RunAsync(ToolBase tool, Dictionary<string, string> parameters)
    {
        _ = GdalInitialized.Value;
        var info = tool.ToToolInfo();
        Assert.NotNull(info.ExecuteAsync);
        return await info.ExecuteAsync!(parameters, null, CancellationToken.None);
    }

    public static void AssertSucceeded(ToolResult result)
    {
        Assert.True(result.Success, $"Tool failed: {result.Message}");
    }

    // ─── Vector data builders ───

    public static void WriteLayer(Action<OguLayer> configure, string path, DataFormatType? format = null)
    {
        var layer = new OguLayer
        {
            Name = Path.GetFileNameWithoutExtension(path)
        };
        configure(layer);
        var fmt = format ?? DetectFormat(path);
        OguLayerUtil.WriteLayer(fmt, layer, path);
    }

    public static DataFormatType DetectFormat(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".shp" => DataFormatType.SHP,
        ".geojson" or ".json" => DataFormatType.GEOJSON,
        ".gpkg" => DataFormatType.GEOPACKAGE,
        ".kml" => DataFormatType.KML,
        _ => throw new NotSupportedException(Path.GetExtension(path))
    };

    public static OguLayer ReadLayer(string path) => OguLayerUtil.ReadLayer(DetectFormat(path), path);

    /// <summary>Builds a polygon layer with squares of known size in UTM 50N (EPSG:32650).</summary>
    public static string MakeSquaresShp(string dir, string name = "squares.shp", double sizeDeg = 0.01)
    {
        var path = Path.Combine(dir, name);
        WriteLayer(l =>
        {
            l.GeometryType = GeometryType.POLYGON;
            l.Wkid = 4326;
            l.AddField(new OguField { Name = "id", DataType = FieldDataType.STRING, Length = 20 });
            l.AddField(new OguField { Name = "label", DataType = FieldDataType.STRING, Length = 50 });
            void AddSquare(int fid, double x0, double y0, string label)
            {
                var x1 = x0 + sizeDeg; var y1 = y0 + sizeDeg;
                l.AddFeature(new OguFeature
                {
                    Fid = fid,
                    Wkt = $"POLYGON (({x0} {y0}, {x1} {y0}, {x1} {y1}, {x0} {y1}, {x0} {y0}))",
                }.Also(f => { f.SetValue("id", fid.ToString()); f.SetValue("label", label); }));
            }
            AddSquare(0, 116.30, 39.90, "A");
            AddSquare(1, 116.32, 39.90, "B");
            AddSquare(2, 116.31, 39.91, "C"); // overlaps A and B
        }, path);
        return path;
    }

    public static string MakePointsGeoJson(string dir)
    {
        var path = Path.Combine(dir, "points.geojson");
        WriteLayer(l =>
        {
            l.GeometryType = GeometryType.POINT;
            l.Wkid = 4326;
            l.AddField(new OguField { Name = "city", DataType = FieldDataType.STRING, Length = 50 });
            l.AddFeature(new OguFeature { Fid = 0, Wkt = "POINT (116.397 39.909)" }.Also(f => f.SetValue("city", "Beijing")));
            l.AddFeature(new OguFeature { Fid = 1, Wkt = "POINT (121.474 31.231)" }.Also(f => f.SetValue("city", "Shanghai")));
            l.AddFeature(new OguFeature { Fid = 2, Wkt = "POINT (113.265 23.129)" }.Also(f => f.SetValue("city", "Guangzhou")));
        }, path);
        return path;
    }

    public static string MakeLinesShp(string dir)
    {
        var path = Path.Combine(dir, "lines.shp");
        WriteLayer(l =>
        {
            l.GeometryType = GeometryType.LINESTRING;
            l.Wkid = 4326;
            l.AddField(new OguField { Name = "name", DataType = FieldDataType.STRING, Length = 50 });
            l.AddFeature(new OguFeature { Fid = 0, Wkt = "LINESTRING (116.0 39.0, 117.0 39.0, 117.0 40.0)" }.Also(f => f.SetValue("name", "L1")));
            // zigzag line with many vertices for simplification tests
            var pts = new List<string>();
            for (int i = 0; i <= 50; i++)
                pts.Add($"{(116.0 + i * 0.001).ToString(System.Globalization.CultureInfo.InvariantCulture)} {(39.5 + (i % 2) * 0.00005).ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            l.AddFeature(new OguFeature { Fid = 1, Wkt = "LINESTRING (" + string.Join(", ", pts) + ")" }.Also(f => f.SetValue("name", "zigzag")));
        }, path);
        return path;
    }

    public static string MakeInvalidPolygons(string dir)
    {
        var path = Path.Combine(dir, "invalid.geojson");
        WriteLayer(l =>
        {
            l.GeometryType = GeometryType.POLYGON;
            l.Wkid = 4326;
            // bowtie (self-intersecting)
            l.AddFeature(new OguFeature { Fid = 0, Wkt = "POLYGON ((116.0 39.0, 116.1 39.1, 116.1 39.0, 116.0 39.1, 116.0 39.0))" });
            // valid square
            l.AddFeature(new OguFeature { Fid = 1, Wkt = "POLYGON ((117.0 39.0, 117.1 39.0, 117.1 39.1, 117.0 39.1, 117.0 39.0))" });
        }, path);
        return path;
    }
}

public static class Extensions
{
    public static T Also<T>(this T obj, Action<T> action) { action(obj); return obj; }
}
