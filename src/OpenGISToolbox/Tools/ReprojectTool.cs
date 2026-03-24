using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenGIS.Utils.DataSource;
using OpenGIS.Utils.Engine.Util;
using OpenGISToolbox.Models;

namespace OpenGISToolbox.Tools;

/// <summary>
/// Transforms a vector layer from one coordinate system to another.
/// </summary>
public class ReprojectTool : ToolBase
{
    private const string FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg";

    public override string Id => "reproject";
    public override string Name => "Reproject";
    public override string NameZh => "重投影";
    public override string Description => "Transform a vector layer from one coordinate system to another";
    public override string DescriptionZh => "将矢量图层从一个坐标系转换到另一个坐标系";
    public override ToolCategory Category => ToolCategory.Coordinate;

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
                Name = "sourceWkid",
                Label = "Source WKID",
                LabelZh = "源坐标系 WKID",
                Description = L("Source coordinate system WKID", "源坐标系 WKID"),
                Type = ParameterType.Integer,
                Required = true,
                DefaultValue = "4326"
            },
            new ToolParameter
            {
                Name = "targetWkid",
                Label = "Target WKID",
                LabelZh = "目标坐标系 WKID",
                Description = L("Target coordinate system WKID", "目标坐标系 WKID"),
                Type = ParameterType.Integer,
                Required = true,
                DefaultValue = "4490"
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
        var sourceWkid = GetRequiredInt(parameters, "sourceWkid");
        var targetWkid = GetRequiredInt(parameters, "targetWkid");

        progress?.Report(L("Reading input file...", "读取输入文件..."));
        var inputFormat = DetectFormat(inputPath);
        var layer = await Task.Run(() => OguLayerUtil.ReadLayer(inputFormat, inputPath), ct);

        progress?.Report(L($"Reprojecting {layer.GetFeatureCount()} features from WKID {sourceWkid} to {targetWkid}...",
            $"正在将 {layer.GetFeatureCount()} 个要素从 WKID {sourceWkid} 重投影到 {targetWkid}..."));

        foreach (var feature in layer.Features)
        {
            ct.ThrowIfCancellationRequested();

            if (!string.IsNullOrWhiteSpace(feature.Wkt))
                feature.Wkt = CrsUtil.Transform(feature.Wkt, sourceWkid, targetWkid);
        }

        layer.Wkid = targetWkid;

        progress?.Report(L($"Writing {layer.GetFeatureCount()} features...",
            $"正在写入 {layer.GetFeatureCount()} 个要素..."));
        var outputFormat = DetectFormat(outputPath);
        await Task.Run(() => OguLayerUtil.WriteLayer(outputFormat, layer, outputPath), ct);

        return new ToolResult
        {
            Success = true,
            Message = L(
                $"Reproject completed. {layer.GetFeatureCount()} features reprojected from WKID {sourceWkid} to {targetWkid}.",
                $"重投影完成，共重投影 {layer.GetFeatureCount()} 个要素，从 WKID {sourceWkid} 到 {targetWkid}。"),
            OutputPath = outputPath
        };
    }
}
