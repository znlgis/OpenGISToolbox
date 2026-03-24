using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OpenGIS.Utils.DataSource;
using OpenGIS.Utils.Engine.Enums;
using OpenGIS.Utils.Engine.Util;
using OpenGISToolbox.Models;

namespace OpenGISToolbox.Tools;

/// <summary>
/// Reprojects all vector files in a folder to a different coordinate system.
/// </summary>
public class BatchReprojectTool : ToolBase
{
    public override string Id => "batch-reproject";
    public override string Name => "Batch Reproject";
    public override string NameZh => "批量重投影";
    public override string Description => "Reproject all vector files in a folder to a different coordinate system";
    public override string DescriptionZh => "将文件夹中所有矢量文件重投影到不同的坐标系";
    public override ToolCategory Category => ToolCategory.Coordinate;

    public override List<ToolParameter> BuildParameters()
    {
        return new List<ToolParameter>
        {
            new ToolParameter
            {
                Name = "inputFolder",
                Label = "Input Folder",
                LabelZh = "输入文件夹",
                Description = L("Folder containing vector files", "包含矢量文件的文件夹"),
                Type = ParameterType.FolderPath,
                Required = true
            },
            new ToolParameter
            {
                Name = "outputFolder",
                Label = "Output Folder",
                LabelZh = "输出文件夹",
                Description = L("Folder for reprojected files", "重投影文件的输出文件夹"),
                Type = ParameterType.FolderPath,
                Required = true
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
            },
            new ToolParameter
            {
                Name = "format",
                Label = "File Format",
                LabelZh = "文件格式",
                Description = L("Vector file format", "矢量文件格式"),
                Type = ParameterType.Dropdown,
                Required = true,
                DefaultValue = "SHP",
                Options = new[] { "SHP", "GeoJSON", "GeoPackage" }
            }
        };
    }

    protected override async Task<ToolResult> ExecuteCoreAsync(
        Dictionary<string, string> parameters,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var inputFolder = GetRequired(parameters, "inputFolder");
        var outputFolder = GetRequired(parameters, "outputFolder");
        var sourceWkid = GetRequiredInt(parameters, "sourceWkid");
        var targetWkid = GetRequiredInt(parameters, "targetWkid");
        var format = GetOptional(parameters, "format", "SHP");

        var (extension, dataFormat) = format switch
        {
            "GeoJSON" => ("*.geojson", DataFormatType.GEOJSON),
            "GeoPackage" => ("*.gpkg", DataFormatType.GEOPACKAGE),
            _ => ("*.shp", DataFormatType.SHP)
        };

        if (!Directory.Exists(outputFolder))
            Directory.CreateDirectory(outputFolder);

        var files = Directory.GetFiles(inputFolder, extension);
        if (files.Length == 0)
            throw new ArgumentException(L(
                $"No {format} files found in the input folder.",
                $"输入文件夹中未找到 {format} 文件。"));

        var processedCount = 0;
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            progress?.Report(L(
                $"Processing {Path.GetFileName(file)} ({processedCount + 1}/{files.Length})...",
                $"正在处理 {Path.GetFileName(file)}（{processedCount + 1}/{files.Length}）..."));

            var layer = await Task.Run(() => OguLayerUtil.ReadLayer(dataFormat, file), ct);

            foreach (var feature in layer.Features)
            {
                if (!string.IsNullOrWhiteSpace(feature.Wkt))
                    feature.Wkt = CrsUtil.Transform(feature.Wkt, sourceWkid, targetWkid);
            }

            layer.Wkid = targetWkid;

            var outputPath = Path.Combine(outputFolder, Path.GetFileName(file));
            await Task.Run(() => OguLayerUtil.WriteLayer(dataFormat, layer, outputPath), ct);
            processedCount++;
        }

        return new ToolResult
        {
            Success = true,
            Message = L(
                $"Batch reproject completed. {processedCount} files reprojected from WKID {sourceWkid} to {targetWkid}.",
                $"批量重投影完成。{processedCount} 个文件已从 WKID {sourceWkid} 重投影到 {targetWkid}。"),
            OutputPath = outputFolder
        };
    }
}
