using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenGIS.Utils.DataSource;
using OpenGIS.Utils.Engine.Enums;
using OpenGISToolbox.Models;

namespace OpenGISToolbox.Tools;

/// <summary>
/// Filters features by attribute expression (SQL WHERE clause).
/// </summary>
public class AttributeQueryTool : ToolBase
{
    private const string InputFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg";
    private const string OutputFilter = "Shapefile|*.shp";

    public override string Id => "attribute-query";
    public override string Name => "Attribute Query";
    public override string NameZh => "属性查询";
    public override string Description => "Filter features by attribute expression (SQL WHERE clause)";
    public override string DescriptionZh => "按属性表达式过滤要素（SQL WHERE 子句）";
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
                FileFilter = InputFilter
            },
            new ToolParameter
            {
                Name = "output",
                Label = "Output File",
                LabelZh = "输出文件",
                Description = L("Output shapefile", "输出 Shapefile"),
                Type = ParameterType.OutputFile,
                Required = true,
                FileFilter = OutputFilter
            },
            new ToolParameter
            {
                Name = "whereClause",
                Label = "WHERE Clause",
                LabelZh = "WHERE 子句",
                Description = L("SQL WHERE clause for filtering", "用于过滤的 SQL WHERE 子句"),
                Type = ParameterType.Text,
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
        var outputPath = GetRequired(parameters, "output");
        var whereClause = GetRequired(parameters, "whereClause");
        var inputFormat = DetectFormat(inputPath);

        progress?.Report(L("Reading and filtering layer...", "正在读取并过滤图层..."));
        var layer = await Task.Run(() =>
            OguLayerUtil.ReadLayer(inputFormat, inputPath, null, whereClause, null, null, null), ct);

        progress?.Report(L($"Writing {layer.GetFeatureCount()} filtered features...",
            $"正在写入 {layer.GetFeatureCount()} 个过滤后的要素..."));
        await Task.Run(() => OguLayerUtil.WriteLayer(DataFormatType.SHP, layer, outputPath), ct);

        return new ToolResult
        {
            Success = true,
            Message = L(
                $"Attribute query completed. {layer.GetFeatureCount()} features matched.",
                $"属性查询完成。匹配了 {layer.GetFeatureCount()} 个要素。"),
            OutputPath = outputPath
        };
    }
}
