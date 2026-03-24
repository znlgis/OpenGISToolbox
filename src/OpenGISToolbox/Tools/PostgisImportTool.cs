using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenGIS.Utils.DataSource;
using OpenGIS.Utils.Engine.Enums;
using OpenGIS.Utils.Engine.Util;
using OpenGISToolbox.Models;

namespace OpenGISToolbox.Tools;

/// <summary>
/// Imports (reads) a PostGIS table and writes it to a local vector file.
/// </summary>
public class PostgisImportTool : ToolBase
{
    public override string Id => "postgis-import";
    public override string Name => "PostGIS Import";
    public override string NameZh => "PostGIS导入";
    public override string Description => "Read a PostGIS table and export it to a local shapefile.";
    public override string DescriptionZh => "读取PostGIS表并导出为本地Shapefile文件。";
    public override ToolCategory Category => ToolCategory.Conversion;

    public override List<ToolParameter> BuildParameters()
    {
        return new List<ToolParameter>
        {
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
                Description = L("Name of the PostGIS table to import", "要导入的PostGIS表名"),
                Type = ParameterType.Text,
                Required = true
            },
            new ToolParameter
            {
                Name = "output",
                Label = "Output File",
                LabelZh = "输出文件",
                Description = L("Output shapefile path", "输出Shapefile路径"),
                Type = ParameterType.OutputFile,
                Required = true,
                FileFilter = "Shapefile|*.shp"
            }
        };
    }

    protected override async Task<ToolResult> ExecuteCoreAsync(
        Dictionary<string, string> parameters,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var connectionString = GetRequired(parameters, "connectionString");
        var tableName = GetRequired(parameters, "tableName");
        var outputPath = GetRequired(parameters, "output");

        progress?.Report(L($"Reading PostGIS table '{tableName}'...", $"读取PostGIS表 '{tableName}'..."));
        var layer = await Task.Run(() => PostgisUtil.ReadPostGIS(connectionString, tableName, null), ct);

        progress?.Report(L($"Read {layer.GetFeatureCount()} features. Writing output...", $"已读取 {layer.GetFeatureCount()} 个要素，正在写入输出..."));
        await Task.Run(() => OguLayerUtil.WriteLayer(DataFormatType.SHP, layer, outputPath), ct);

        return new ToolResult
        {
            Success = true,
            Message = L($"Import completed. {layer.GetFeatureCount()} features exported to shapefile.", $"导入完成，共导出 {layer.GetFeatureCount()} 个要素到Shapefile。"),
            OutputPath = outputPath
        };
    }
}
