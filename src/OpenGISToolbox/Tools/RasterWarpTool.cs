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
/// Reprojects (warps) a raster into a target coordinate reference system using
/// GDAL's warp engine on the bundled runtime. Also supports an optional extent
/// cut and resampling algorithm.
/// </summary>
public class RasterWarpTool : ToolBase
{
    public override string Id => "raster-warp";
    public override string Name => "Raster Reproject (Warp)";
    public override string NameZh => "栅格重投影";
    public override string Description => "Reproject a raster to a target CRS";
    public override string DescriptionZh => "将栅格重投影到目标坐标系";
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
        new ToolParameter
        {
            Name = "targetWkid", Label = "Target WKID", LabelZh = "目标坐标系 WKID",
            Description = L("Target EPSG/WKID", "目标 EPSG/WKID"),
            Type = ParameterType.Integer, Required = true, DefaultValue = "4326"
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
        var inputPath = GetRequired(parameters, "input");
        var outputPath = GetRequired(parameters, "output");
        var targetWkid = GetRequiredInt(parameters, "targetWkid");
        var resampling = GetOptional(parameters, "resampling", "nearest");

        RasterGdal.Ensure();
        progress?.Report(L($"Reprojecting to EPSG:{targetWkid}...", $"正在重投影到 EPSG:{targetWkid}..."));

        await Task.Run(() =>
        {
            using var src = Gdal.Open(inputPath, Access.GA_ReadOnly)
                ?? throw new ArgumentException(L("Cannot open input raster.", "无法打开输入栅格。"));
            var options = new GDALWarpAppOptions(new[]
            {
                "-t_srs", "EPSG:" + targetWkid.ToString(CultureInfo.InvariantCulture),
                "-r", resampling
            });
            var dst = Gdal.Warp(outputPath, new[] { src }, options, null, null)
                ?? throw new InvalidOperationException(L("Warp produced no output.", "重投影未产生输出。"));
            dst.Dispose();
            options.Dispose();
        }, ct);

        return new ToolResult
        {
            Success = true,
            Message = L($"Reprojection completed to EPSG:{targetWkid}.", $"重投影完成，目标 EPSG:{targetWkid}。"),
            OutputPath = outputPath
        };
    }
}
