using System.Buffers.Binary;
using System.Globalization;

namespace OpenGISToolbox.TestKit;

/// <summary>
/// A real vector layer with independently-derived expectations.
/// The feature count and geometry type are parsed straight from the
/// .dbf/.shp/.geojson bytes; the catalog refuses to surface a layer whose
/// three sources disagree (or whose .shp record chain is inconsistent).
/// </summary>
public sealed class RealLayer
{
    public required string Name { get; init; }
    public required string ShpPath { get; init; }
    public string? GeoJsonPath { get; init; }
    public required ShpFile Shp { get; init; }
    public required DbfFile Dbf { get; init; }
    public GeoJsonFile? GeoJson { get; init; }

    public int ExpectedCount => Shp.Records; // == Dbf.Records (== GeoJson count when present); enforced in ctor path
    public ShpFile.Kind Kind => Shp.GeometryKind;
    public string DbfPath => Path.ChangeExtension(ShpPath, ".dbf");

    /// <summary>Bounding box (xmin, ymin, xmax, ymax) from the .shp header, no GDAL.</summary>
    public double[] Bbox { get; private init; } = new double[4];

    /// <summary>Validates cross-source agreement; throws InvalidDataException on disagreement.</summary>
    public static RealLayer Load(string shpPath, string? geoJsonPath)
    {
        var shp = ShpFile.Read(shpPath);
        var dbf = DbfFile.Read(Path.ChangeExtension(shpPath, ".dbf"));
        GeoJsonFile? gj = null;
        if (geoJsonPath != null && File.Exists(geoJsonPath)) gj = GeoJsonFile.Read(geoJsonPath);

        if (shp.Records != dbf.Records)
            throw new InvalidDataException($"{Path.GetFileName(shpPath)}: .shp chain has {shp.Records} records, .dbf claims {dbf.Records}");
        if (gj != null && gj.FeatureCount != shp.Records)
            throw new InvalidDataException($"{Path.GetFileName(shpPath)}: source GeoJSON has {gj.FeatureCount} features, converted .shp has {shp.Records}");

        double[] bbox = new double[4];
        var hb = File.ReadAllBytes(shpPath)[..100];
        for (var i = 0; i < 4; i++)
            bbox[i] = BinaryPrimitives.ReadDoubleLittleEndian(hb.AsSpan(36 + i * 8));

        return new RealLayer
        {
            Name = Path.GetFileNameWithoutExtension(shpPath),
            ShpPath = shpPath,
            GeoJsonPath = geoJsonPath,
            Shp = shp,
            Dbf = dbf,
            GeoJson = gj,
            Bbox = bbox
        };
    }

    /// <summary>
    /// Picks a (field, value) pair with a pure-ASCII value that occurs at least
    /// twice, so WHERE-clause expectations are encoding-independent. Low
    /// cardinality fields are preferred.
    /// </summary>
    public (string Field, string Value, int Matches)? PickAsciiWherePair()
    {
        for (var f = 0; f < Dbf.Fields.Count; f++)
        {
            var field = Dbf.Fields[f];
            if (field.Type != 'C') continue;
            var chosen = Dbf.RawRecords.Select(r => r[f].Trim())
                .GroupBy(v => v)
                .Where(g => g.Key.Length > 0 && IsAsciiSafe(g.Key) && g.Count() >= 2)
                .OrderBy(g => g.Count())
                .FirstOrDefault();
            if (chosen == null) continue;
            return (field.Name, chosen.Key, chosen.Count());
        }
        return null;
    }

    private static bool IsAsciiSafe(string s) => s.All(c => c >= 32 && c < 127);

    /// <summary>Picks a low-cardinality character field for split tests (ASCII values preferred).</summary>
    public (string Field, int DistinctValues, int NonEmpty)? PickSplitField()
    {
        for (var f = 0; f < Dbf.Fields.Count; f++)
        {
            var field = Dbf.Fields[f];
            if (field.Type != 'C') continue;
            var distinct = Dbf.RawRecords.Select(r => r[f].Trim()).Where(v => v.Length > 0).Distinct().Count();
            var nonEmpty = Dbf.RawRecords.Count(r => r[f].Trim().Length > 0);
            if (distinct >= 2 && nonEmpty == ExpectedCount && Dbf.RawRecords.All(r => IsAsciiSafe(r[f])))
                return (field.Name, distinct, nonEmpty);
        }
        return null;
    }

