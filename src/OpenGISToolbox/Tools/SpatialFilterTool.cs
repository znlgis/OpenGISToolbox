using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OpenGIS.Utils.DataSource;
using OpenGIS.Utils.Engine.Model.Layer;
using OpenGIS.Utils.Geometry;
using OpenGISToolbox.Models;

namespace OpenGISToolbox.Tools;

/// <summary>
/// Filters features by spatial extent (WKT polygon).
/// </summary>
public class SpatialFilterTool : ToolBase
{
    private const string FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg";

    public override string Id => "spatial-filter";
    public override string Name => "Spatial Filter";
    public override string NameZh => "空间过滤";
    public override string Description => "Filter features by spatial extent (WKT polygon)";
    public override string DescriptionZh => "按空间范围过滤要素（WKT 多边形）";
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
                FileFilter = FileFilter
            },
            new ToolParameter
            {
                Name = "output",
                Label = "Output File",
                LabelZh = "输出文件",
                Description = L("Output vector file", "输出矢量文件"),
                Type = ParameterType.OutputFile,
                Required = true,
                FileFilter = FileFilter
            },
            new ToolParameter
            {
                Name = "extentWkt",
                Label = "Filter Extent (WKT)",
                LabelZh = "过滤范围 (WKT)",
                Description = L("WKT polygon defining the spatial filter extent", "定义空间过滤范围的 WKT 多边形"),
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
        var extentWkt = GetRequired(parameters, "extentWkt");

        progress?.Report(L("Reading input file...", "读取输入文件..."));
        var inputFormat = DetectFormat(inputPath);
        var layer = await Task.Run(() => OguLayerUtil.ReadLayer(inputFormat, inputPath), ct);

        progress?.Report(L($"Filtering {layer.GetFeatureCount()} features by spatial extent...",
            $"正在按空间范围过滤 {layer.GetFeatureCount()} 个要素..."));

        var outputLayer = new OguLayer
        {
            Name = Path.GetFileNameWithoutExtension(outputPath),
            GeometryType = layer.GeometryType,
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

            if (string.IsNullOrEmpty(feature.Wkt))
                continue;

            if (GeometryUtil.IntersectsWkt(feature.Wkt, extentWkt))
            {
                var cloned = feature.Clone();
                cloned.Fid = fid++;
                outputLayer.AddFeature(cloned);
            }
        }

        progress?.Report(L($"Writing {outputLayer.GetFeatureCount()} features...",
            $"正在写入 {outputLayer.GetFeatureCount()} 个要素..."));
        var outputFormat = DetectFormat(outputPath);
        await Task.Run(() => OguLayerUtil.WriteLayer(outputFormat, outputLayer, outputPath), ct);

        return new ToolResult
        {
            Success = true,
            Message = L(
                $"Spatial filter completed. {outputLayer.GetFeatureCount()} of {layer.GetFeatureCount()} features matched.",
                $"空间过滤完成，匹配 {outputLayer.GetFeatureCount()}/{layer.GetFeatureCount()} 个要素。"),
            OutputPath = outputPath
        };
    }
}
