using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenGIS.Utils.DataSource;
using OpenGIS.Utils.Engine.Enums;
using OpenGIS.Utils.Engine.Model.Layer;
using OpenGISToolbox.Models;

namespace OpenGISToolbox.Tools;

/// <summary>
/// Creates a rectangular "fishnet" grid of polygons. The extent comes either from
/// an existing layer's bounding box or from explicit min/max coordinates; the grid
/// is defined by a row and column count. Produces rows×cols cells whose total area
/// equals the extent area.
/// </summary>
public class CreateGridTool : ToolBase
{
    private const string FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg";

    public override string Id => "create-grid";
    public override string Name => "Create Grid";
    public override string NameZh => "创建渔网网格";
    public override string Description => "Generate a rectangular grid of polygons over an extent";
    public override string DescriptionZh => "在给定范围内生成矩形网格面要素";
    public override ToolCategory Category => ToolCategory.Analysis;

    public override List<ToolParameter> BuildParameters() => new()
    {
        new ToolParameter
        {
            Name = "input", Label = "Extent Layer (optional)", LabelZh = "范围图层（可选）",
            Description = L("Layer whose bounding box defines the extent; leave empty to use manual extent",
                "以其外包框作为范围；留空则使用手动范围"),
            Type = ParameterType.InputFile, Required = false, FileFilter = FileFilter, DefaultValue = ""
        },
        new ToolParameter
        {
            Name = "output", Label = "Output File", LabelZh = "输出文件",
            Description = L("Output grid vector file", "输出网格矢量文件"),
            Type = ParameterType.OutputFile, Required = true, FileFilter = FileFilter
        },
        new ToolParameter { Name = "xmin", Label = "Min X", LabelZh = "X 最小值",
            Description = L("Minimum X (used when no extent layer)", "X 最小值（无范围图层时使用）"),
            Type = ParameterType.Number, Required = false, DefaultValue = "" },
        new ToolParameter { Name = "ymin", Label = "Min Y", LabelZh = "Y 最小值",
            Description = L("Minimum Y", "Y 最小值"),
            Type = ParameterType.Number, Required = false, DefaultValue = "" },
        new ToolParameter { Name = "xmax", Label = "Max X", LabelZh = "X 最大值",
            Description = L("Maximum X", "X 最大值"),
            Type = ParameterType.Number, Required = false, DefaultValue = "" },
        new ToolParameter { Name = "ymax", Label = "Max Y", LabelZh = "Y 最大值",
            Description = L("Maximum Y", "Y 最大值"),
            Type = ParameterType.Number, Required = false, DefaultValue = "" },
        new ToolParameter { Name = "rows", Label = "Rows", LabelZh = "行数",
            Description = L("Number of grid rows", "网格行数"),
            Type = ParameterType.Integer, Required = true, DefaultValue = "10" },
        new ToolParameter { Name = "cols", Label = "Columns", LabelZh = "列数",
            Description = L("Number of grid columns", "网格列数"),
            Type = ParameterType.Integer, Required = true, DefaultValue = "10" },
        new ToolParameter { Name = "wkid", Label = "CRS WKID (manual extent)", LabelZh = "坐标系 WKID（手动范围）",
            Description = L("EPSG/WKID of the output when extent is manual", "手动范围时输出的 EPSG/WKID"),
            Type = ParameterType.Integer, Required = false, DefaultValue = "4326" }
    };