    public string BboxAsExtentWkt(double pad = 0.0)
    {
        var (x0, y0, x1, y1) = (Bbox[0] - pad, Bbox[1] - pad, Bbox[2] + pad, Bbox[3] + pad);
        return string.Format(CultureInfo.InvariantCulture,
            "POLYGON (({0} {1}, {2} {1}, {2} {3}, {0} {3}, {0} {1}))", x0, y0, x1, y1);
    }

    /// <summary>
    /// Writes a synthetic square polygon GeoJSON centred on this layer's bbox
    /// centre (uses the layer's own CRS units — degrees for geographic data).
    /// </summary>
    public string MakeSquareAroundCenterGeoJson(double halfDeg, string outPath)
    {
        var cx = (Bbox[0] + Bbox[2]) / 2.0;
        var cy = (Bbox[1] + Bbox[3]) / 2.0;
        string Fmt(double v) => v.ToString("R", CultureInfo.InvariantCulture);
        string Pt(double x, double y) => $"[{Fmt(x)}, {Fmt(y)}]";
        var ring = string.Join(", ", new[]
        {
            Pt(cx - halfDeg, cy - halfDeg), Pt(cx + halfDeg, cy - halfDeg),
            Pt(cx + halfDeg, cy + halfDeg), Pt(cx - halfDeg, cy + halfDeg),
            Pt(cx - halfDeg, cy - halfDeg)
        });
        var coords = $"[[{ring}]]";
        var json = """
            {"type":"FeatureCollection","crs":{"type":"name","properties":{"name":"EPSG:4326"}},
             "features":[{"type":"Feature","properties":{"name":"probe-square"},"geometry":{"type":"Polygon","coordinates":__COORDS__}}]}
            """.Replace("__COORDS__", coords);
        File.WriteAllText(outPath, json);
        return outPath;
    }
}

/// <summary>
/// Resolves the real-data set for harness/xunit runs.
///
/// Order of precedence:
///  1. <c>OGT_REAL_DATA_DIR</c> — user-supplied directory; every paired .shp/.dbf found
///     recursively becomes a layer (optionally cross-checked against a sibling .geojson).
///  2. Otherwise: Natural Earth 1:50m layers downloaded once into a local cache
///     (<c>OGT_REAL_DATA_CACHE</c> or %LOCALAPPDATA%), converted to .shp with the
///     application's own converter (dogfooding) and then treated as case 1 layers with
///     a GeoJSON sibling anchor.
///
/// Nothing downstream embeds dataset-specific names or expected values.
/// </summary>
public sealed class RealDataCatalog
{
    public required IReadOnlyList<RealLayer> Layers { get; init; }
    public required string SourceDescription { get; init; }
    public required string WorkingDir { get; init; }

    public RealLayer? Polygon => Layers.FirstOrDefault(l => l.Kind == ShpFile.Kind.Polygon);
    public RealLayer? Line => Layers.FirstOrDefault(l => l.Kind == ShpFile.Kind.Line);
    public RealLayer? Point => Layers.FirstOrDefault(l => l.Kind == ShpFile.Kind.Point);

    public IReadOnlyList<RealLayer> OfKind(ShpFile.Kind k) => Layers.Where(l => l.Kind == k).ToList();

