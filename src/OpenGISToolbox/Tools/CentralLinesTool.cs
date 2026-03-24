using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OpenGIS.Utils.DataSource;
using OpenGIS.Utils.Engine.Enums;
using OpenGIS.Utils.Engine.Model.Layer;
using OpenGIS.Utils.Geometry;
using OpenGISToolbox.Models;

namespace OpenGISToolbox.Tools;

/// <summary>
/// Approximates center lines of polygons using negative buffering.
/// </summary>
public class CentralLinesTool : ToolBase
{
    private const string InputFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg";
    private const string OutputFilter = "Shapefile|*.shp";

    public override string Id => "central-lines";
    public override string Name => "Central Lines";
    public override string NameZh => "中心线";
    public override string Description => "Approximate center lines of polygons using negative buffering";
    public override string DescriptionZh => "使用负缓冲区近似计算多边形的中心线";
    public override ToolCategory Category => ToolCategory.Geometry;

    public override List<ToolParameter> BuildParameters()
    {
        return new List<ToolParameter>
        {
            new ToolParameter
            {
                Name = "input",
                Label = "Input File",
                LabelZh = "输入文件",
                Description = L("Input polygon vector file", "输入多边形矢量文件"),
                Type = ParameterType.InputFile,
                Required = true,
                FileFilter = InputFilter
            },
            new ToolParameter
            {
                Name = "output",
                Label = "Output File",
                LabelZh = "输出文件",
                Description = L("Output line vector file", "输出线矢量文件"),
                Type = ParameterType.OutputFile,
                Required = true,
                FileFilter = OutputFilter
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

        progress?.Report(L("Reading input file...", "读取输入文件..."));
        var inputFormat = DetectFormat(inputPath);
        var layer = await Task.Run(() => OguLayerUtil.ReadLayer(inputFormat, inputPath), ct);

        progress?.Report(L($"Read {layer.GetFeatureCount()} features. Computing center lines...",
            $"已读取 {layer.GetFeatureCount()} 个要素，正在计算中心线..."));

        var outputLayer = new OguLayer
        {
            Name = Path.GetFileNameWithoutExtension(outputPath),
            GeometryType = GeometryType.MULTILINESTRING,
            Wkid = layer.Wkid
        };

        foreach (var field in layer.Fields)
        {
            outputLayer.AddField(field.Clone());
        }

        int fid = 0;
        foreach (var feature in layer.Features)
        {
            ct.ThrowIfCancellationRequested();

            var outFeature = feature.Clone();
            outFeature.Fid = fid++;

            if (!string.IsNullOrEmpty(feature.Wkt))
            {
                var geom = GeometryUtil.Wkt2Geometry(feature.Wkt);
                var area = GeometryUtil.Area(geom);
                var boundary = GeometryUtil.Boundary(geom);
                var boundaryLength = GeometryUtil.Length(boundary);

                if (area > 0 && boundaryLength > 0)
                {
                    var approxWidth = 2.0 * area / boundaryLength;
                    var negativeBuffer = GeometryUtil.Buffer(geom, -approxWidth * 0.45);

                    if (!GeometryUtil.IsEmpty(negativeBuffer))
                    {
                        var centerLine = GeometryUtil.Boundary(negativeBuffer);
                        outFeature.Wkt = GeometryUtil.Geometry2Wkt(centerLine);
                    }
                    else
                    {
                        var centroid = GeometryUtil.Centroid(geom);
                        outFeature.Wkt = GeometryUtil.Geometry2Wkt(centroid);
                    }
                }
                else
                {
                    var centroid = GeometryUtil.Centroid(geom);
                    outFeature.Wkt = GeometryUtil.Geometry2Wkt(centroid);
                }
            }

            outputLayer.AddFeature(outFeature);
        }

        progress?.Report(L($"Writing {outputLayer.GetFeatureCount()} features...",
            $"正在写入 {outputLayer.GetFeatureCount()} 个要素..."));
        await Task.Run(() => OguLayerUtil.WriteLayer(DataFormatType.SHP, outputLayer, outputPath), ct);

        return new ToolResult
        {
            Success = true,
            Message = L(
                $"Center lines completed. {outputLayer.GetFeatureCount()} features processed.",
                $"中心线计算完成，共处理 {outputLayer.GetFeatureCount()} 个要素。"),
            OutputPath = outputPath
        };
    }
}
