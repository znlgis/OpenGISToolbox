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
/// Performs band math operations on raster data.
/// </summary>
public class RasterCalculatorTool : ToolBase
{
    private static readonly Lazy<bool> GdalInitialized = new(() => { GdalBase.ConfigureAll(); return true; });
    private static void EnsureGdalInitialized() => _ = GdalInitialized.Value;

    public override string Id => "raster-calculator";
    public override string Name => "Raster Calculator";
    public override string NameZh => "栅格计算器";
    public override string Description => "Perform band math operations on raster data";
    public override string DescriptionZh => "对栅格数据执行波段数学运算";
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
                Description = L("Input GeoTIFF raster file", "输入 GeoTIFF 栅格文件"),
                Type = ParameterType.InputFile,
                Required = true,
                FileFilter = "GeoTIFF|*.tif;*.tiff"
            },
            new ToolParameter
            {
                Name = "output",
                Label = "Output File",
                LabelZh = "输出文件",
                Description = L("Output GeoTIFF file", "输出 GeoTIFF 文件"),
                Type = ParameterType.OutputFile,
                Required = true,
                FileFilter = "GeoTIFF|*.tif"
            },
            new ToolParameter
            {
                Name = "operation",
                Label = "Operation",
                LabelZh = "运算",
                Description = L("Band math operation to perform", "要执行的波段运算"),
                Type = ParameterType.Dropdown,
                Required = true,
                DefaultValue = "NDVI (Band4-Band3)/(Band4+Band3)",
                Options = new[]
                {
                    "NDVI (Band4-Band3)/(Band4+Band3)",
                    "Scale (Band1 * Factor)",
                    "Offset (Band1 + Value)",
                    "Threshold (Band1 > Value)"
                }
            },
            new ToolParameter
            {
                Name = "value",
                Label = "Value / Factor",
                LabelZh = "值 / 因子",
                Description = L("Value or factor for the operation", "运算使用的值或因子"),
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
        var operation = GetRequired(parameters, "operation");
        var value = GetRequiredDouble(parameters, "value");

        progress?.Report(L("Initializing GDAL...", "正在初始化 GDAL..."));
        await Task.Run(EnsureGdalInitialized, ct);

        progress?.Report(L("Processing raster...", "正在处理栅格..."));

        await Task.Run(() =>
        {
            using var srcDs = Gdal.Open(inputPath, Access.GA_ReadOnly)
                ?? throw new InvalidOperationException(L(
                    $"Failed to open input raster: {inputPath}",
                    $"无法打开输入栅格文件: {inputPath}"));

            int width = srcDs.RasterXSize;
            int height = srcDs.RasterYSize;

            // Memory estimation
            var pixelCount = (long)width * height;
            var bytesPerPixel = 8; // double = 8 bytes
            var arrayCount = operation.StartsWith("NDVI") ? 3 : 2;
            var estimatedMB = pixelCount * bytesPerPixel * arrayCount / (1024.0 * 1024.0);

            if (estimatedMB > 2048)
                throw new InvalidOperationException(L(
                    $"Raster is too large ({width}x{height}, estimated {estimatedMB:N0} MB). Consider tiling or using a smaller dataset.",
                    $"栅格太大（{width}x{height}，预计需要 {estimatedMB:N0} MB 内存）。请考虑分块处理或使用较小的数据集。"));

            if (estimatedMB > 512)
                progress?.Report(L(
                    $"Warning: Large raster ({estimatedMB:N0} MB estimated). Processing may be slow.",
                    $"警告：大栅格（预计 {estimatedMB:N0} MB）。处理可能较慢。"));

            var geoTransform = new double[6];
            srcDs.GetGeoTransform(geoTransform);
            var projection = srcDs.GetProjection();

            var driver = Gdal.GetDriverByName("GTiff")
                ?? throw new InvalidOperationException(L(
                    "GTiff driver not available.",
                    "GTiff 驱动不可用。"));

            using var outDs = driver.Create(outputPath, width, height, 1, DataType.GDT_Float64, null)
                ?? throw new InvalidOperationException(L(
                    $"Failed to create output raster: {outputPath}",
                    $"无法创建输出栅格文件: {outputPath}"));

            outDs.SetGeoTransform(geoTransform);
            outDs.SetProjection(projection);

            var outBand = outDs.GetRasterBand(1);

            if (operation.StartsWith("NDVI"))
            {
                if (srcDs.RasterCount < 4)
                    throw new InvalidOperationException(L(
                        $"NDVI requires at least 4 bands, but input has {srcDs.RasterCount}.",
                        $"NDVI 需要至少 4 个波段，但输入仅有 {srcDs.RasterCount} 个。"));

                var red = new double[pixelCount];
                var nir = new double[pixelCount];
                var result = new double[pixelCount];

                srcDs.GetRasterBand(3).ReadRaster(0, 0, width, height, red, width, height, 0, 0);
                srcDs.GetRasterBand(4).ReadRaster(0, 0, width, height, nir, width, height, 0, 0);

                for (long i = 0; i < pixelCount; i++)
                {
                    var sum = nir[i] + red[i];
                    result[i] = sum == 0 ? 0 : (nir[i] - red[i]) / sum;
                }

                outBand.WriteRaster(0, 0, width, height, result, width, height, 0, 0);
            }
            else
            {
                var band1 = new double[pixelCount];
                var result = new double[pixelCount];

                srcDs.GetRasterBand(1).ReadRaster(0, 0, width, height, band1, width, height, 0, 0);

                if (operation.StartsWith("Scale"))
                {
                    for (long i = 0; i < pixelCount; i++)
                        result[i] = band1[i] * value;
                }
                else if (operation.StartsWith("Offset"))
                {
                    for (long i = 0; i < pixelCount; i++)
                        result[i] = band1[i] + value;
                }
                else if (operation.StartsWith("Threshold"))
                {
                    for (long i = 0; i < pixelCount; i++)
                        result[i] = band1[i] > value ? 1.0 : 0.0;
                }

                outBand.WriteRaster(0, 0, width, height, result, width, height, 0, 0);
            }

            outBand.FlushCache();
            outDs.FlushCache();
        }, ct);

        return new ToolResult
        {
            Success = true,
            Message = L(
                $"Raster calculation completed. Operation: {operation}",
                $"栅格计算完成。运算: {operation}"),
            OutputPath = outputPath
        };
    }
}