    public static RealDataCatalog? Resolve(IProgress<string>? log = null)
    {
        var overrideDir = Environment.GetEnvironmentVariable("OGT_REAL_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(overrideDir))
        {
            if (!Directory.Exists(overrideDir))
                throw new DirectoryNotFoundException($"OGT_REAL_DATA_DIR does not exist: {overrideDir}");
            var layers = DiscoverLayers(overrideDir, log);
            if (layers.Count == 0)
                throw new InvalidDataException($"No .shp/.dbf pairs found under {overrideDir}");
            return new RealDataCatalog
            {
                Layers = layers,
                SourceDescription = $"OGT_REAL_DATA_DIR = {overrideDir}",
                WorkingDir = overrideDir
            };
        }

        return EnsureNaturalEarth(log);
    }

    public static List<RealLayer> DiscoverLayers(string root, IProgress<string>? log = null)
    {
        var result = new List<RealLayer>();
        foreach (var shp in Directory.EnumerateFiles(root, "*.shp", SearchOption.AllDirectories).OrderBy(p => p))
        {
            var dbf = Path.ChangeExtension(shp, ".dbf");
            if (!File.Exists(dbf)) continue; // dbf-less .shp: not usable for attribute expectations
            var gj = Path.ChangeExtension(shp, ".geojson");
            if (!File.Exists(gj)) gj = Path.ChangeExtension(shp, ".json");
            RealLayer layer;
            try
            {
                layer = RealLayer.Load(shp, File.Exists(gj) ? gj : null);
            }
            catch (Exception ex)
            {
                log?.Report($"{Path.GetFileName(shp)} skipped: {ex.Message}");
                continue;
            }
            if (layer.Kind is ShpFile.Kind.Point or ShpFile.Kind.Line or ShpFile.Kind.Polygon)
                result.Add(layer);
        }
        return result;
    }

    private static readonly (string FileName, string Url)[] NaturalEarthFiles =
    {
        ("ne_50m_admin_0_countries", "https://raw.githubusercontent.com/nvkelso/natural-earth-vector/master/geojson/ne_50m_admin_0_countries.geojson"),
        ("ne_50m_rivers_lake_centerlines", "https://raw.githubusercontent.com/nvkelso/natural-earth-vector/master/geojson/ne_50m_rivers_lake_centerlines.geojson"),
        ("ne_50m_populated_places", "https://raw.githubusercontent.com/nvkelso/natural-earth-vector/master/geojson/ne_50m_populated_places.geojson"),
    };

    public static string CacheDir =>
        Environment.GetEnvironmentVariable("OGT_REAL_DATA_CACHE") is { Length: > 0 } cd
            ? cd
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenGISToolboxTests", "natural-earth-50m");

    /// <summary>Downloads + converts the Natural Earth anchor set; null when network is unavailable.</summary>
    private static RealDataCatalog? EnsureNaturalEarth(IProgress<string>? log)
    {
        var dir = CacheDir;
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var (name, url) in NaturalEarthFiles)
            {
                var geojson = Path.Combine(dir, name + ".geojson");
                if (!File.Exists(geojson) || new FileInfo(geojson).Length == 0)
                {
                    log?.Report($"downloading {name} ...");
                    DownloadAsync(url, geojson).GetAwaiter().GetResult();
                }
                var shp = Path.Combine(dir, name + ".shp");
                if (!File.Exists(shp) || !File.Exists(Path.ChangeExtension(shp, ".dbf")))
                {
                    log?.Report($"converting {name}.geojson -> shp with the app's own converter ...");
                    var tool = new OpenGISToolbox.Tools.FormatConversionTool(
                        "ne-geojson-to-shp", "GeoJSON → SHP", "GeoJSON → SHP",
                        "", "",
                        OpenGIS.Utils.Engine.Enums.DataFormatType.GEOJSON, ".geojson", "GeoJSON|*.geojson",
                        OpenGIS.Utils.Engine.Enums.DataFormatType.SHP, ".shp", "Shapefile|*.shp");
                    var result = GdalEnv.RunToolAsync(tool, new Dictionary<string, string>
                    {
                        ["input"] = geojson, ["output"] = shp
                    }).GetAwaiter().GetResult();
                    if (!result.Success)
                        throw new InvalidDataException($"converter failed on {name}: {result.Message}");
                }
            }
        }
        catch (HttpRequestException ex)
        {
            log?.Report($"Natural Earth download unavailable: {ex.Message}");
            return null;
        }
        catch (Exception ex) when (ex is TaskCanceledException or IOException)
        {
            log?.Report($"Natural Earth download unavailable: {ex.Message}");
            return null;
        }

        var layers = DiscoverLayers(dir, log);
        if (layers.Count == 0) return null;
        return new RealDataCatalog { Layers = layers, SourceDescription = $"Natural Earth 1:50m cache @ {dir}", WorkingDir = dir };
    }

    private static async Task DownloadAsync(string url, string target)
    {
        using var http = new HttpClient();
        http.Timeout = TimeSpan.FromMinutes(5);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("OpenGISToolbox-Tests/1.0");
        var bytes = await http.GetByteArrayAsync(url);
        var part = target + ".part";
        await File.WriteAllBytesAsync(part, bytes);
        File.Move(part, target, overwrite: true);
    }
}
