using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenGIS.Utils.DataSource;
using OpenGIS.Utils.Engine.Enums;
using OpenGIS.Utils.Engine.Model.Layer;
using OpenGIS.Utils.Geometry;
using OpenGISToolbox.Models;

namespace OpenGISToolbox.Tools;

/// <summary>
/// Dissolves (merges) features that share the same value of an attribute field,
/// unioning their geometries into one feature per group. This is the high-frequency
/// "Dissolve" geoprocessing tool absent from the original set (Union only merges two
/// whole layers into a single feature, not grouped by attribute).
/// </summary>
public class DissolveTool : ToolBase
{
    private const string FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg";

    public override string Id => "dissolve";
    public override string Name => "Dissolve";
    public override string NameZh => "按属性融合";
    public override string Description => "Merge features that share the same attribute value into dissolved polygons";
    public override string DescriptionZh => "将具有相同属性值的要素融合合并为整体";
    public override ToolCategory Category => ToolCategory.Geometry;

    public override List<ToolParameter> BuildParameters()
    {
        return new List<ToolParameter>
        {
            new ToolParameter
            {
                Name = "input",
                Label = "Input File",
                LabelZh = "输入文件",
                Description = L("Input vector file", "输入矢量文件"),
                Type = ParameterType.InputFile,
                Required = true,
                FileFilter = FileFilter
            },
            new ToolParameter
            {
                Name = "output",
                Label = "Output File",
                LabelZh = "输出文件",
                Description = L("Output vector file", "输出矢量文件"),
                Type = ParameterType.OutputFile,
                Required = true,
                FileFilter = FileFilter
            },
            new ToolParameter
            {
                Name = "field",
                Label = "Dissolve Field",
                LabelZh = "融合字段",
                Description = L("Attribute field whose shared value defines each dissolve group",
                    "按该属性字段的相同值分组进行融合"),
                Type = ParameterType.Text,
                Required = true
            }
        };
    }

    protected override async Task<ToolResult> ExecuteCoreAsync(
        Dictionary<string, string> parameters,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var inputPath = GetRequired(parameters, "input");
        var outputPath = GetRequired(parameters, "output");
        var fieldName = GetRequired(parameters, "field");

        progress?.Report(L("Reading input file...", "读取输入文件..."));
        var inputFormat = DetectFormat(inputPath);
        var layer = await Task.Run(() => OguLayerUtil.ReadLayer(inputFormat, inputPath), ct);

        if (layer.GetField(fieldName) == null)
            throw new ArgumentException(L(
                $"Dissolve field '{fieldName}' not found in the input layer.",
                $"输入图层中未找到融合字段 '{fieldName}'。"));

        progress?.Report(L($"Read {layer.GetFeatureCount()} features. Grouping by '{fieldName}'...",
            $"已读取 {layer.GetFeatureCount()} 个要素，正在按 '{fieldName}' 分组..."));

        var geometryType = DissolvedGeometryType(layer.GeometryType);

        var outputLayer = new OguLayer
        {
            Name = Path.GetFileNameWithoutExtension(outputPath),
            GeometryType = geometryType,
            Wkid = layer.Wkid
        };
        foreach (var field in layer.Fields)
            outputLayer.AddField(field.Clone());

        var groups = layer.Features
            .Where(f => !string.IsNullOrWhiteSpace(f.Wkt))
            .GroupBy(f => f.GetValue(fieldName)?.ToString() ?? string.Empty)
            .ToList();

        int fid = 0;
        int dissolvedFeatures = 0;
        foreach (var group in groups)
        {
            ct.ThrowIfCancellationRequested();

            var wkts = group.Select(f => f.Wkt!).ToList();
            string mergedWkt;
            if (wkts.Count == 1)
            {
                mergedWkt = wkts[0];
            }
            else
            {
                mergedWkt = GeometryUtil.UnionWkt(wkts);
            }
            if (string.IsNullOrWhiteSpace(mergedWkt)) continue;

            // Carry the first feature's attributes so the group key survives the merge.
            var outFeature = group.First().Clone();
            outFeature.Fid = fid++;
            outFeature.Wkt = mergedWkt;
            outputLayer.AddFeature(outFeature);
            dissolvedFeatures += wkts.Count;
        }

        progress?.Report(L($"Writing {outputLayer.GetFeatureCount()} dissolved features...",
            $"正在写入 {outputLayer.GetFeatureCount()} 个融合要素..."));
        var outputFormat = DetectFormat(outputPath);
        await Task.Run(() => WriteLayerSafe(outputFormat, outputLayer, outputPath, progress), ct);

        return new ToolResult
        {
            Success = true,
            Message = L(
                $"Dissolve completed. {dissolvedFeatures} features merged into {outputLayer.GetFeatureCount()} by '{fieldName}'.",
                $"融合完成，{dissolvedFeatures} 个要素按 '{fieldName}' 合并为 {outputLayer.GetFeatureCount()} 个。"),
            OutputPath = outputPath
        };
    }

    private static GeometryType DissolvedGeometryType(GeometryType input) => input switch
    {
        GeometryType.POLYGON or GeometryType.MULTIPOLYGON => GeometryType.MULTIPOLYGON,
        GeometryType.LINESTRING or GeometryType.MULTILINESTRING => GeometryType.MULTILINESTRING,
        GeometryType.POINT or GeometryType.MULTIPOINT => GeometryType.MULTIPOINT,
        _ => GeometryType.GEOMETRYCOLLECTION
    };
}
