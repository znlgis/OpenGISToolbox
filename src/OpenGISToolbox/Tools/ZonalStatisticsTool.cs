using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenGIS.Utils.DataSource;
using OpenGIS.Utils.Engine.Model.Layer;
using OpenGIS.Utils.Geometry;
using OpenGISToolbox.Models;
using OSGeo.GDAL;

namespace OpenGISToolbox.Tools;

/// <summary>
/// Zonal statistics: for every polygon zone, summarises the raster cells whose
/// centres fall inside it (count, min, max, mean). Pure managed computation over
/// the pixel buffer — no external zonal/rasterize binding required — so it is
/// deterministic and directly testable.
/// </summary>
public class ZonalStatisticsTool : ToolBase
{
    public override string Id => "zonal-statistics";
    public override string Name => "Zonal Statistics";
    public override string NameZh => "分区统计";
    public override string Description => "Summarise a raster per polygon zone (count/min/max/mean)";
    public override string DescriptionZh => "按多边形分区统计栅格（计数/最小/最大/平均）";
    public override ToolCategory Category => ToolCategory.Raster;

    public override List<ToolParameter> BuildParameters() => new()
    {
        new ToolParameter
        {
            Name = "polygons", Label = "Zones (Polygons)", LabelZh = "分区（多边形）",
            Description = L("Polygon layer defining the zones", "定义分区的多边形图层"),
            Type = ParameterType.InputFile, Required = true,
            FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg"
        },
        new ToolParameter
        {
            Name = "raster", Label = "Value Raster", LabelZh = "取值栅格",
            Description = L("Raster providing the values to summarise", "提供统计值的栅格"),
            Type = ParameterType.InputFile, Required = true, FileFilter = RasterGdal.RasterFilter
        },
        new ToolParameter
        {
            Name = "output", Label = "Output File", LabelZh = "输出文件",
            Description = L("Output polygon layer with statistics fields", "带统计字段的输出多边形图层"),
            Type = ParameterType.OutputFile, Required = true,
            FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg"
        },
        new ToolParameter
        {
            Name = "band", Label = "Raster Band", LabelZh = "栅格波段",
            Description = L("1-based band index", "从 1 开始的波段序号"),
            Type = ParameterType.Integer, Required = false, DefaultValue = "1"
        }
    };

    protected override async Task<ToolResult> ExecuteCoreAsync(
        Dictionary<string, string> parameters, IProgress<string>? progress, CancellationToken ct)
    {
        var polyPath = GetRequired(parameters, "polygons");
        var rasterPath = GetRequired(parameters, "raster");
        var outputPath = GetRequired(parameters, "output");
        var bandIndex = GetRequiredInt(parameters, "band");

        RasterGdal.Ensure();

        var polygons = await Task.Run(() => OguLayerUtil.ReadLayer(DetectFormat(polyPath), polyPath), ct);

        var raster = await Task.Run(() => ReadRaster(rasterPath, bandIndex), ct);

        progress?.Report(L(
            $"Computing statistics for {polygons.GetFeatureCount()} zones over a {raster.Width}x{raster.Height} raster...",
            $"正在为 {polygons.GetFeatureCount()} 个分区、{raster.Width}×{raster.Height} 栅格计算统计值..."));

        var outputLayer = new OguLayer
        {
            Name = Path.GetFileNameWithoutExtension(outputPath),
            GeometryType = polygons.GeometryType,
            Wkid = polygons.Wkid
        };
        foreach (var field in polygons.Fields)
            outputLayer.AddField(field.Clone());
        outputLayer.AddField(new OguField { Name = "zone_count", DataType = OpenGIS.Utils.Engine.Enums.FieldDataType.INTEGER });
        outputLayer.AddField(new OguField { Name = "zone_min", DataType = OpenGIS.Utils.Engine.Enums.FieldDataType.DOUBLE });
        outputLayer.AddField(new OguField { Name = "zone_max", DataType = OpenGIS.Utils.Engine.Enums.FieldDataType.DOUBLE });
        outputLayer.AddField(new OguField { Name = "zone_mean", DataType = OpenGIS.Utils.Engine.Enums.FieldDataType.DOUBLE });

        foreach (var feature in polygons.Features)
        {
            ct.ThrowIfCancellationRequested();
            var outFeature = feature.Clone();

            int count = 0; double min = double.MaxValue, max = double.MinValue, sum = 0;
            var bounds = GeometryOps.BoundsOfWkt(feature.Wkt);
            if (!string.IsNullOrWhiteSpace(feature.Wkt) && bounds != null)
            {
                for (var row = 0; row < raster.Height; row++)
                {
                    double cy = raster.Gt[3] + (row + 0.5) * raster.Gt[5];
                    if (cy < bounds[1] || cy > bounds[3]) continue; // prune by y
                    for (var col = 0; col < raster.Width; col++)
                    {
                        double cx = raster.Gt[0] + (col + 0.5) * raster.Gt[1];
                        if (cx < bounds[0] || cx > bounds[2]) continue; // prune by x
                        var value = raster.Values[row * raster.Width + col];
                        if (double.IsNaN(value)) continue;
                        var pt = string.Format(CultureInfo.InvariantCulture, "POINT ({0} {1})", cx, cy);
                        bool inside;
                        try { inside = GeometryUtil.ContainsWkt(feature.Wkt!, pt); }
                        catch { inside = false; }
                        if (!inside) continue;
                        count++;
                        if (value < min) min = value;
                        if (value > max) max = value;
                        sum += value;
                    }
                }
            }

            outFeature.SetValue("zone_count", count);
            outFeature.SetValue("zone_min", count > 0 ? min : 0.0);
            outFeature.SetValue("zone_max", count > 0 ? max : 0.0);
            outFeature.SetValue("zone_mean", count > 0 ? sum / count : 0.0);
            outputLayer.AddFeature(outFeature);
        }

        await Task.Run(() => WriteLayerSafe(DetectFormat(outputPath), outputLayer, outputPath, progress), ct);

        return new ToolResult
        {
            Success = true,
            Message = L(
                $"Zonal statistics completed for {outputLayer.GetFeatureCount()} zones.",
                $"分区统计完成，共 {outputLayer.GetFeatureCount()} 个分区。"),
            OutputPath = outputPath
        };
    }

    private static RasterGrid ReadRaster(string path, int bandIndex)
    {
        using var ds = Gdal.Open(path, Access.GA_ReadOnly)
            ?? throw new ArgumentException(L("Cannot open raster.", "无法打开栅格。"));
        var w = ds.RasterXSize;
        var h = ds.RasterYSize;
        var gt = new double[6];
        ds.GetGeoTransform(gt);
        using var band = ds.GetRasterBand(bandIndex);
        var buf = new double[w * h];
        band.ReadRaster(0, 0, w, h, buf, w, h, 0, 0);
        return new RasterGrid { Width = w, Height = h, Gt = gt, Values = buf };
    }

    private sealed class RasterGrid
    {
        public int Width;
        public int Height;
        public double[] Gt = new double[6];
        public double[] Values = Array.Empty<double>();
    }
}
