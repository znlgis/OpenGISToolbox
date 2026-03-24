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
/// Calculates the length of each feature in a vector layer.
/// </summary>
public class CalculateLengthTool : ToolBase
{
    private const string FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg";

    public override string Id => "calculate-length";
    public override string Name => "Calculate Length";
    public override string NameZh => "计算长度";
    public override string Description => "Calculate the length of each feature in a vector layer";
    public override string DescriptionZh => "计算矢量图层中每个要素的长度";
    public override ToolCategory Category => ToolCategory.Analysis;

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

        if (layer.GetFeatureCount() == 0)
        {
            return new ToolResult
            {
                Success = true,
                Message = L("The layer contains no features.", "图层不包含任何要素。")
            };
        }

        progress?.Report(L($"Calculating length for {layer.GetFeatureCount()} features...",
            $"正在计算 {layer.GetFeatureCount()} 个要素的长度..."));

        var sb = new StringBuilder();
        sb.AppendLine(L("=== Length Calculation Report ===", "=== 长度计算报告 ==="));
        sb.AppendLine($"{"FID",-10} {"Length",20}");
        sb.AppendLine(new string('-', 32));

        double totalLength = 0;
        int count = 0;

        foreach (var feature in layer.Features)
        {
            ct.ThrowIfCancellationRequested();

            double length = 0;
            if (!string.IsNullOrEmpty(feature.Wkt))
            {
                length = GeometryUtil.LengthWkt(feature.Wkt);
            }
            totalLength += length;

            if (count < 50)
            {
                sb.AppendLine($"{feature.Fid,-10} {length,20:N6}");
            }
            count++;
        }

        if (count > 50)
        {
            sb.AppendLine(L($"... and {count - 50} more features.",
                $"... 以及其他 {count - 50} 个要素。"));
        }

        sb.AppendLine(new string('-', 32));
        sb.AppendLine(L($"Total Length: {totalLength:N6} (coordinate system units)",
            $"总长度: {totalLength:N6}（坐标系单位）"));

        return new ToolResult
        {
            Success = true,
            Message = sb.ToString()
        };
    }
}
