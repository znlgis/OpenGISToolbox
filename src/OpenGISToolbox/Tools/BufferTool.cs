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
/// Creates buffer zones around all features in a vector layer.
/// </summary>
public class BufferTool : ToolBase
{
    private const string FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg";

    public override string Id => "buffer";
    public override string Name => "Buffer";
    public override string NameZh => "缓冲区";
    public override string Description => "Create buffer zones around all features in a vector layer";
    public override string DescriptionZh => "为矢量图层中的所有要素创建缓冲区";
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
                Name = "distance",
                Label = "Buffer Distance",
                LabelZh = "缓冲距离",
                Description = L("Buffer distance", "缓冲距离"),
                Type = ParameterType.Number,
                Required = true,
                DefaultValue = "1.0"
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
        var distance = GetRequiredDouble(parameters, "distance");

        progress?.Report(L("Reading input file...", "读取输入文件..."));
        var inputFormat = DetectFormat(inputPath);
        var layer = await Task.Run(() => OguLayerUtil.ReadLayer(inputFormat, inputPath), ct);

        progress?.Report(L($"Read {layer.GetFeatureCount()} features. Creating buffers...",
            $"已读取 {layer.GetFeatureCount()} 个要素，正在创建缓冲区..."));

        var outputLayer = new OguLayer
        {
            Name = Path.GetFileNameWithoutExtension(outputPath),
            GeometryType = GeometryType.POLYGON,
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
                newFeature.Wkt = GeometryUtil.BufferWkt(feature.Wkt, distance);
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
            Message = L($"Buffer completed. {outputLayer.GetFeatureCount()} features created with distance {distance}.",
                $"缓冲区创建完成，共生成 {outputLayer.GetFeatureCount()} 个要素，缓冲距离为 {distance}。"),
            OutputPath = outputPath
        };
    }
}
