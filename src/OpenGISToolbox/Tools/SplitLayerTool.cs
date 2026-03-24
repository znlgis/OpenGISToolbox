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
/// Splits a vector layer into multiple layers based on a field value.
/// </summary>
public class SplitLayerTool : ToolBase
{
    private const string FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg";

    public override string Id => "split-layer";
    public override string Name => "Split Layer";
    public override string NameZh => "拆分图层";
    public override string Description => "Split a vector layer into multiple layers by field value";
    public override string DescriptionZh => "按字段值将矢量图层拆分为多个图层";
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
                Name = "fieldName",
                Label = "Split Field Name",
                LabelZh = "拆分字段名",
                Description = L("Field name to split by", "用于拆分的字段名"),
                Type = ParameterType.Text,
                Required = true
            },
            new ToolParameter
            {
                Name = "outputFolder",
                Label = "Output Folder",
                LabelZh = "输出文件夹",
                Description = L("Output folder for split layers", "拆分图层的输出文件夹"),
                Type = ParameterType.FolderPath,
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
        var fieldName = GetRequired(parameters, "fieldName");
        var outputFolder = GetRequired(parameters, "outputFolder");

        progress?.Report(L("Reading input file...", "读取输入文件..."));
        var inputFormat = DetectFormat(inputPath);
        var layer = await Task.Run(() => OguLayerUtil.ReadLayer(inputFormat, inputPath), ct);

        progress?.Report(L($"Read {layer.GetFeatureCount()} features. Grouping by '{fieldName}'...",
            $"已读取 {layer.GetFeatureCount()} 个要素，正在按 '{fieldName}' 分组..."));

        // Group features by field value
        var groups = layer.Features
            .GroupBy(f => f.GetValue(fieldName)?.ToString() ?? "NULL")
            .ToList();

        if (!Directory.Exists(outputFolder))
            Directory.CreateDirectory(outputFolder);

        int groupCount = 0;
        int fallbackCount = 0;
        foreach (var group in groups)
        {
            ct.ThrowIfCancellationRequested();

            var groupLayer = new OguLayer
            {
                Name = SanitizeFileName(group.Key, ref fallbackCount),
                GeometryType = layer.GeometryType,
                Wkid = layer.Wkid
            };

            foreach (var field in layer.Fields)
            {
                groupLayer.AddField(field.Clone());
            }

            int fid = 0;
            foreach (var feature in group)
            {
                ct.ThrowIfCancellationRequested();
                var cloned = feature.Clone();
                cloned.Fid = fid++;
                groupLayer.AddFeature(cloned);
            }

            var outputPath = Path.Combine(outputFolder, $"{groupLayer.Name}.shp");
            await Task.Run(() => OguLayerUtil.WriteLayer(DataFormatType.SHP, groupLayer, outputPath), ct);
            groupCount++;

            progress?.Report(L(
                $"Written group '{group.Key}' ({groupLayer.GetFeatureCount()} features).",
                $"已写入分组 '{group.Key}'（{groupLayer.GetFeatureCount()} 个要素）。"));
        }

        return new ToolResult
        {
            Success = true,
            Message = L(
                $"Split completed. {layer.GetFeatureCount()} features split into {groupCount} layers.",
                $"拆分完成，{layer.GetFeatureCount()} 个要素拆分为 {groupCount} 个图层。"),
            OutputPath = outputFolder
        };
    }

    private static string SanitizeFileName(string name, ref int fallbackCount)
    {
        var sanitized = string.Join("_",
            name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = $"group_{fallbackCount}";
            fallbackCount++;
        }
        return sanitized;
    }
}
