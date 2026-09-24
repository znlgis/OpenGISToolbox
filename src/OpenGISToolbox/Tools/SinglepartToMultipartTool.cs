using System;
using System.Collections.Generic;
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
/// Collects single-part geometries into multipart features, optionally grouped by
/// an attribute field (when the field is omitted, everything is collected into one
/// feature). Parts are gathered as-is — not unioned — preserving each component.
/// </summary>
public class SinglepartToMultipartTool : ToolBase
{
    private const string FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg";

    public override string Id => "single-to-multipart";
    public override string Name => "Singlepart to Multipart";
    public override string NameZh => "单部件转多部件";
    public override string Description => "Collect single-part geometries into multipart features (optionally grouped by field)";
    public override string DescriptionZh => "将单部件几何收集为多重几何要素（可选按字段分组）";
    public override ToolCategory Category => ToolCategory.Geometry;

    public override List<ToolParameter> BuildParameters() => new()
    {
        new ToolParameter
        {
            Name = "input", Label = "Input File", LabelZh = "输入文件",
            Description = L("Input vector file", "输入矢量文件"),
            Type = ParameterType.InputFile, Required = true, FileFilter = FileFilter
        },
        new ToolParameter
        {
            Name = "output", Label = "Output File", LabelZh = "输出文件",
            Description = L("Output vector file", "输出矢量文件"),
            Type = ParameterType.OutputFile, Required = true, FileFilter = FileFilter
        },
        new ToolParameter
        {
            Name = "field", Label = "Group Field (optional)", LabelZh = "分组字段（可选）",
            Description = L("Attribute field to group by; leave empty to collect all into one feature",
                "按该属性字段分组；留空则将所有要素收集为一个"),
            Type = ParameterType.Text, Required = false, DefaultValue = ""
        }
    };

    protected override async Task<ToolResult> ExecuteCoreAsync(
        Dictionary<string, string> parameters, IProgress<string>? progress, CancellationToken ct)
    {
        var inputPath = GetRequired(parameters, "input");
        var outputPath = GetRequired(parameters, "output");
        var fieldName = GetOptional(parameters, "field", "");

        var inputFormat = DetectFormat(inputPath);
        var layer = await Task.Run(() => OguLayerUtil.ReadLayer(inputFormat, inputPath), ct);

        if (!string.IsNullOrWhiteSpace(fieldName) && layer.GetField(fieldName) == null)
            throw new ArgumentException(L(
                $"Group field '{fieldName}' not found in the input layer.",
                $"输入图层中未找到分组字段 '{fieldName}'。"));

        progress?.Report(L($"Read {layer.GetFeatureCount()} features. Collecting parts...",
            $"已读取 {layer.GetFeatureCount()} 个要素，正在收集部件..."));

        var outputLayer = new OguLayer
        {
            Name = Path.GetFileNameWithoutExtension(outputPath),
            GeometryType = ToMultiType(layer.GeometryType),
            Wkid = layer.Wkid
        };
        foreach (var field in layer.Fields)
            outputLayer.AddField(field.Clone());

        var withGeom = layer.Features.Where(f => !string.IsNullOrWhiteSpace(f.Wkt)).ToList();
        List<IGrouping<string, OguFeature>> groups;
        if (withGeom.Count == 0)
        {
            groups = new List<IGrouping<string, OguFeature>>();
        }
        else if (string.IsNullOrWhiteSpace(fieldName))
        {
            groups = new List<IGrouping<string, OguFeature>> { withGeom.GroupBy(_ => "").First() };
        }
        else
        {
            groups = withGeom.GroupBy(f => f.GetValue(fieldName)?.ToString() ?? string.Empty).ToList();
        }

        int fid = 0;
        foreach (var group in groups)
        {
            ct.ThrowIfCancellationRequested();

            // Explode everything to single parts first so mixed inputs normalise.
            var singleParts = group.SelectMany(f => GeometryOps.ExplodeParts(f.Wkt)).ToList();
            var merged = GeometryOps.CollectToMulti(singleParts);
            if (merged == null)
                throw new ArgumentException(L(
                    $"Group '{group.Key}' mixes incompatible geometry types and cannot be collected.",
                    $"分组 '{group.Key}' 混合了不兼容的几何类型，无法收集。"));

            var outFeature = group.First().Clone();
            outFeature.Fid = fid++;
            outFeature.Wkt = merged;
            outputLayer.AddFeature(outFeature);
        }

        var outputFormat = DetectFormat(outputPath);
        await Task.Run(() => WriteLayerSafe(outputFormat, outputLayer, outputPath, progress), ct);

        return new ToolResult
        {
            Success = true,
            Message = L(
                $"Collected {withGeom.Count} features into {outputLayer.GetFeatureCount()} multipart feature(s).",
                $"已将 {withGeom.Count} 个要素收集为 {outputLayer.GetFeatureCount()} 个多重几何要素。"),
            OutputPath = outputPath
        };
    }

    private static GeometryType ToMultiType(GeometryType t) => t switch
    {
        GeometryType.POLYGON => GeometryType.MULTIPOLYGON,
        GeometryType.LINESTRING => GeometryType.MULTILINESTRING,
        GeometryType.POINT => GeometryType.MULTIPOINT,
        _ => t
    };
}
