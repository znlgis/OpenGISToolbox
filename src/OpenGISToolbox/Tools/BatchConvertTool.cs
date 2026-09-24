using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenGIS.Utils.DataSource;
using OpenGIS.Utils.Engine.Enums;
using OpenGISToolbox.Models;

namespace OpenGISToolbox.Tools;

/// <summary>
/// Batch-converts every vector file in a folder from one format to another.
/// Extends the single-file converters to whole directories, reusing the same
/// read/write pipeline (so the same driver caveats and safe-write behaviour apply).
/// </summary>
public class BatchConvertTool : ToolBase
{
    public override string Id => "batch-convert";
    public override string Name => "Batch Format Conversion";
    public override string NameZh => "批量格式转换";
    public override string Description => "Convert all vector files in a folder from one format to another";
    public override string DescriptionZh => "将文件夹中的所有矢量文件从一种格式批量转换为另一种格式";
    public override ToolCategory Category => ToolCategory.Conversion;

    private static readonly string[] Formats = { "SHP", "GeoJSON", "GeoPackage", "KML" };

    public override List<ToolParameter> BuildParameters() => new()
    {
        new ToolParameter
        {
            Name = "inputFolder", Label = "Input Folder", LabelZh = "输入文件夹",
            Description = L("Folder containing source vector files", "包含源矢量文件的文件夹"),
            Type = ParameterType.FolderPath, Required = true
        },
        new ToolParameter
        {
            Name = "outputFolder", Label = "Output Folder", LabelZh = "输出文件夹",
            Description = L("Folder for converted files", "转换后文件的输出文件夹"),
            Type = ParameterType.FolderPath, Required = true
        },
        new ToolParameter
        {
            Name = "sourceFormat", Label = "Source Format", LabelZh = "源格式",
            Description = L("Format of the input files", "输入文件的格式"),
            Type = ParameterType.Dropdown, Required = true, DefaultValue = "SHP", Options = Formats
        },
        new ToolParameter
        {
            Name = "targetFormat", Label = "Target Format", LabelZh = "目标格式",
            Description = L("Format to convert to", "要转换到的格式"),
            Type = ParameterType.Dropdown, Required = true, DefaultValue = "GeoJSON", Options = Formats
        }
    };

    protected override async Task<ToolResult> ExecuteCoreAsync(
        Dictionary<string, string> parameters, IProgress<string>? progress, CancellationToken ct)
    {
        var inputFolder = GetRequired(parameters, "inputFolder");
        var outputFolder = GetRequired(parameters, "outputFolder");
        var sourceFormat = GetOptional(parameters, "sourceFormat", "SHP");
        var targetFormat = GetOptional(parameters, "targetFormat", "GeoJSON");

        if (!Directory.Exists(inputFolder))
            throw new DirectoryNotFoundException(L(
                $"Input folder does not exist: {inputFolder}", $"输入文件夹不存在：{inputFolder}"));

        var (srcExt, srcType) = Resolve(sourceFormat);
        var (tgtExt, tgtType) = Resolve(targetFormat);
        if (sourceFormat == targetFormat)
            throw new ArgumentException(L(
                "Source and target formats are identical.", "源格式与目标格式相同。"));

        if (!Directory.Exists(outputFolder))
            Directory.CreateDirectory(outputFolder);

        var files = Directory.GetFiles(inputFolder, "*" + srcExt, SearchOption.TopDirectoryOnly);
        // For Shapefiles the extension glob may miss case variants; filter again defensively.
        if (sourceFormat == "SHP")
            files = Directory.GetFiles(inputFolder, "*.shp", SearchOption.TopDirectoryOnly)
                .Concat(Directory.GetFiles(inputFolder, "*.SHP", SearchOption.TopDirectoryOnly))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        if (files.Length == 0)
            throw new ArgumentException(L(
                $"No {sourceFormat} files found in the input folder.",
                $"输入文件夹中未找到 {sourceFormat} 文件。"));

        int processed = 0;
        int failed = 0;
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var baseName = Path.GetFileNameWithoutExtension(file);
            var outPath = Path.Combine(outputFolder, baseName + tgtExt);
            progress?.Report(L(
                $"Converting {Path.GetFileName(file)} ({processed + failed + 1}/{files.Length})...",
                $"正在转换 {Path.GetFileName(file)}（{processed + failed + 1}/{files.Length}）..."));
            try
            {
                var layer = await Task.Run(() => OguLayerUtil.ReadLayer(srcType, file), ct);
                await Task.Run(() => WriteLayerSafe(tgtType, layer, outPath, progress), ct);
                processed++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                failed++;
            }
        }

        var failSuffixEn = failed > 0 ? $", {failed} failed" : "";
        var failSuffixZh = failed > 0 ? $"，失败 {failed} 个" : "";
        return new ToolResult
        {
            Success = processed > 0,
            Message = L(
                $"Batch conversion completed. {processed} file(s) converted{failSuffixEn}.",
                $"批量转换完成，成功 {processed} 个文件{failSuffixZh}。"),
            OutputPath = outputFolder
        };
    }

    private static (string ext, DataFormatType type) Resolve(string format) => format switch
    {
        "SHP" => (".shp", DataFormatType.SHP),
        "GeoJSON" => (".geojson", DataFormatType.GEOJSON),
        "GeoPackage" => (".gpkg", DataFormatType.GEOPACKAGE),
        "KML" => (".kml", DataFormatType.KML),
        _ => throw new ArgumentException(L($"Unsupported format: {format}", $"不支持的格式：{format}"))
    };
}
