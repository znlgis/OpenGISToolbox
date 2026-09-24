using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenGIS.Utils.DataSource;
using OpenGIS.Utils.Engine.Enums;
using OpenGIS.Utils.Engine.Model.Layer;
using OpenGISToolbox.Models;

namespace OpenGISToolbox.Tools;

/// <summary>
/// Converts line features to polygons by treating each (closed) line ring as a
/// polygon boundary. Open rings are closed automatically; rings with fewer than
/// three distinct vertices are skipped.
/// </summary>
public class LineToPolygonTool : ToolBase
{
    private const string FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg";

    public override string Id => "line-to-polygon";
    public override string Name => "Line to Polygon";
    public override string NameZh => "线转面";
    public override string Description => "Convert line features to polygons by closing their rings";
    public override string DescriptionZh => "通过将线要素闭合为环来转换为面要素";
    public override ToolCategory Category => ToolCategory.Geometry;

    public override List<ToolParameter> BuildParameters() => new()
    {
        new ToolParameter
        {
            Name = "input", Label = "Input File", LabelZh = "输入文件",
            Description = L("Input line vector file", "输入线矢量文件"),
            Type = ParameterType.InputFile, Required = true, FileFilter = FileFilter
        },
        new ToolParameter
        {
            Name = "output", Label = "Output File", LabelZh = "输出文件",
            Description = L("Output polygon vector file", "输出面矢量文件"),
            Type = ParameterType.OutputFile, Required = true, FileFilter = FileFilter
        }
    };

    protected override async Task<ToolResult> ExecuteCoreAsync(
        Dictionary<string, string> parameters, IProgress<string>? progress, CancellationToken ct)
    {
        var inputPath = GetRequired(parameters, "input");
        var outputPath = GetRequired(parameters, "output");

        var inputFormat = DetectFormat(inputPath);
        var layer = await Task.Run(() => OguLayerUtil.ReadLayer(inputFormat, inputPath), ct);

        var outputLayer = new OguLayer
        {
            Name = Path.GetFileNameWithoutExtension(outputPath),
            GeometryType = GeometryType.MULTIPOLYGON,
            Wkid = layer.Wkid
        };
        foreach (var field in layer.Fields)
            outputLayer.AddField(field.Clone());

        int fid = 0;
        int converted = 0;
        int skipped = 0;
        foreach (var feature in layer.Features)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(feature.Wkt)) continue;

            var rings = new List<string>();
            foreach (var line in GeometryOps.ExplodeParts(feature.Wkt))
            {
                var ring = BuildRing(line);
                if (ring != null) rings.Add(ring);
            }
            if (rings.Count == 0)
            {
                skipped++;
                continue;
            }

            var wkt = rings.Count == 1 ? rings[0] : GeometryOps.CollectToMulti(rings);
            if (wkt == null) { skipped++; continue; }

            var outFeature = feature.Clone();
            outFeature.Fid = fid++;
            outFeature.Wkt = wkt;
            outputLayer.AddFeature(outFeature);
            converted++;
        }

        var outputFormat = DetectFormat(outputPath);
        await Task.Run(() => WriteLayerSafe(outputFormat, outputLayer, outputPath, progress), ct);

        return new ToolResult
        {
            Success = true,
            Message = L(
                $"Converted {converted} line features to polygons ({skipped} skipped as degenerate).",
                $"已将 {converted} 个线要素转换为面（{skipped} 个因退化被跳过）。"),
            OutputPath = outputPath
        };
    }

    /// <summary>Builds a single-ring POLYGON WKT from a LINESTRING, closing it if needed.</summary>
    private static string? BuildRing(string lineWkt)
    {
        if (!GeometryOps.BaseType(lineWkt).Contains("LINESTRING", StringComparison.Ordinal))
            return null;
        var coords = GeometryOps.ExtractCoordinates(lineWkt);
        if (coords.Count < 3) return null;

        // Ensure the ring is closed.
        var first = coords[0];
        var last = coords[^1];
        if (Math.Abs(first.X - last.X) > 1e-12 || Math.Abs(first.Y - last.Y) > 1e-12)
            coords.Add(first);
        if (coords.Count < 4) return null;

        string Fmt(double v) => v.ToString("R", CultureInfo.InvariantCulture);
        var pts = string.Join(",", coords.Select(c => $"{Fmt(c.X)} {Fmt(c.Y)}"));
        return $"POLYGON (({pts}))";
    }
}
