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
/// Explodes every multipart geometry into single-part features, carrying the
/// original attributes onto each part (unlike the DXF path which drops them).
/// </summary>
public class MultipartToSinglepartTool : ToolBase
{
    private const string FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg";

    public override string Id => "multipart-to-single";
    public override string Name => "Multipart to Singlepart";
    public override string NameZh => "多部件转单部件";
    public override string Description => "Split multipart geometries into single-part features";
    public override string DescriptionZh => "将多重几何要素拆分为单部件要素";
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
        }
    };

    protected override async Task<ToolResult> ExecuteCoreAsync(
        Dictionary<string, string> parameters, IProgress<string>? progress, CancellationToken ct)
    {
        var inputPath = GetRequired(parameters, "input");
        var outputPath = GetRequired(parameters, "output");

        var inputFormat = DetectFormat(inputPath);
        var layer = await Task.Run(() => OguLayerUtil.ReadLayer(inputFormat, inputPath), ct);

        progress?.Report(L($"Read {layer.GetFeatureCount()} features. Exploding parts...",
            $"已读取 {layer.GetFeatureCount()} 个要素，正在拆分部件..."));

        var singleType = ToSingleType(layer.GeometryType);
        var outputLayer = new OguLayer
        {
            Name = Path.GetFileNameWithoutExtension(outputPath),
            GeometryType = singleType,
            Wkid = layer.Wkid
        };
        foreach (var field in layer.Fields)
            outputLayer.AddField(field.Clone());

        int fid = 0;
        int partTotal = 0;
        foreach (var feature in layer.Features)
        {
            ct.ThrowIfCancellationRequested();
            var parts = GeometryOps.ExplodeParts(feature.Wkt);
            partTotal += parts.Count;
            foreach (var partWkt in parts)
            {
                var cloned = feature.Clone();
                cloned.Fid = fid++;
                cloned.Wkt = partWkt;
                outputLayer.AddFeature(cloned);
            }
        }

        var outputFormat = DetectFormat(outputPath);
        await Task.Run(() => WriteLayerSafe(outputFormat, outputLayer, outputPath, progress), ct);

        return new ToolResult
        {
            Success = true,
            Message = L(
                $"Exploded {layer.GetFeatureCount()} features into {outputLayer.GetFeatureCount()} single-part features.",
                $"已将 {layer.GetFeatureCount()} 个要素拆分为 {outputLayer.GetFeatureCount()} 个单部件要素。"),
            OutputPath = outputPath
        };
    }

    private static GeometryType ToSingleType(GeometryType t) => t switch
    {
        GeometryType.MULTIPOLYGON => GeometryType.POLYGON,
        GeometryType.MULTILINESTRING => GeometryType.LINESTRING,
        GeometryType.MULTIPOINT => GeometryType.POINT,
        _ => t
    };
}
