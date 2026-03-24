using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OpenGIS.Utils.DataSource;
using OpenGIS.Utils.Engine.Enums;
using OpenGIS.Utils.Engine.Model.Layer;
using OpenGISToolbox.Models;

namespace OpenGISToolbox.Tools;

/// <summary>
/// Converts a CSV file with X/Y coordinate columns to a vector point layer.
/// </summary>
public class CsvToVectorTool : ToolBase
{
    public override string Id => "csv-to-vector";
    public override string Name => "CSV to Vector";
    public override string NameZh => "CSV转矢量";
    public override string Description => "Convert a CSV file with coordinate columns to a vector point layer.";
    public override string DescriptionZh => "将包含坐标列的CSV文件转换为矢量点图层。";
    public override ToolCategory Category => ToolCategory.Conversion;

    public override List<ToolParameter> BuildParameters()
    {
        return new List<ToolParameter>
        {
            new ToolParameter
            {
                Name = "input",
                Label = "Input CSV File",
                LabelZh = "输入CSV文件",
                Description = L("CSV file with coordinate columns", "包含坐标列的CSV文件"),
                Type = ParameterType.InputFile,
                Required = true,
                FileFilter = "CSV Files|*.csv"
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
            },
            new ToolParameter
            {
                Name = "xField",
                Label = "X Field",
                LabelZh = "X字段",
                Description = L("Column name for X (longitude) coordinate", "X（经度）坐标的列名"),
                Type = ParameterType.Text,
                Required = true,
                DefaultValue = "x"
            },
            new ToolParameter
            {
                Name = "yField",
                Label = "Y Field",
                LabelZh = "Y字段",
                Description = L("Column name for Y (latitude) coordinate", "Y（纬度）坐标的列名"),
                Type = ParameterType.Text,
                Required = true,
                DefaultValue = "y"
            },
            new ToolParameter
            {
                Name = "delimiter",
                Label = "Delimiter",
                LabelZh = "分隔符",
                Description = L("Column delimiter character", "列分隔符"),
                Type = ParameterType.Dropdown,
                Required = true,
                DefaultValue = ",",
                Options = new[] { ",", "\t", ";", "|" }
            },
            new ToolParameter
            {
                Name = "wkid",
                Label = "Coordinate System WKID",
                LabelZh = "坐标系WKID",
                Description = L("WKID of the coordinate system (e.g. 4326 for WGS84)", "坐标系WKID（如4326为WGS84）"),
                Type = ParameterType.Integer,
                Required = true,
                DefaultValue = "4326"
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
        var xField = GetRequired(parameters, "xField");
        var yField = GetRequired(parameters, "yField");
        var delimiter = GetOptional(parameters, "delimiter", ",");
        var wkid = GetRequiredInt(parameters, "wkid");

        progress?.Report(L("Reading CSV file...", "读取CSV文件..."));

        var lines = await Task.Run(() => File.ReadAllLines(inputPath), ct);
        if (lines.Length < 2)
            throw new ArgumentException(L("CSV file must have a header row and at least one data row.", "CSV文件必须包含表头行和至少一行数据。"));

        // Parse header
        var delimChar = delimiter == "\\t" ? '\t' : delimiter[0];
        var headers = lines[0].Split(delimChar);

        int xIndex = -1, yIndex = -1;
        for (int i = 0; i < headers.Length; i++)
        {
            var h = headers[i].Trim();
            if (string.Equals(h, xField, StringComparison.OrdinalIgnoreCase))
                xIndex = i;
            else if (string.Equals(h, yField, StringComparison.OrdinalIgnoreCase))
                yIndex = i;
        }

        if (xIndex < 0)
            throw new ArgumentException(L($"X field '{xField}' not found in CSV header.", $"在CSV表头中未找到X字段 '{xField}'。"));
        if (yIndex < 0)
            throw new ArgumentException(L($"Y field '{yField}' not found in CSV header.", $"在CSV表头中未找到Y字段 '{yField}'。"));

        // Build layer
        var layer = new OguLayer
        {
            Name = Path.GetFileNameWithoutExtension(outputPath),
            GeometryType = GeometryType.POINT,
            Wkid = wkid
        };

        // Add attribute fields (all columns except x/y as STRING)
        for (int i = 0; i < headers.Length; i++)
        {
            if (i == xIndex || i == yIndex) continue;
            layer.AddField(new OguField
            {
                Name = headers[i].Trim(),
                DataType = FieldDataType.STRING,
                Length = 254
            });
        }

        progress?.Report(L("Creating features...", "创建要素..."));

        int fid = 0;
        int skipped = 0;

        for (int row = 1; row < lines.Length; row++)
        {
            ct.ThrowIfCancellationRequested();

            var line = lines[row];
            if (string.IsNullOrWhiteSpace(line)) continue;

            var values = line.Split(delimChar);
            if (values.Length <= Math.Max(xIndex, yIndex)) continue;

            if (!double.TryParse(values[xIndex].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                !double.TryParse(values[yIndex].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            {
                skipped++;
                continue;
            }

            var feature = new OguFeature
            {
                Fid = fid++,
                Wkt = $"POINT ({x.ToString(CultureInfo.InvariantCulture)} {y.ToString(CultureInfo.InvariantCulture)})"
            };

            // Set attribute values
            for (int i = 0; i < headers.Length; i++)
            {
                if (i == xIndex || i == yIndex) continue;
                var val = i < values.Length ? values[i].Trim() : "";
                feature.SetValue(headers[i].Trim(), val);
            }

            layer.AddFeature(feature);
        }

        progress?.Report(L($"Writing {layer.GetFeatureCount()} features...", $"正在写入 {layer.GetFeatureCount()} 个要素..."));
        await Task.Run(() => OguLayerUtil.WriteLayer(DataFormatType.SHP, layer, outputPath), ct);

        var message = skipped > 0
            ? L($"Conversion completed. {layer.GetFeatureCount()} features created, {skipped} rows skipped (invalid coordinates).",
                $"转换完成，共创建 {layer.GetFeatureCount()} 个要素，跳过 {skipped} 行（坐标无效）。")
            : L($"Conversion completed. {layer.GetFeatureCount()} features created.",
                $"转换完成，共创建 {layer.GetFeatureCount()} 个要素。");

        return new ToolResult
        {
            Success = true,
            Message = message,
            OutputPath = outputPath
        };
    }
}
