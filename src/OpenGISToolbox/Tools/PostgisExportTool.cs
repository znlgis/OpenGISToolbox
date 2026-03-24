using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenGIS.Utils.DataSource;
using OpenGIS.Utils.Engine.Util;
using OpenGISToolbox.Models;

namespace OpenGISToolbox.Tools;

/// <summary>
/// Exports (writes) a local vector file to a PostGIS table.
/// </summary>
public class PostgisExportTool : ToolBase
{
    public override string Id => "postgis-export";
    public override string Name => "PostGIS Export";
    public override string NameZh => "PostGIS导出";
    public override string Description => "Upload a local vector file to a PostGIS table.";
    public override string DescriptionZh => "将本地矢量文件上传到PostGIS表。";
    public override ToolCategory Category => ToolCategory.Conversion;

    public override List<ToolParameter> BuildParameters()
    {
        return new List<ToolParameter>
        {
            new ToolParameter
            {
                Name = "input",
                Label = "Input File",
                LabelZh = "输入文件",
                Description = L("Vector file to export to PostGIS", "要导出到PostGIS的矢量文件"),
                Type = ParameterType.InputFile,
                Required = true,
                FileFilter = "Vector Files|*.shp;*.geojson;*.json;*.gpkg;*.kml;*.dxf"
            },
            new ToolParameter
            {
                Name = "connectionString",
                Label = "Connection String",
                LabelZh = "连接字符串",
                Description = L("PostgreSQL/PostGIS connection string", "PostgreSQL/PostGIS连接字符串"),
                Type = ParameterType.Text,
                Required = true
            },
            new ToolParameter
            {
                Name = "tableName",
                Label = "Table Name",
                LabelZh = "表名",
                Description = L("Target PostGIS table name", "目标PostGIS表名"),
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
        var connectionString = GetRequired(parameters, "connectionString");
        var tableName = GetRequired(parameters, "tableName");

        var format = DetectFormat(inputPath);

        progress?.Report(L("Reading input file...", "读取输入文件..."));
        var layer = await Task.Run(() => OguLayerUtil.ReadLayer(format, inputPath), ct);

        progress?.Report(L($"Read {layer.GetFeatureCount()} features. Writing to PostGIS table '{tableName}'...", $"已读取 {layer.GetFeatureCount()} 个要素，正在写入PostGIS表 '{tableName}'..."));
        await Task.Run(() => PostgisUtil.WritePostGIS(layer, connectionString, tableName), ct);

        return new ToolResult
        {
            Success = true,
            Message = L($"Export completed. {layer.GetFeatureCount()} features written to PostGIS table '{tableName}'.", $"导出完成，共写入 {layer.GetFeatureCount()} 个要素到PostGIS表 '{tableName}'。")
        };
    }
}
