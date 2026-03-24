using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenGIS.Utils.DataSource;
using OpenGIS.Utils.Geometry;
using OpenGISToolbox.Models;

namespace OpenGISToolbox.Tools;

/// <summary>
/// Validates geometries in a vector layer and reports issues.
/// </summary>
public class CheckGeometryTool : ToolBase
{
    private const string FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg";

    public override string Id => "check-geometry";
    public override string Name => "Check Geometry";
    public override string NameZh => "检查几何";
    public override string Description => "Validate geometries in a vector layer and report issues";
    public override string DescriptionZh => "验证矢量图层中的几何体并报告问题";
    public override ToolCategory Category => ToolCategory.Validation;

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
            }
        };
    }

    protected override async Task<ToolResult> ExecuteCoreAsync(
        Dictionary<string, string> parameters,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var inputPath = GetRequired(parameters, "input");

        progress?.Report(L("Reading input file...", "读取输入文件..."));
        var inputFormat = DetectFormat(inputPath);
        var layer = await Task.Run(() => OguLayerUtil.ReadLayer(inputFormat, inputPath), ct);

        progress?.Report(L($"Checking {layer.GetFeatureCount()} features...",
            $"正在检查 {layer.GetFeatureCount()} 个要素..."));

        int validCount = 0;
        int invalidCount = 0;
        int emptyCount = 0;
        var invalidDetails = new List<string>();

        foreach (var feature in layer.Features)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrEmpty(feature.Wkt))
            {
                emptyCount++;
                continue;
            }

            var geom = GeometryUtil.Wkt2Geometry(feature.Wkt);
            var result = GeometryUtil.IsValid(geom);
            if (result.IsValid)
            {
                validCount++;
            }
            else
            {
                invalidCount++;
                invalidDetails.Add($"FID {feature.Fid}: {result.ErrorMessage}");
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine(L("=== Geometry Validation Report ===", "=== 几何验证报告 ==="));
        sb.AppendLine(L($"Total features: {layer.GetFeatureCount()}", $"总要素数: {layer.GetFeatureCount()}"));
        sb.AppendLine(L($"Valid: {validCount}", $"有效: {validCount}"));
        sb.AppendLine(L($"Invalid: {invalidCount}", $"无效: {invalidCount}"));
        sb.AppendLine(L($"Empty: {emptyCount}", $"空几何: {emptyCount}"));

        if (invalidDetails.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(L("--- Invalid Geometry Details ---", "--- 无效几何详情 ---"));
            var showCount = Math.Min(invalidDetails.Count, 20);
            for (int i = 0; i < showCount; i++)
            {
                sb.AppendLine(invalidDetails[i]);
            }
            if (invalidDetails.Count > 20)
            {
                sb.AppendLine(L($"... and {invalidDetails.Count - 20} more.",
                    $"... 以及其他 {invalidDetails.Count - 20} 条。"));
            }
        }

        return new ToolResult
        {
            Success = true,
            Message = sb.ToString()
        };
    }
}
