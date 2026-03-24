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
/// Simplifies geometries in a vector layer using the Douglas-Peucker algorithm.
/// </summary>
public class SimplifyTool : ToolBase
{
    private const string FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg";

    public override string Id => "simplify";
    public override string Name => "Simplify";
    public override string NameZh => "简化";
    public override string Description => "Simplify geometries in a vector layer using the Douglas-Peucker algorithm";
    public override string DescriptionZh => "使用 Douglas-Peucker 算法简化矢量图层中的几何体";
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
                Name = "tolerance",
                Label = "Tolerance",
                LabelZh = "容差",
                Description = L("Simplification tolerance (Douglas-Peucker)", "简化容差（Douglas-Peucker）"),
                Type = ParameterType.Number,
                Required = true,
                DefaultValue = "0.001"
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
        var tolerance = GetRequiredDouble(parameters, "tolerance");

        progress?.Report(L("Reading input file...", "读取输入文件..."));
        var inputFormat = DetectFormat(inputPath);
        var layer = await Task.Run(() => OguLayerUtil.ReadLayer(inputFormat, inputPath), ct);

        progress?.Report(L($"Read {layer.GetFeatureCount()} features. Simplifying geometries...",
            $"已读取 {layer.GetFeatureCount()} 个要素，正在简化几何体..."));

        var outputLayer = new OguLayer
        {
            Name = Path.GetFileNameWithoutExtension(outputPath),
            GeometryType = layer.GeometryType,
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
                newFeature.Wkt = GeometryUtil.SimplifyWkt(feature.Wkt, tolerance);
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
            Message = L($"Simplification completed. {outputLayer.GetFeatureCount()} features simplified with tolerance {tolerance}.",
                $"简化完成，共简化 {outputLayer.GetFeatureCount()} 个要素，容差为 {tolerance}。"),
            OutputPath = outputPath
        };
    }
}
