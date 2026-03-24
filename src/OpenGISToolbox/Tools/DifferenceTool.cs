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
/// Computes the geometric difference of two vector layers (A minus B).
/// </summary>
public class DifferenceTool : ToolBase
{
    private const string FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg";

    public override string Id => "difference";
    public override string Name => "Difference";
    public override string NameZh => "差集";
    public override string Description => "Compute the geometric difference of two vector layers (A minus B)";
    public override string DescriptionZh => "计算两个矢量图层的几何差集（A 减去 B）";
    public override ToolCategory Category => ToolCategory.Geometry;

    public override List<ToolParameter> BuildParameters()
    {
        return new List<ToolParameter>
        {
            new ToolParameter
            {
                Name = "input1",
                Label = "Input File (A)",
                LabelZh = "输入文件 (A)",
                Description = L("Input vector file (A)", "输入矢量文件 (A)"),
                Type = ParameterType.InputFile,
                Required = true,
                FileFilter = FileFilter
            },
            new ToolParameter
            {
                Name = "input2",
                Label = "Erase File (B)",
                LabelZh = "擦除文件 (B)",
                Description = L("Erase vector file (B)", "擦除矢量文件 (B)"),
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

        progress?.Report(L($"Read {layer1.GetFeatureCount()} + {layer2.GetFeatureCount()} features. Computing difference...",
            $"已读取 {layer1.GetFeatureCount()} + {layer2.GetFeatureCount()} 个要素，正在计算差集..."));

        // Union all layer2 geometries into a single erase geometry
        var layer2Wkts = new List<string>();
        foreach (var feature in layer2.Features)
        {
            ct.ThrowIfCancellationRequested();
            if (!string.IsNullOrEmpty(feature.Wkt))
                layer2Wkts.Add(feature.Wkt);
        }

        var eraseWkt = GeometryUtil.UnionWkt(layer2Wkts);
        var eraseGeom = GeometryUtil.Wkt2Geometry(eraseWkt);

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
            var differenceGeom = GeometryUtil.Difference(featureGeom, eraseGeom);

            if (!GeometryUtil.IsEmpty(differenceGeom))
            {
                var newFeature = feature.Clone();
                newFeature.Fid = fid++;
                newFeature.Wkt = GeometryUtil.Geometry2Wkt(differenceGeom);
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
            Message = L($"Difference completed. {retained} of {layer1.GetFeatureCount()} features retained.",
                $"差集计算完成，保留了 {layer1.GetFeatureCount()} 个要素中的 {retained} 个。"),
            OutputPath = outputPath
        };
    }
}
