using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MaxRev.Gdal.Core;
using OpenGISToolbox.Models;
using OSGeo.GDAL;

namespace OpenGISToolbox.Tools;

/// <summary>
/// Converts raster data between different formats (GeoTIFF, PNG, JPEG, BMP).
/// </summary>
public class RasterFormatConvertTool : ToolBase
{
    private static readonly Lazy<bool> GdalInitialized = new(() => { GdalBase.ConfigureAll(); return true; });
    private static void EnsureGdalInitialized() => _ = GdalInitialized.Value;

    public override string Id => "raster-format-convert";
    public override string Name => "Raster Format Conversion";
    public override string NameZh => "栅格格式转换";
    public override string Description => "Convert raster data between different formats (GeoTIFF, PNG, JPEG, BMP)";
    public override string DescriptionZh => "在不同格式之间转换栅格数据（GeoTIFF、PNG、JPEG、BMP）";
    public override ToolCategory Category => ToolCategory.Raster;

    public override List<ToolParameter> BuildParameters()
    {
        return new List<ToolParameter>
        {
            new ToolParameter
            {
                Name = "input",
                Label = "Input File",
                LabelZh = "输入文件",
                Description = L("Input raster file", "输入栅格文件"),
                Type = ParameterType.InputFile,
                Required = true,
                FileFilter = "Raster files|*.tif;*.tiff;*.png;*.jpg;*.jpeg;*.bmp"
            },
            new ToolParameter
            {
                Name = "output",
                Label = "Output File",
                LabelZh = "输出文件",
                Description = L("Output raster file", "输出栅格文件"),
                Type = ParameterType.OutputFile,
                Required = true,
                FileFilter = "GeoTIFF|*.tif|PNG|*.png|JPEG|*.jpg|BMP|*.bmp"
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

        progress?.Report(L("Initializing GDAL...", "正在初始化 GDAL..."));
        await Task.Run(EnsureGdalInitialized, ct);

        var ext = Path.GetExtension(outputPath)?.ToLowerInvariant();
        var driverName = ext switch
        {
            ".png" => "PNG",
            ".jpg" or ".jpeg" => "JPEG",
            ".bmp" => "BMP",
            _ => "GTiff"
        };

        progress?.Report(L($"Converting to {driverName} format...", $"正在转换为 {driverName} 格式..."));

        await Task.Run(() =>
        {
            using var srcDs = Gdal.Open(inputPath, Access.GA_ReadOnly)
                ?? throw new InvalidOperationException(L(
                    $"Failed to open input raster: {inputPath}",
                    $"无法打开输入栅格文件: {inputPath}"));

            var driver = Gdal.GetDriverByName(driverName)
                ?? throw new InvalidOperationException(L(
                    $"GDAL driver '{driverName}' not available.",
                    $"GDAL 驱动 '{driverName}' 不可用。"));

            using var outDs = driver.CreateCopy(outputPath, srcDs, 0, null, null, null)
                ?? throw new InvalidOperationException(L(
                    $"Failed to create output raster: {outputPath}",
                    $"无法创建输出栅格文件: {outputPath}"));

            outDs.FlushCache();
        }, ct);

        return new ToolResult
        {
            Success = true,
            Message = L(
                $"Raster format conversion completed. Output saved as {driverName}.",
                $"栅格格式转换完成。输出已保存为 {driverName} 格式。"),
            OutputPath = outputPath
        };
    }
}
