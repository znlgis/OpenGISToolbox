using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using OpenGIS.Utils.Geometry;

namespace OpenGISToolbox.Tools;

/// <summary>
/// Low-level, WKT-in/WKT-out geometry helpers shared by the Sprint-1 geometry
/// tools. Deliberately free of any layer/format I/O so the tools stay focused
/// on parameter handling, progress and result reporting.
/// </summary>
public static class GeometryOps
{
    /// <summary>
    /// Splits a (possibly MULTI) WKT geometry into its single-part WKTs.
    /// Non-multipart geometries are returned as a one-element list. The
    /// part-walking mirrors the proven pattern in FormatConversionTool.
    /// </summary>
    public static List<string> ExplodeParts(string? wkt)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(wkt)) return result;

        var trimmed = wkt.Trim();
        if (!IsMultipart(trimmed))
        {
            result.Add(trimmed);
            return result;
        }

        var geometry = GeometryUtil.Wkt2Geometry(wkt);
        if (geometry == null)
        {
            result.Add(trimmed);
            return result;
        }
        try
        {
            var count = geometry.GetGeometryCount();
            if (count <= 0)
            {
                result.Add(trimmed);
                return result;
            }
            for (var i = 0; i < count; i++)
            {
                var part = geometry.GetGeometryRef(i);
                if (part == null) continue;
                var partWkt = GeometryUtil.Geometry2Wkt(part);
                if (!string.IsNullOrWhiteSpace(partWkt))
                    result.Add(partWkt);
            }
        }
        finally
        {
            geometry.Dispose();
        }
        if (result.Count == 0) result.Add(trimmed);
        return result;
    }

    /// <summary>True when the WKT starts with a MULTI* container keyword.</summary>
    public static bool IsMultipart(string wkt)
    {
        var u = wkt.TrimStart().ToUpperInvariant();
        return u.StartsWith("MULTIPOLYGON", StringComparison.Ordinal)
            || u.StartsWith("MULTILINESTRING", StringComparison.Ordinal)
            || u.StartsWith("MULTIPOINT", StringComparison.Ordinal);
    }

    /// <summary>
    /// Collects a set of single-part geometries sharing one base type into a
    /// single MULTI* WKT via textual assembly (no mutable-OGR construction).
    /// Returns null when parts disagree on base type. A single part is still
    /// wrapped, so output is always multipart — the point of the operation.
    /// </summary>
    public static string? CollectToMulti(IEnumerable<string> singlePartWkts)
    {
        var parts = singlePartWkts.Where(w => !string.IsNullOrWhiteSpace(w)).ToList();
        if (parts.Count == 0) return null;

        string? baseType = null;
        var contents = new List<string>();
        foreach (var p in parts)
        {
            var open = p.IndexOf('(');
            if (open < 0) return null;
            var type = p[..open].Trim().ToUpperInvariant();
            // Normalise: strip a MULTI prefix so single/mixed inputs collapse.
            if (type.StartsWith("MULTI", StringComparison.Ordinal))
                type = type[5..];
            baseType ??= type;
            if (baseType != type) return null; // heterogeneous group

            var content = p[open..].Trim(); // from first '(' to matching ')'
            contents.Add(content);
        }

        var multiType = "MULTI" + baseType;
        var sb = new StringBuilder();
        sb.Append(multiType).Append(" (");
        sb.Append(string.Join(",", contents));
        sb.Append(')');
        return sb.ToString();
    }

    /// <summary>Extracts every X/Y coordinate pair from a WKT, in file order.</summary>
    public static List<(double X, double Y)> ExtractCoordinates(string? wkt)
    {
        var result = new List<(double, double)>();
        if (string.IsNullOrWhiteSpace(wkt)) return result;
        var nums = new List<double>();
        var sb = new StringBuilder();
        foreach (var ch in wkt)
        {
            if (char.IsDigit(ch) || ch is '-' or '+' or '.' or 'e' or 'E')
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
            if (sb.Length > 0 &&
                double.TryParse(sb.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                nums.Add(v);
        }
    }

    /// <summary>Base geometry keyword of a WKT (e.g. "POLYGON", "POINT").</summary>
    public static string BaseType(string? wkt)
    {
        if (string.IsNullOrWhiteSpace(wkt)) return string.Empty;
        var open = wkt.IndexOf('(');
        return (open < 0 ? wkt : wkt[..open]).Trim().ToUpperInvariant();
    }
}
