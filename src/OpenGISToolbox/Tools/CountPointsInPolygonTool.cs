using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenGIS.Utils.DataSource;
using OpenGIS.Utils.Engine.Enums;
using OpenGIS.Utils.Engine.Model.Layer;
using OpenGIS.Utils.Geometry;
using OpenGISToolbox.Models;

namespace OpenGISToolbox.Tools;

/// <summary>
/// Counts how many point features fall inside each polygon and writes the tally
/// into a new integer field. Uses a bounding-box prefilter then a strict
/// point-in-polygon (Contains) test, so Σ(count) equals the number of points that
/// lie within some polygon (a full coverage yields the total point count).
/// </summary>
public class CountPointsInPolygonTool : ToolBase
{
    private const string FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg";

    public override string Id => "count-points-in-polygon";
    public override string Name => "Count Points in Polygon";
    public override string NameZh => "点在多边形计数";
    public override string Description => "Count point features falling inside each polygon into a new field";
    public override string DescriptionZh => "统计落入每个多边形内的点要素数量并写入新字段";
    public override ToolCategory Category => ToolCategory.Analysis;

    public override List<ToolParameter> BuildParameters() => new()
    {
        new ToolParameter
        {
            Name = "input", Label = "Polygon Layer", LabelZh = "多边形图层",
            Description = L("Polygon layer to count into", "用于承载计数结果的多边形图层"),
            Type = ParameterType.InputFile, Required = true, FileFilter = FileFilter
        },
        new ToolParameter
        {
            Name = "points", Label = "Point Layer", LabelZh = "点图层",
            Description = L("Point layer to count", "要统计的点图层"),
            Type = ParameterType.InputFile, Required = true, FileFilter = FileFilter
        },
        new ToolParameter
        {
            Name = "output", Label = "Output File", LabelZh = "输出文件",
            Description = L("Output polygon vector file", "输出多边形矢量文件"),
            Type = ParameterType.OutputFile, Required = true, FileFilter = FileFilter
        },
        new ToolParameter
        {
            Name = "field", Label = "Count Field Name", LabelZh = "计数字段名",
            Description = L("Name of the integer count field to add", "要添加的整数字段名称"),
            Type = ParameterType.Text, Required = true, DefaultValue = "pt_count"
        }
    };

    protected override async Task<ToolResult> ExecuteCoreAsync(
        Dictionary<string, string> parameters, IProgress<string>? progress, CancellationToken ct)
    {
        var polyPath = GetRequired(parameters, "input");
        var pointPath = GetRequired(parameters, "points");
        var outputPath = GetRequired(parameters, "output");
        var fieldName = SanitizeField(GetOptional(parameters, "field", "pt_count"));

        var polygons = await Task.Run(() => OguLayerUtil.ReadLayer(DetectFormat(polyPath), polyPath), ct);
        var points = await Task.Run(() => OguLayerUtil.ReadLayer(DetectFormat(pointPath), pointPath), ct);

        var pointList = new List<(string Wkt, double X, double Y)>();
        foreach (var p in points.Features)
        {
            if (string.IsNullOrWhiteSpace(p.Wkt)) continue;
            var xy = GeometryOps.ExtractCoordinates(p.Wkt).FirstOrDefault();
            if (xy == default && string.IsNullOrEmpty(p.Wkt)) continue;
            pointList.Add((p.Wkt!, xy.X, xy.Y));
        }

        progress?.Report(L($"Counting {pointList.Count} points across {polygons.GetFeatureCount()} polygons...",
            $"正在 {polygons.GetFeatureCount()} 个多边形中统计 {pointList.Count} 个点..."));

        var outputLayer = new OguLayer
        {
            Name = Path.GetFileNameWithoutExtension(outputPath),
            GeometryType = polygons.GeometryType,
            Wkid = polygons.Wkid
        };
        foreach (var field in polygons.Fields)
            outputLayer.AddField(field.Clone());
        outputLayer.AddField(new OguField { Name = fieldName, DataType = FieldDataType.INTEGER });

        int assigned = 0;
        foreach (var feature in polygons.Features)
        {
            ct.ThrowIfCancellationRequested();
            var outFeature = feature.Clone();
            int count = 0;
            if (!string.IsNullOrWhiteSpace(feature.Wkt))
            {
                var bounds = GeometryOps.BoundsOfWkt(feature.Wkt);
                if (bounds != null)
                {
                    foreach (var (wkt, x, y) in pointList)
                    {
                        if (x < bounds[0] || x > bounds[2] || y < bounds[1] || y > bounds[3]) continue;
                        if (Safe(() => GeometryUtil.ContainsWkt(feature.Wkt!, wkt)))
                            count++;
                    }
                }
            }
            outFeature.SetValue(fieldName, count);
            outputLayer.AddFeature(outFeature);
            assigned += count;
        }

        var outputFormat = DetectFormat(outputPath);
        await Task.Run(() => WriteLayerSafe(outputFormat, outputLayer, outputPath, progress), ct);

        return new ToolResult
        {
            Success = true,
            Message = L(
                $"Point count completed. {assigned} of {pointList.Count} points fell inside polygons.",
                $"点计数完成，{pointList.Count} 个点中有 {assigned} 个落入多边形。"),
            OutputPath = outputPath
        };
    }

    private static bool Safe(Func<bool> test)
    {
        try { return test(); }
        catch { return false; }
    }

    private static string SanitizeField(string name)
    {
        var cleaned = new string(name.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
        if (string.IsNullOrWhiteSpace(cleaned)) cleaned = "pt_count";
        return cleaned;
    }
}
