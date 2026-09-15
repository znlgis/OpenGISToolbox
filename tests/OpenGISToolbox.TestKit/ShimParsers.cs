using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace OpenGISToolbox.TestKit;

/// <summary>
/// Binary readers for ESRI Shape / dBASE files and GeoJSON.
/// These parse file structures directly (no GDAL involvement) so that test
/// expectations are derived independently from the engine under test and can be
/// cross-checked against GDAL results.
/// </summary>
public sealed class ShpFile
{
    public int Records { get; private init; }
    public int ShapeTypeCode { get; private init; }
    public long FileLengthBytes { get; private init; }
    public string ShapeTypeName { get; private init; } = "Unknown";

    public enum Kind { Point, Line, Polygon, Null, Unknown }

    public Kind GeometryKind { get; private init; }

    /// <summary>
    /// Reads the 100-byte header and walks the entire record chain, verifying
    /// that record headers tile the file exactly. Any truncation or garbage
    /// throws InvalidDataException.
    /// </summary>
    public static ShpFile Read(string shpPath)
    {
        using var fs = File.OpenRead(shpPath);
        var header = new byte[100];
        fs.ReadExactly(header);

        var fileCode = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(0));
        if (fileCode != 9994)
            throw new InvalidDataException($"Not a shapefile (file code {fileCode})");

        var fileLengthWords = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(24));
        var shapeType = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(32));

        // Walk the record chain: 8-byte record header (record number + content length,
        // both big-endian 32-bit words of 16-bit units), then 2*contentLength bytes.
        long expectedEnd = fileLengthWords * 2L;
        long pos = 100;
        int records = 0;
        var buf = new byte[8];
        while (pos < expectedEnd)
        {
            fs.Position = pos;
            fs.ReadExactly(buf);
            var recNo = BinaryPrimitives.ReadInt32BigEndian(buf.AsSpan(0));
            var contentWords = BinaryPrimitives.ReadInt32BigEndian(buf.AsSpan(4));
            if (contentWords < 0 || pos + 8 + contentWords * 2L > expectedEnd)
                throw new InvalidDataException($"Record {records} (#{recNo}) overruns file length");
            pos += 8 + contentWords * 2L;
            records++;
        }

        var kind = shapeType switch
        {
            0 => Kind.Null,
            1 or 11 or 21 or 3001 or 3101 or 3201 or 3301 => Kind.Point,
            3 or 13 or 23 or 3003 or 3103 or 3203 or 3303 => Kind.Line,
            5 or 15 or 25 or 3005 or 3105 or 3205 or 3305 => Kind.Polygon,
            _ => Kind.Unknown
        };

        return new ShpFile
        {
            Records = records,
            ShapeTypeCode = shapeType,
            FileLengthBytes = expectedEnd,
            ShapeTypeName = kind.ToString(),
            GeometryKind = kind
        };
    }
}

/// <summary>
/// Minimal .dbf reader: header, field descriptors and a raw byte view of every
/// record, decoded per the file's declared language driver (defaulting to
/// Windows-1252, with .cpg sidecar override support).
/// </summary>
public sealed class DbfFile
{
    public sealed record FieldInfo(string Name, char Type, int Length);

    public int Records { get; private init; }
    public List<FieldInfo> Fields { get; private init; } = new();
    public List<string[]> RawRecords { get; private init; } = new();

    /// <summary>Record values decoded as strings with padding trimmed.</summary>
    public static DbfFile Read(string dbfPath)
    {
        var bytes = File.ReadAllBytes(dbfPath);
        if (bytes.Length < 32) throw new InvalidDataException("DBF too small");
        if (bytes[0] != 0x03) throw new InvalidDataException($"Not a dBASE III file (version 0x{bytes[0]:X2})");

        var recordCount = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4));
        var headerLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8));
        var recordLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(10));
        var ldi = bytes[29];

        var encoding = ResolveEncoding(dbfPath, ldi);

        var fields = new List<FieldInfo>();
        for (var p = 32; p + 32 <= headerLength - 1; p += 32)
        {
            if (bytes[p] == 0x0D) break;
            var name = Encoding.ASCII.GetString(bytes, p, 11).TrimEnd('\0').Trim();
            var type = (char)bytes[p + 11];
            var len = bytes[p + 16];
            fields.Add(new FieldInfo(name, type, len));
        }

        var records = new List<string[]>();
        var dataStart = headerLength;
        for (var i = 0; i < recordCount; i++)
        {
            var start = dataStart + i * recordLength;
            if (start + recordLength > bytes.Length)
                throw new InvalidDataException($"DBF record {i} beyond end of file");
            if (bytes[start] == 0x2A) continue; // deleted record flag '*'
            var values = new string[fields.Count];
            var off = start + 1; // skip deletion flag
            for (var f = 0; f < fields.Count; f++)
            {
                values[f] = encoding.GetString(bytes, off, fields[f].Length).Trim();
                off += fields[f].Length;
            }
            records.Add(values);
        }

        return new DbfFile
        {
            Records = recordCount,
            Fields = fields,
            RawRecords = records
        };
    }

    private static Encoding ResolveEncoding(string dbfPath, byte ldi)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cpg = Path.ChangeExtension(dbfPath, ".cpg");
        if (File.Exists(cpg))
        {
            var txt = File.ReadAllText(cpg).Trim();
            try
            {
                if (txt.StartsWith("UTF-8", StringComparison.OrdinalIgnoreCase)) return new UTF8Encoding(false);
                return Encoding.GetEncoding(txt);
            }
            catch (ArgumentException) { /* fall through */ }
        }
        // Language driver id: 0x03 = ANSI Windows-1252, 0x4D = UTF-8 (some tools), else Latin-1 guess.
        return ldi switch
        {
            0x03 => Encoding.GetEncoding(1252),
            0x4D or 0x5D => new UTF8Encoding(false),
            0xC8 => Encoding.GetEncoding("GBK"),
            _ => Encoding.Latin1
        };
    }
}

/// <summary>GeoJSON summary parsed with System.Text.Json — no GDAL involved.</summary>
public sealed class GeoJsonFile
{
    public int FeatureCount { get; private init; }
    public string FirstGeometryType { get; private init; } = "";
    public HashSet<string> GeometryTypes { get; private init; } = new();

    public static GeoJsonFile Read(string path)
    {
        using var doc = JsonDocument.Parse(File.OpenRead(path),
            new JsonDocumentOptions { AllowTrailingCommas = true });
        var types = new HashSet<string>();
        var count = 0;
        string first = "";
        if (doc.RootElement.TryGetProperty("features", out var features) && features.ValueKind == JsonValueKind.Array)
        {
            foreach (var f in features.EnumerateArray())
            {
                count++;
                var t = "";
                if (f.TryGetProperty("geometry", out var g) && g.ValueKind == JsonValueKind.Object &&
                    g.TryGetProperty("type", out var gt) && gt.ValueKind == JsonValueKind.String)
                    t = gt.GetString()!;
                if (count == 1) first = t;
                if (!string.IsNullOrEmpty(t)) types.Add(t);
            }
        }
        else if (doc.RootElement.TryGetProperty("type", out var top) && top.ValueKind == JsonValueKind.String)
        {
            // bare geometry
            count = 1;
            first = top.GetString()!;
            types.Add(first);
        }
        return new GeoJsonFile { FeatureCount = count, FirstGeometryType = first, GeometryTypes = types };
    }
}
