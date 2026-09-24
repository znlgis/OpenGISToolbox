using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OpenGISToolbox.Models;
using OSGeo.GDAL;

namespace OpenGISToolbox.Tools;

/// <summary>
/// Derives terrain surfaces from a digital elevation model using GDAL's DEM
/// processing: slope, aspect or hillshade. Runs entirely on the bundled MaxRev
/// runtime (no external gdaldem executable).
/// </summary>
public class DemTerrainTool : ToolBase
{
    public override string Id => "dem-terrain";
    public override string Name => "DEM Terrain Analysis";
    public override string NameZh => "DEM 地形分析";
    public override string Description => "Compute slope, aspect or hillshade from a DEM raster";
    public override string DescriptionZh => "从 DEM 栅格计算坡度、坡向或山体阴影";
    public override ToolCategory Category => ToolCategory.Raster;

    public override List<ToolParameter> BuildParameters() => new()
    {
        new ToolParameter
        {
            Name = "input", Label = "Input DEM", LabelZh = "输入 DEM",
            Description = L("Input DEM raster file", "输入 DEM 栅格文件"),
            Type = ParameterType.InputFile, Required = true, FileFilter = RasterGdal.RasterFilter
        },
        new ToolParameter
        {
            Name = "output", Label = "Output File", LabelZh = "输出文件",
            Description = L("Output raster file", "输出栅格文件"),
            Type = ParameterType.OutputFile, Required = true, FileFilter = RasterGdal.RasterFilter
        },
        new ToolParameter
        {
            Name = "algorithm", Label = "Terrain Model", LabelZh = "地形模型",
            Description = L("Terrain model to compute", "要计算的地形模型"),
            Type = ParameterType.Dropdown, Required = true, DefaultValue = "Slope",
            Options = new[] { "Slope", "Aspect", "Hillshade" }
        },
        new ToolParameter
        {
            Name = "slopeFormat", Label = "Slope Unit", LabelZh = "坡度单位",
            Description = L("Slope output unit (degrees or percent)", "坡度输出单位（度或百分比）"),
            Type = ParameterType.Dropdown, Required = false, DefaultValue = "degrees",
            Options = new[] { "degrees", "percent" }
        },
        new ToolParameter
        {
            Name = "zFactor", Label = "Vertical Factor", LabelZh = "垂直系数",
            Description = L("Hillshade vertical exaggeration (-z)", "山体阴影垂直夸张系数（-z）"),
            Type = ParameterType.Number, Required = false, DefaultValue = "1"
        },
        new ToolParameter
        {
            Name = "azimuth", Label = "Sun Azimuth", LabelZh = "太阳方位角",
            Description = L("Hillshade sun azimuth in degrees", "山体阴影太阳方位角（度）"),
            Type = ParameterType.Number, Required = false, DefaultValue = "315"
        },
        new ToolParameter
        {
            Name = "altitude", Label = "Sun Altitude", LabelZh = "太阳高度角",
            Description = L("Hillshade sun altitude in degrees", "山体阴影太阳高度角（度）"),
            Type = ParameterType.Number, Required = false, DefaultValue = "45"
        }
    };

    protected override async Task<ToolResult> ExecuteCoreAsync(
        Dictionary<string, string> parameters, IProgress<string>? progress, CancellationToken ct)
    {
        var inputPath = GetRequired(parameters, "input");
        var outputPath = GetRequired(parameters, "output");
        var algorithm = GetOptional(parameters, "algorithm", "Slope");
        var slopeFormat = GetOptional(parameters, "slopeFormat", "degrees");
        var zFactor = GetOptional(parameters, "zFactor", "1");
        var azimuth = GetOptional(parameters, "azimuth", "315");
        var altitude = GetOptional(parameters, "altitude", "45");

        var (processing, options) = BuildArgs(algorithm, slopeFormat, zFactor, azimuth, altitude);

        progress?.Report(L($"Running GDAL DEM {processing}...", $"正在执行 GDAL DEM {processing}..."));
        RasterGdal.Ensure();

        var outPath = await Task.Run(() =>
        {
            using var src = Gdal.Open(inputPath, Access.GA_ReadOnly)
                ?? throw new ArgumentException(L("Cannot open input DEM.", "无法打开输入 DEM。"));
            var demOptions = new GDALDEMProcessingOptions(options);
            var dst = Gdal.wrapper_GDALDEMProcessing(outputPath, src, processing, null, demOptions, null, null)
                ?? throw new InvalidOperationException(L(
                    $"DEM processing '{processing}' produced no output.",
                    $"DEM 处理 '{processing}' 未产生输出。"));
            var path = dst.GetDescription();
            dst.Dispose();
            demOptions.Dispose();
            return path ?? outputPath;
        }, ct);

        // This GDAL build's slope --format flag is rejected, so percent slope is
        // produced by post-scaling the degrees output in place: pct = 100·tan(°).
        if (algorithm.Equals("Slope", StringComparison.OrdinalIgnoreCase)
            && slopeFormat.Equals("percent", StringComparison.OrdinalIgnoreCase))
        {
            await Task.Run(() => ToPercentSlope(outPath), ct);
        }

        return new ToolResult
        {
            Success = true,
            Message = L($"DEM {processing} completed.", $"DEM {processing} 计算完成。"),
            OutputPath = outPath
        };
    }

    private static void ToPercentSlope(string path)
    {
        using var ds = Gdal.Open(path, Access.GA_Update)
            ?? throw new InvalidOperationException(L("Cannot reopen slope output.", "无法重新打开坡度输出。"));
        using var band = ds.GetRasterBand(1);
        var w = ds.RasterXSize;
        var h = ds.RasterYSize;
        var data = new double[w * h];
        band.ReadRaster(0, 0, w, h, data, w, h, 0, 0);
        for (var i = 0; i < data.Length; i++)
            if (!double.IsNaN(data[i]))
                data[i] = Math.Tan(data[i] * Math.PI / 180.0) * 100.0;
        band.WriteRaster(0, 0, w, h, data, w, h, 0, 0);
        band.FlushCache();
        ds.FlushCache();
    }

    private (string processing, string[] options) BuildArgs(
        string algorithm, string slopeFormat, string zFactor, string azimuth, string altitude)
    {
        switch (algorithm.ToLowerInvariant())
        {
            case "aspect":
                return ("aspect", new[] { "-compute_edges" });
            case "hillshade":
                return ("hillshade", new[]
                {
                    "-az", azimuth, "-alt", altitude, "-z", zFactor, "-compute_edges"
                });
            default: // slope (degrees by default; percent is post-scaled below)
                return ("slope", new[] { "-compute_edges" });
        }
    }
}