    protected override async Task<ToolResult> ExecuteCoreAsync(
        Dictionary<string, string> parameters, IProgress<string>? progress, CancellationToken ct)
    {
        var inputPath = GetOptional(parameters, "input", "");
        var outputPath = GetRequired(parameters, "output");
        var rows = GetRequiredInt(parameters, "rows");
        var cols = GetRequiredInt(parameters, "cols");
        if (rows < 1 || cols < 1)
            throw new ArgumentException(L("Rows and columns must both be >= 1.", "行数与列数均须 ≥ 1。"));

        double[] extent;
        int? wkid;
        if (!string.IsNullOrWhiteSpace(inputPath) && File.Exists(inputPath))
        {
            var src = await Task.Run(() => OguLayerUtil.ReadLayer(DetectFormat(inputPath), inputPath), ct);
            extent = ComputeBounds(src);
            wkid = src.Wkid;
            progress?.Report(L($"Extent from layer: [{extent[0]:R}, {extent[1]:R}, {extent[2]:R}, {extent[3]:R}]",
                $"范围取自图层：[{extent[0]:R}, {extent[1]:R}, {extent[2]:R}, {extent[3]:R}]"));
        }
        else
        {
            var xmin = ParseRequired(parameters, "xmin");
            var ymin = ParseRequired(parameters, "ymin");
            var xmax = ParseRequired(parameters, "xmax");
            var ymax = ParseRequired(parameters, "ymax");
            if (xmax <= xmin || ymax <= ymin)
                throw new ArgumentException(L("Extent max must exceed min.", "范围最大值须大于最小值。"));
            extent = new[] { xmin, ymin, xmax, ymax };
            wkid = int.TryParse(GetOptional(parameters, "wkid", "4326"), out var w) ? w : 4326;
        }

        var (minx, miny, maxx, maxy) = (extent[0], extent[1], extent[2], extent[3]);
        double dx = (maxx - minx) / cols;
        double dy = (maxy - miny) / rows;

        var outputLayer = new OguLayer
        {
            Name = Path.GetFileNameWithoutExtension(outputPath),
            GeometryType = GeometryType.POLYGON,
            Wkid = wkid
        };
        outputLayer.AddField(new OguField { Name = "grid_row", DataType = FieldDataType.INTEGER });
        outputLayer.AddField(new OguField { Name = "grid_col", DataType = FieldDataType.INTEGER });
        outputLayer.AddField(new OguField { Name = "cell_id", DataType = FieldDataType.INTEGER });

        string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);
        int fid = 0;
        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < cols; c++)
            {
                ct.ThrowIfCancellationRequested();
                double x0 = minx + c * dx, x1 = minx + (c + 1) * dx;
                double y0 = miny + r * dy, y1 = miny + (r + 1) * dy;
                var cell = new OguFeature
                {
                    Fid = fid,
                    Wkt = $"POLYGON (({F(x0)} {F(y0)},{F(x1)} {F(y0)},{F(x1)} {F(y1)},{F(x0)} {F(y1)},{F(x0)} {F(y0)}))"
                };
                cell.SetValue("grid_row", r);
                cell.SetValue("grid_col", c);
                cell.SetValue("cell_id", fid);
                outputLayer.AddFeature(cell);
                fid++;
            }
        }

        var outputFormat = DetectFormat(outputPath);
        await Task.Run(() => WriteLayerSafe(outputFormat, outputLayer, outputPath, progress), ct);

        return new ToolResult
        {
            Success = true,
            Message = L(
                $"Grid created: {rows}×{cols} = {outputLayer.GetFeatureCount()} cells.",
                $"网格创建完成：{rows}×{cols} = {outputLayer.GetFeatureCount()} 个单元。"),
            OutputPath = outputPath
        };
    }

    private double ParseRequired(Dictionary<string, string> parameters, string key)
    {
        var raw = GetRequired(parameters, key);
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            throw new ArgumentException(L($"Parameter '{key}' is not a valid number.", $"参数 '{key}' 不是有效数字。"));
        return v;
    }

    private static double[] ComputeBounds(OguLayer layer)
    {
        double minx = double.MaxValue, miny = double.MaxValue;
        double maxx = double.MinValue, maxy = double.MinValue;
        bool any = false;
        foreach (var f in layer.Features)
        {
            var b = GeometryOps.BoundsOfWkt(f.Wkt);
            if (b == null) continue;
            any = true;
            minx = Math.Min(minx, b[0]); miny = Math.Min(miny, b[1]);
            maxx = Math.Max(maxx, b[2]); maxy = Math.Max(maxy, b[3]);
        }
        if (!any)
            throw new ArgumentException(L("The extent layer has no geometry to bound.", "范围图层没有可计算的几何。"));
        return new[] { minx, miny, maxx, maxy };
    }
}
