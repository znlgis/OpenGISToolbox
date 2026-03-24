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
/// Computes the geometric intersection of two vector layers.
/// </summary>
public class IntersectionTool : ToolBase
{
    private const string FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg";

    public override string Id => "intersection";
    public override string Name => "Intersection";
    public override string NameZh => "交集";
    public override string Description => "Compute the geometric intersection of two vector layers";
    public override string DescriptionZh => "计算两个矢量图层的几何交集";
    public override ToolCategory Category => ToolCategory.Geometry;

    public override List<ToolParameter> BuildParameters()
    {
        return new List<ToolParameter>
        {
            new ToolParameter
            {
                Name = "input1",
                Label = "Input File 1",
                LabelZh = "输入文件 1",
                Description = L("First input vector file", "第一个输入矢量文件"),
                Type = ParameterType.InputFile,
                Required = true,
                FileFilter = FileFilter
            },
            new ToolParameter
            {
                Name = "input2",
                Label = "Input File 2",
                LabelZh = "输入文件 2",
                Description = L("Second input vector file (overlay)", "第二个输入矢量文件（叠加层）"),
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
            }
        };
    }

    protected override async Task<ToolResult> ExecuteCoreAsync(
        Dictionary<string, string> parameters,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var input1Path = GetRequired(parameters, "input1");
        var input2Path = GetRequired(parameters, "input2");
        var outputPath = GetRequired(parameters, "output");

        progress?.Report(L("Reading input files...", "读取输入文件..."));
        var layer1 = await Task.Run(() => OguLayerUtil.ReadLayer(DetectFormat(input1Path), input1Path), ct);
        var layer2 = await Task.Run(() => OguLayerUtil.ReadLayer(DetectFormat(input2Path), input2Path), ct);

        progress?.Report(L($"Read {layer1.GetFeatureCount()} + {layer2.GetFeatureCount()} features. Computing intersection...",
            $"已读取 {layer1.GetFeatureCount()} + {layer2.GetFeatureCount()} 个要素，正在计算交集..."));

        // Union all layer2 geometries into a single overlay geometry
        var layer2Wkts = new List<string>();
        foreach (var feature in layer2.Features)
        {
            ct.ThrowIfCancellationRequested();
            if (!string.IsNullOrEmpty(feature.Wkt))
                layer2Wkts.Add(feature.Wkt);
        }

        var overlayWkt = GeometryUtil.UnionWkt(layer2Wkts);
        var overlayGeom = GeometryUtil.Wkt2Geometry(overlayWkt);

        var outputLayer = new OguLayer
        {
            Name = Path.GetFileNameWithoutExtension(outputPath),
            GeometryType = GeometryType.POLYGON,
            Wkid = layer1.Wkid
        };

        foreach (var field in layer1.Fields)
        {
            outputLayer.AddField(field.Clone());
        }

        int fid = 0;
        int retained = 0;
        foreach (var feature in layer1.Features)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrEmpty(feature.Wkt))
                continue;

            var featureGeom = GeometryUtil.Wkt2Geometry(feature.Wkt);
            var intersectionGeom = GeometryUtil.Intersection(featureGeom, overlayGeom);

            if (!GeometryUtil.IsEmpty(intersectionGeom))
            {
                var newFeature = feature.Clone();
                newFeature.Fid = fid++;
                newFeature.Wkt = GeometryUtil.Geometry2Wkt(intersectionGeom);
                outputLayer.AddFeature(newFeature);
                retained++;
            }
        }

        progress?.Report(L($"Writing {outputLayer.GetFeatureCount()} features...",
            $"正在写入 {outputLayer.GetFeatureCount()} 个要素..."));
        var outputFormat = DetectFormat(outputPath);
        await Task.Run(() => OguLayerUtil.WriteLayer(outputFormat, outputLayer, outputPath), ct);

        return new ToolResult
        {
            Success = true,
            Message = L($"Intersection completed. {retained} of {layer1.GetFeatureCount()} features retained.",
                $"交集计算完成，保留了 {layer1.GetFeatureCount()} 个要素中的 {retained} 个。"),
            OutputPath = outputPath
        };
    }
}
