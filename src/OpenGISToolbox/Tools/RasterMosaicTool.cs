using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenGISToolbox.Models;
using OSGeo.GDAL;

namespace OpenGISToolbox.Tools;

/// <summary>
/// Mosaics (stitches) two rasters of the same CRS into a single output covering
/// the union extent, later inputs layered over earlier ones, via GDAL warp.
/// </summary>
public class RasterMosaicTool : ToolBase
{
    public override string Id => "raster-mosaic";
    public override string Name => "Raster Mosaic";
    public override string NameZh => "栅格镶嵌";
    public override string Description => "Stitch two rasters into one mosaic over the union extent";
    public override string DescriptionZh => "将两个栅格镶嵌为一个覆盖并集范围的栅格";
    public override ToolCategory Category => ToolCategory.Raster;

    public override List<ToolParameter> BuildParameters() => new()
    {
        new ToolParameter
        {
            Name = "input1", Label = "Input Raster 1", LabelZh = "输入栅格 1",
            Description = L("First raster", "第一个栅格"),
            Type = ParameterType.InputFile, Required = true, FileFilter = RasterGdal.RasterFilter
        },
        new ToolParameter
        {
            Name = "input2", Label = "Input Raster 2", LabelZh = "输入栅格 2",
            Description = L("Second raster", "第二个栅格"),
            Type = ParameterType.InputFile, Required = true, FileFilter = RasterGdal.RasterFilter
        },
        new ToolParameter
        {
            Name = "output", Label = "Output Raster", LabelZh = "输出栅格",
            Description = L("Output mosaic raster", "输出镶嵌栅格"),
            Type = ParameterType.OutputFile, Required = true, FileFilter = RasterGdal.RasterFilter
        },
        new ToolParameter
        {
            Name = "resampling", Label = "Resampling", LabelZh = "重采样方法",
            Description = L("Resampling algorithm", "重采样算法"),
            Type = ParameterType.Dropdown, Required = false, DefaultValue = "nearest",
            Options = new[] { "nearest", "bilinear", "cubic" }
        }
    };

    protected override async Task<ToolResult> ExecuteCoreAsync(
        Dictionary<string, string> parameters, IProgress<string>? progress, CancellationToken ct)
    {
        var p1 = GetRequired(parameters, "input1");
        var p2 = GetRequired(parameters, "input2");
        var outputPath = GetRequired(parameters, "output");
        var resampling = GetOptional(parameters, "resampling", "nearest");

        RasterGdal.Ensure();
        progress?.Report(L("Mosaicking rasters...", "正在镶嵌栅格..."));

        await Task.Run(() =>
        {
            using var a = Gdal.Open(p1, Access.GA_ReadOnly)
                ?? throw new ArgumentException(L("Cannot open raster 1.", "无法打开栅格 1。"));
            using var b = Gdal.Open(p2, Access.GA_ReadOnly)
                ?? throw new ArgumentException(L("Cannot open raster 2.", "无法打开栅格 2。"));
            var options = new GDALWarpAppOptions(new[] { "-r", resampling });
            var dst = Gdal.Warp(outputPath, new[] { a, b }, options, null, null)
                ?? throw new InvalidOperationException(L("Mosaic produced no output.", "镶嵌未产生输出。"));
            dst.Dispose();
            options.Dispose();
        }, ct);

        return new ToolResult
        {
            Success = true,
            Message = L("Raster mosaic completed.", "栅格镶嵌完成。"),
            OutputPath = outputPath
        };
    }
}
