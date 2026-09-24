using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OpenGISToolbox.Models;
using OSGeo.GDAL;
using OSGeo.OGR;

namespace OpenGISToolbox.Tools;

/// <summary>
/// Extracts contour lines from a DEM raster at a fixed interval using GDAL's
/// ContourGenerate, writing them to a vector layer (Shapefile or GeoJSON) with an
/// "id" and an "ELEV" elevation attribute per line.
/// </summary>
public class ContourTool : ToolBase
{
    public override string Id => "contour";
    public override string Name => "Contour Extraction";
    public override string NameZh => "等高线提取";
    public override string Description => "Generate contour lines from a DEM raster at a fixed interval";
    public override string DescriptionZh => "按固定间隔从 DEM 栅格生成等高线";
    public override ToolCategory Category => ToolCategory.Raster;

    public override List<ToolParameter> BuildParameters() => new()
    {
        new ToolParameter
        {
            Name = "input", Label = "Input DEM", LabelZh = "输入 DEM",
            Description = L("Input DEM raster file", "输入 DEM 栅格文件"),
            Type = ParameterType.InputFile, Required = true, FileFilter = RasterGdal.RasterFilter
        },
        new ToolParameter
        {
            Name = "output", Label = "Output Vector", LabelZh = "输出矢量",
            Description = L("Output vector file (.shp or .geojson)", "输出矢量文件（.shp 或 .geojson）"),
            Type = ParameterType.OutputFile, Required = true,
            FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson"
        },
        new ToolParameter
        {
            Name = "interval", Label = "Contour Interval", LabelZh = "等高距",
            Description = L("Elevation interval between contours", "相邻等高线的高程间隔"),
            Type = ParameterType.Number, Required = true, DefaultValue = "1"
        },
        new ToolParameter
        {
            Name = "base", Label = "Base Level", LabelZh = "基准高程",
            Description = L("First contour level (offset)", "首条等高线的高程偏移"),
            Type = ParameterType.Number, Required = false, DefaultValue = "0"
        }
    };

    protected override async Task<ToolResult> ExecuteCoreAsync(
        Dictionary<string, string> parameters, IProgress<string>? progress, CancellationToken ct)
    {
        var inputPath = GetRequired(parameters, "input");
        var outputPath = GetRequired(parameters, "output");
        var interval = GetRequiredDouble(parameters, "interval");
        var baseLevel = GetOptional(parameters, "base", "0");
        var baseVal = double.TryParse(baseLevel, NumberStyles.Float, CultureInfo.InvariantCulture, out var b) ? b : 0.0;

        RasterGdal.Ensure();

        long featureCount = await Task.Run(() =>
        {
            var ext = Path.GetExtension(outputPath).ToLowerInvariant();
            string driverName = ext is ".geojson" or ".json" ? "GeoJSON" : "ESRI Shapefile";
            if (File.Exists(outputPath)) TryDelete(outputPath);

            using var src = Gdal.Open(inputPath, Access.GA_ReadOnly)
                ?? throw new ArgumentException(L("Cannot open input DEM.", "无法打开输入 DEM。"));
            using var band = src.GetRasterBand(1);

            var ogrDriver = Ogr.GetDriverByName(driverName)
                ?? throw new InvalidOperationException(L(
                    $"OGR driver {driverName} unavailable.", $"OGR 驱动 {driverName} 不可用。"));
            var ds = ogrDriver.CreateDataSource(outputPath, null)
                ?? throw new InvalidOperationException(L("Cannot create output datasource.", "无法创建输出数据源。"));
            using (ds)
            {
                var layer = ds.CreateLayer("contour", null, wkbGeometryType.wkbLineString, null)
                    ?? throw new InvalidOperationException(L("Cannot create output layer.", "无法创建输出图层。"));
                layer.CreateField(new FieldDefn("id", FieldType.OFTInteger), 1);
                layer.CreateField(new FieldDefn("ELEV", FieldType.OFTReal), 1);

                int rc = Gdal.ContourGenerate(
                    band, interval, baseVal, 0, null, 0, 0.0,
                    layer, 0, 1, null, null);
                if (rc != 0)
                    throw new InvalidOperationException(L(
                        $"ContourGenerate failed with code {rc}.", $"ContourGenerate 失败，代码 {rc}。"));
                layer.SyncToDisk();
                return layer.GetFeatureCount(1);
            }
        }, ct);

        return new ToolResult
        {
            Success = true,
            Message = L(
                $"Contour extraction completed. {featureCount} contour line(s) at interval {interval}.",
                $"等高线提取完成，间隔 {interval}，共 {featureCount} 条等高线。"),
            OutputPath = outputPath
        };
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* overwrite via driver when possible */ }
    }
}
