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
/// Clips a vector layer by a polygon layer.
/// </summary>
public class ClipTool : ToolBase
{
    private const string InputFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg";
    private const string OutputFilter = "Shapefile|*.shp";

    public override string Id => "clip";
    public override string Name => "Clip";
    public override string NameZh => "裁剪";
    public override string Description => "Clip a vector layer by a polygon layer";
    public override string DescriptionZh => "使用多边形图层裁剪矢量图层";
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
                Description = L("Input vector file to clip", "要裁剪的输入矢量文件"),
                Type = ParameterType.InputFile,
                Required = true,
                FileFilter = InputFilter
            },
            new ToolParameter
            {
                Name = "clipLayer",
                Label = "Clip Polygon File",
                LabelZh = "裁剪多边形文件",
                Description = L("Polygon layer used for clipping", "用于裁剪的多边形图层"),
                Type = ParameterType.InputFile,
                Required = true,
                FileFilter = InputFilter
            },
            new ToolParameter
            {
                Name = "output",
                Label = "Output File",
                LabelZh = "输出文件",
                Description = L("Output clipped vector file", "输出裁剪后的矢量文件"),
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
        var clipPath = GetRequired(parameters, "clipLayer");
        var outputPath = GetRequired(parameters, "output");

        progress?.Report(L("Reading input file...", "读取输入文件..."));
        var inputFormat = DetectFormat(inputPath);
        var inputLayer = await Task.Run(() => OguLayerUtil.ReadLayer(inputFormat, inputPath), ct);

        progress?.Report(L("Reading clip polygon file...", "读取裁剪多边形文件..."));
        var clipFormat = DetectFormat(clipPath);
        var clipLayer = await Task.Run(() => OguLayerUtil.ReadLayer(clipFormat, clipPath), ct);

        progress?.Report(L("Building clip geometry...", "构建裁剪几何体..."));
        var clipWkts = clipLayer.Features
            .Where(f => !string.IsNullOrEmpty(f.Wkt))
            .Select(f => f.Wkt!)
            .ToList();

        var clipGeomWkt = GeometryUtil.UnionWkt(clipWkts);
        var clipGeom = GeometryUtil.Wkt2Geometry(clipGeomWkt);

        progress?.Report(L(
            $"Clipping {inputLayer.GetFeatureCount()} features...",
            $"正在裁剪 {inputLayer.GetFeatureCount()} 个要素..."));

        var outputLayer = new OguLayer
        {
            Name = Path.GetFileNameWithoutExtension(outputPath),
            GeometryType = inputLayer.GeometryType,
            Wkid = inputLayer.Wkid
        };

        foreach (var field in inputLayer.Fields)
        {
            outputLayer.AddField(field.Clone());
        }

        int fid = 0;
        foreach (var feature in inputLayer.Features)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrEmpty(feature.Wkt))
                continue;

            var featureGeom = GeometryUtil.Wkt2Geometry(feature.Wkt);
            var intersection = GeometryUtil.Intersection(featureGeom, clipGeom);

            if (!GeometryUtil.IsEmpty(intersection))
            {
                var cloned = feature.Clone();
                cloned.Fid = fid++;
                cloned.Wkt = GeometryUtil.Geometry2Wkt(intersection);
                outputLayer.AddFeature(cloned);
            }
        }

        progress?.Report(L($"Writing {outputLayer.GetFeatureCount()} features...",
            $"正在写入 {outputLayer.GetFeatureCount()} 个要素..."));
        await Task.Run(() => OguLayerUtil.WriteLayer(DataFormatType.SHP, outputLayer, outputPath), ct);

        return new ToolResult
        {
            Success = true,
            Message = L(
                $"Clip completed. {outputLayer.GetFeatureCount()} of {inputLayer.GetFeatureCount()} features retained.",
                $"裁剪完成，保留 {outputLayer.GetFeatureCount()}/{inputLayer.GetFeatureCount()} 个要素。"),
            OutputPath = outputPath
        };
    }
}
