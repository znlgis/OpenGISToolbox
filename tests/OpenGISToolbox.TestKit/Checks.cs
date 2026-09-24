using System.Globalization;
using System.Text;
using OpenGIS.Utils.Engine.Model.Layer;

namespace OpenGISToolbox.TestKit;

/// <summary>Geometry/attribute comparison helpers shared by harness and xunit tests.</summary>
public static class LayerCompare
{
    /// <summary>Extracts all coordinate pairs from a WKT string (in file order).</summary>
    public static List<(double X, double Y)> ParseCoords(string wkt)
    {
        var result = new List<(double, double)>();
        var nums = new List<double>();
        var sb = new StringBuilder();
        foreach (var ch in wkt)
        {
            if (char.IsDigit(ch) || ch == '-' || ch == '+' || ch == '.' || ch == 'e' || ch == 'E')
                sb.Append(ch);
            else
            {
                Flush();
                sb.Clear();
            }
        }
        Flush();
        for (var i = 0; i + 1 < nums.Count; i += 2)
            result.Add((nums[i], nums[i + 1]));
        return result;

        void Flush()
        {
            if (sb.Length > 0 && double.TryParse(sb.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                nums.Add(v);
        }
    }

    /// <summary>Max per-vertex absolute deviation between two equal-length coordinate sequences.</summary>
    public static double MaxDeviation(string wktA, string wktB)
    {
        var a = ParseCoords(wktA);
        var b = ParseCoords(wktB);
        if (a.Count != b.Count) return double.PositiveInfinity;
        var max = 0.0;
        for (var i = 0; i < a.Count; i++)
        {
            max = Math.Max(max, Math.Abs(a[i].X - b[i].X));
            max = Math.Max(max, Math.Abs(a[i].Y - b[i].Y));
        }
        return max;
    }

    public static Dictionary<int, OguFeature> ByFid(OguLayer layer) => layer.Features.ToDictionary(f => f.Fid);

    /// <summary>
    /// Compares attribute dictionaries case-insensitively (PG folds identifiers to
    /// lower case — general pitfall ②), normalizing numeric invariant formatting.
    /// Returns a list of human-readable mismatches (empty == equal).
    /// </summary>
    public static List<string> CompareAttributes(OguFeature source, OguFeature target, IEnumerable<string> skipFields)
    {
        var mismatches = new List<string>();
        var skip = new HashSet<string>(skipFields, StringComparer.OrdinalIgnoreCase);
        var srcAttrs = FeatureAttributes(source);
        var dstAttrs = FeatureAttributes(target);

        foreach (var (name, value) in srcAttrs)
        {
            if (skip.Contains(name)) continue;
            if (!dstAttrs.TryGetValue(name, out var found))
            {
                mismatches.Add($"field '{name}' missing in target");
                continue;
            }
            if (Norm(value) != Norm(found))
                mismatches.Add($"field '{name}': '{value}' vs '{found}'");
        }
        return mismatches;
    }

    private static string? Norm(object? v) => v switch
    {
        null => null,
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        _ => v.ToString()?.Trim()
    };

    /// <summary>Attribute map of a feature, keys preserved as read.</summary>
    public static Dictionary<string, object?> FeatureAttributes(OguFeature feature)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, fv) in feature.Attributes) result[name] = fv?.Value;
        return result;
    }

    /// <summary>
    /// Order-independent geometry digest: feature count plus sums of all X/Y
    /// coordinates.immune to row-order drift (heap order after a PostGIS round
    /// trip — general pitfall ①) while still detecting lost/duplicated/corrupted
    /// geometries. A tiny signed error is expected from double round-tripping of
    /// many vertices, so callers compare with relative tolerance.
    /// </summary>
    public static (int Count, double SumX, double SumY) GeometryFingerprint(OguLayer layer)
    {
        double sx = 0, sy = 0;
        var count = 0;
        foreach (var f in layer.Features)
        {
            count++;
            if (string.IsNullOrEmpty(f.Wkt)) continue;
            foreach (var (x, y) in ParseCoords(f.Wkt))
            {
                sx += x;
                sy += y;
            }
        }
        return (count, sx, sy);
    }

    /// <summary>Per-field value multiset (trimmed strings, case-insensitive key) for order-free attribute comparison.</summary>
    public static Dictionary<string, SortedDictionary<string, int>> AttributeMultiset(OguLayer layer, IEnumerable<string> skipFields)
    {
        var skip = new HashSet<string>(skipFields, StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, SortedDictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var feature in layer.Features)
        {
            foreach (var (name, value) in FeatureAttributes(feature))
            {
                if (skip.Contains(name)) continue;
                if (!result.TryGetValue(name, out var bag))
                    result[name] = bag = new SortedDictionary<string, int>(StringComparer.Ordinal);
                var key = value?.ToString()?.Trim() ?? "<null>";
                if (double.TryParse(key, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                    key = d.ToString("R", CultureInfo.InvariantCulture); // fold "2" vs "2.0"
                bag[key] = bag.TryGetValue(key, out var c) ? c + 1 : 1;
            }
        }
        return result;
    }
}

/// <summary>Synthetic fixtures for formats that real-world data anchors do not provide.</summary>
public static class Synth
{
    /// <summary>
    /// Creates a 4-band float GeoTIFF with known values:
    /// band1 = 1..64 cycling mod 256, band2 = 0, band3 (red) = 2, band4 (nir) = 8.
    /// </summary>
    public static string MakeGeoTiff(string dir, string name = "synth.tif", int width = 8, int height = 8)
    {
        GdalEnv.Ensure();
        var path = Path.Combine(dir, name);
        var driver = OSGeo.GDAL.Gdal.GetDriverByName("GTiff")
                     ?? throw new InvalidOperationException("GTiff driver missing");
        using var ds = driver.Create(path, width, height, 4, OSGeo.GDAL.DataType.GDT_Float64, null);
        for (var b = 1; b <= 4; b++)
        {
            var data = new double[width * height];
            for (var i = 0; i < data.Length; i++)
                data[i] = b switch
                {
                    1 => (i + 1) % 256,
                    3 => 2.0,
                    4 => 8.0,
                    _ => 0.0
                };
            ds.GetRasterBand(b).WriteRaster(0, 0, width, height, data, width, height, 0, 0);
            ds.GetRasterBand(b).FlushCache();
        }
        ds.FlushCache();
        return path;
    }

    public static double[] ReadBand(string path, int band = 1)
    {
        GdalEnv.Ensure();
        using var ds = OSGeo.GDAL.Gdal.Open(path, OSGeo.GDAL.Access.GA_ReadOnly);
        var w = ds.RasterXSize;
        var h = ds.RasterYSize;
        var buf = new double[w * h];
        ds.GetRasterBand(band).ReadRaster(0, 0, w, h, buf, w, h, 0, 0);
        return buf;
    }

    /// <summary>GeoTIFF band dimensions + a single band read, for warp/clip/mosaic checks.</summary>
    public static (int Width, int Height, double[] Values, double[] GeoTransform) ReadRaster(string path, int band = 1)
    {
        GdalEnv.Ensure();
        using var ds = OSGeo.GDAL.Gdal.Open(path, OSGeo.GDAL.Access.GA_ReadOnly);
        var w = ds.RasterXSize;
        var h = ds.RasterYSize;
        var gt = new double[6];
        ds.GetGeoTransform(gt);
        var buf = new double[w * h];
        ds.GetRasterBand(band).ReadRaster(0, 0, w, h, buf, w, h, 0, 0);
        return (w, h, buf, gt);
    }

    private static OSGeo.GDAL.Dataset CreateSingleBandTiff(string path, int width, int height)
    {
        if (File.Exists(path)) File.Delete(path);
        var driver = OSGeo.GDAL.Gdal.GetDriverByName("GTiff")
                     ?? throw new InvalidOperationException("GTiff driver missing");
        return driver.Create(path, width, height, 1, OSGeo.GDAL.DataType.GDT_Float64, null);
    }

    /// <summary>Linear DEM ramp (elevation = column index), north-up, pixel size 1.</summary>
    public static string MakePlanarDem(string dir, string name = "dem.tif", int width = 10, int height = 5, int epsg = 0)
    {
        GdalEnv.Ensure();
        var path = Path.Combine(dir, name);
        using var ds = CreateSingleBandTiff(path, width, height);
        var data = new double[width * height];
        for (var r = 0; r < height; r++)
            for (var c = 0; c < width; c++)
                data[r * width + c] = c;
        ds.GetRasterBand(1).WriteRaster(0, 0, width, height, data, width, height, 0, 0);
        ds.SetGeoTransform(new double[] { 0, 1, 0, 0, 0, -1 });
        if (epsg > 0) SetEpsg(ds, epsg);
        ds.FlushCache();
        return path;
    }

    /// <summary>Constant-value single-band GeoTIFF with a north-up pixel-size-1 geotransform.</summary>
    public static string MakeConstantRaster(string dir, string name = "const.tif", int width = 4, int height = 4,
        double value = 7, double originX = 0, double originY = 0, int epsg = 0)
    {
        GdalEnv.Ensure();
        var path = Path.Combine(dir, name);
        using var ds = CreateSingleBandTiff(path, width, height);
        var data = new double[width * height];
        for (var i = 0; i < data.Length; i++) data[i] = value;
        ds.GetRasterBand(1).WriteRaster(0, 0, width, height, data, width, height, 0, 0);
        ds.SetGeoTransform(new double[] { originX, 1, 0, originY, 0, -1 });
        if (epsg > 0) SetEpsg(ds, epsg);
        ds.FlushCache();
        return path;
    }

    private static void SetEpsg(OSGeo.GDAL.Dataset ds, int epsg)
    {
        var srs = new OSGeo.OSR.SpatialReference(null);
        srs.ImportFromEPSG(epsg);
        srs.ExportToWkt(out string wkt, null);
        ds.SetProjection(wkt);
        srs.Dispose();
    }

    public static string MakeGpx(string dir, string name = "track.gpx", int waypoints = 3, int trackPoints = 5)
    {
        var path = Path.Combine(dir, name);
        var sb = new StringBuilder();
        sb.Append("""<?xml version="1.0" encoding="UTF-8"?>""");
        sb.Append("""<gpx version="1.1" creator="ogt-tests" xmlns="http://www.topografix.com/GPX/1/1">""");
        for (var i = 0; i < waypoints; i++)
            sb.Append($"""<wpt lat="{39.9 + i * 0.01:0.0000}" lon="{116.4 + i * 0.01:0.0000}"><name>WP{i}</name></wpt>""");
        sb.Append("<trk><name>T1</name><trkseg>");
        for (var i = 0; i < trackPoints; i++)
            sb.Append($"""<trkpt lat="{39.95 + i * 0.001:0.0000}" lon="{116.45 + i * 0.001:0.0000}"></trkpt>""");
        sb.Append("</trkseg></trk></gpx>");
        File.WriteAllText(path, sb.ToString());
        return path;
    }

    public static string MakeAddressCsv(string dir, string name = "addresses.csv")
    {
        var path = Path.Combine(dir, name);
        File.WriteAllLines(path, new[]
        {
            "Tiananmen Square, Beijing",
            "Eiffel Tower, Paris"
        });
        return path;
    }
}

/// <summary>Pass/Warn/Fail/Skip bookkeeping for console harnesses.</summary>
public sealed class CheckRunner
{
    public enum Status { Pass, Warn, Fail, Skip }

    public sealed record Case(string Section, string Name, Status State, string Detail);

    private readonly List<Case> _cases = new();
    public IReadOnlyList<Case> Cases => _cases;
    public string Section { get; private set; } = "-";

    public void BeginSection(string name)
    {
        Section = name;
        Console.WriteLine();
        Console.WriteLine($"═══ {name} ═══");
    }

    public void Pass(string name, string detail = "") => Add(name, Status.Pass, detail);
    public void Warn(string name, string detail) => Add(name, Status.Warn, detail);
    public void Fail(string name, string detail) => Add(name, Status.Fail, detail);
    public void Skip(string name, string reason) => Add(name, Status.Skip, reason);

    private void Add(string name, Status state, string detail)
    {
        _cases.Add(new Case(Section, name, state, detail));
        var tag = state switch
        {
            Status.Pass => "PASS",
            Status.Warn => "WARN",
            Status.Fail => "FAIL",
            _ => "SKIP"
        };
        Console.WriteLine($"  [{tag}] {name}{(string.IsNullOrEmpty(detail) ? "" : $" — {detail}")}");
    }

    /// <summary>Runs an async check; any uncaught exception becomes a Fail.</summary>
    public async Task RunAsync(string name, Func<Task> body)
    {
        try
        {
            await body();
        }
        catch (Exception ex)
        {
            Fail(name, $"uncaught: {ex.GetType().Name}: {Trim(ex.Message)}");
        }
    }

    public int ExitCode => _cases.Count(c => c.State == Status.Fail) > 0 ? 1 : 0;

    public void PrintSummary()
    {
        var pass = _cases.Count(c => c.State == Status.Pass);
        var warn = _cases.Count(c => c.State == Status.Warn);
        var fail = _cases.Count(c => c.State == Status.Fail);
        var skip = _cases.Count(c => c.State == Status.Skip);
        Console.WriteLine();
        Console.WriteLine($"═══ SUMMARY: total={_cases.Count} pass={pass} warn={warn} fail={fail} skip={skip} ═══");
        foreach (var c in _cases.Where(c => c.State == Status.Fail))
            Console.WriteLine($"  FAIL {c.Section} / {c.Name}: {c.Detail}");
        foreach (var c in _cases.Where(c => c.State == Status.Warn))
            Console.WriteLine($"  WARN {c.Section} / {c.Name}: {c.Detail}");
    }

    private static string Trim(string s) => s.Length > 400 ? s[..400] + "..." : s;
}
