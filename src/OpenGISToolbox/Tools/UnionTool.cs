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
/// Computes the geometric union of two vector layers.
/// </summary>
public class UnionTool : ToolBase
{
    private const string FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg";

    public override string Id => "union";
    public override string Name => "Union";
    public override string NameZh => "合并";
    public override string Description => "Compute the geometric union of two vector layers";
    public override string DescriptionZh => "计算两个矢量图层的几何并集";
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
                Description = L("Second input vector file", "第二个输入矢量文件"),
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

        progress?.Report(L($"Read {layer1.GetFeatureCount()} + {layer2.GetFeatureCount()} features. Computing union...",
            $"已读取 {layer1.GetFeatureCount()} + {layer2.GetFeatureCount()} 个要素，正在计算并集..."));

        var allWkts = new List<string>();
        foreach (var feature in layer1.Features)
        {
            ct.ThrowIfCancellationRequested();
            if (!string.IsNullOrEmpty(feature.Wkt))
                allWkts.Add(feature.Wkt);
        }
        foreach (var feature in layer2.Features)
        {
            ct.ThrowIfCancellationRequested();
            if (!string.IsNullOrEmpty(feature.Wkt))
                allWkts.Add(feature.Wkt);
        }

        var unionWkt = GeometryUtil.UnionWkt(allWkts);

        var outputLayer = new OguLayer
        {
            Name = Path.GetFileNameWithoutExtension(outputPath),
            GeometryType = GeometryType.POLYGON,
            Wkid = layer1.Wkid
        };

        var unionFeature = new OguFeature
        {
            Fid = 0,
            Wkt = unionWkt
        };
        outputLayer.AddFeature(unionFeature);

        progress?.Report(L("Writing output...", "正在写入输出..."));
        var outputFormat = DetectFormat(outputPath);
        await Task.Run(() => OguLayerUtil.WriteLayer(outputFormat, outputLayer, outputPath), ct);

        return new ToolResult
        {
            Success = true,
            Message = L($"Union completed. Merged {allWkts.Count} geometries into 1 feature.",
                $"合并完成，共将 {allWkts.Count} 个几何体合并为 1 个要素。"),
            OutputPath = outputPath
        };
    }
}
