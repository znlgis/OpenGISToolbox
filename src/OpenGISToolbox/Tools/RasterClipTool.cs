using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using OpenGISToolbox.Models;
using OSGeo.GDAL;

namespace OpenGISToolbox.Tools;

/// <summary>
/// Crops a raster to a bounding-box extent using GDAL warp (-te). The output keeps
/// the source CRS and is trimmed to the requested geographic window.
/// </summary>
public class RasterClipTool : ToolBase
{
    public override string Id => "raster-clip";
    public override string Name => "Raster Clip (by extent)";
    public override string NameZh => "栅格裁剪";
    public override string Description => "Crop a raster to a bounding-box extent";
    public override string DescriptionZh => "按外包框范围裁剪栅格";
    public override ToolCategory Category => ToolCategory.Raster;

    public override List<ToolParameter> BuildParameters() => new()
    {
        new ToolParameter
        {
            Name = "input", Label = "Input Raster", LabelZh = "输入栅格",
            Description = L("Input raster file", "输入栅格文件"),
            Type = ParameterType.InputFile, Required = true, FileFilter = RasterGdal.RasterFilter
        },
        new ToolParameter
        {
            Name = "output", Label = "Output Raster", LabelZh = "输出栅格",
            Description = L("Output raster file", "输出栅格文件"),
            Type = ParameterType.OutputFile, Required = true, FileFilter = RasterGdal.RasterFilter
        },
        new ToolParameter { Name = "xmin", Label = "Min X", LabelZh = "X 最小值",
            Description = L("West bound", "西边界"), Type = ParameterType.Number, Required = true, DefaultValue = "" },
        new ToolParameter { Name = "ymin", Label = "Min Y", LabelZh = "Y 最小值",
            Description = L("South bound", "南边界"), Type = ParameterType.Number, Required = true, DefaultValue = "" },
        new ToolParameter { Name = "xmax", Label = "Max X", LabelZh = "X 最大值",
            Description = L("East bound", "东边界"), Type = ParameterType.Number, Required = true, DefaultValue = "" },
        new ToolParameter { Name = "ymax", Label = "Max Y", LabelZh = "Y 最大值",
            Description = L("North bound", "北边界"), Type = ParameterType.Number, Required = true, DefaultValue = "" }
    };

    protected override async Task<ToolResult> ExecuteCoreAsync(
        Dictionary<string, string> parameters, IProgress<string>? progress, CancellationToken ct)
    {
        var inputPath = GetRequired(parameters, "input");
        var outputPath = GetRequired(parameters, "output");
        var xmin = Num(parameters, "xmin");
        var ymin = Num(parameters, "ymin");
        var xmax = Num(parameters, "xmax");
        var ymax = Num(parameters, "ymax");
        if (xmax <= xmin || ymax <= ymin)
            throw new ArgumentException(L("Clip extent max must exceed min.", "裁剪范围最大值须大于最小值。"));

        RasterGdal.Ensure();
        progress?.Report(L("Clipping raster...", "正在裁剪栅格..."));

        await Task.Run(() =>
        {
            using var src = Gdal.Open(inputPath, Access.GA_ReadOnly)
                ?? throw new ArgumentException(L("Cannot open input raster.", "无法打开输入栅格。"));
            string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);
            var options = new GDALWarpAppOptions(new[]
            {
                "-te", F(xmin), F(ymin), F(xmax), F(ymax)
            });
            var dst = Gdal.Warp(outputPath, new[] { src }, options, null, null)
                ?? throw new InvalidOperationException(L("Clip produced no output.", "裁剪未产生输出。"));
            dst.Dispose();
            options.Dispose();
        }, ct);

        return new ToolResult
        {
            Success = true,
            Message = L("Raster clip completed.", "栅格裁剪完成。"),
            OutputPath = outputPath
        };
    }

    private double Num(Dictionary<string, string> parameters, string key)
    {
        var raw = GetRequired(parameters, key);
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            throw new ArgumentException(L($"Parameter '{key}' is not a valid number.", $"参数 '{key}' 不是有效数字。"));
        return v;
    }
}
