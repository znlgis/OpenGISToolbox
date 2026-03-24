using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OpenGIS.Utils.DataSource;
using OpenGIS.Utils.Engine.Enums;
using OpenGIS.Utils.Engine.Model.Layer;
using OpenGIS.Utils.Geometry;
using OpenGISToolbox.Models;

namespace OpenGISToolbox.Tools;

/// <summary>
/// Computes the centroid of each feature in a vector layer.
/// </summary>
public class CentroidTool : ToolBase
{
    private const string FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg";

    public override string Id => "centroid";
    public override string Name => "Centroid";
    public override string NameZh => "质心";
    public override string Description => "Compute the centroid of each feature in a vector layer";
    public override string DescriptionZh => "计算矢量图层中每个要素的质心";
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

        progress?.Report(L("Reading input file...", "读取输入文件..."));
        var inputFormat = DetectFormat(inputPath);
        var layer = await Task.Run(() => OguLayerUtil.ReadLayer(inputFormat, inputPath), ct);

        progress?.Report(L($"Read {layer.GetFeatureCount()} features. Computing centroids...",
            $"已读取 {layer.GetFeatureCount()} 个要素，正在计算质心..."));

        var outputLayer = new OguLayer
        {
            Name = Path.GetFileNameWithoutExtension(outputPath),
            GeometryType = GeometryType.POINT,
            Wkid = layer.Wkid
        };

        foreach (var field in layer.Fields)
        {
            outputLayer.AddField(field.Clone());
        }

        int fid = 0;
        foreach (var feature in layer.Features)
        {
            ct.ThrowIfCancellationRequested();

            var newFeature = feature.Clone();
            newFeature.Fid = fid++;
            if (!string.IsNullOrEmpty(feature.Wkt))
            {
                newFeature.Wkt = GeometryUtil.CentroidWkt(feature.Wkt);
            }
            outputLayer.AddFeature(newFeature);
        }

        progress?.Report(L($"Writing {outputLayer.GetFeatureCount()} features...",
            $"正在写入 {outputLayer.GetFeatureCount()} 个要素..."));
        var outputFormat = DetectFormat(outputPath);
        await Task.Run(() => OguLayerUtil.WriteLayer(outputFormat, outputLayer, outputPath), ct);

        return new ToolResult
        {
            Success = true,
            Message = L($"Centroid completed. {outputLayer.GetFeatureCount()} centroids created.",
                $"质心计算完成，共生成 {outputLayer.GetFeatureCount()} 个质心。"),
            OutputPath = outputPath
        };
    }
}
