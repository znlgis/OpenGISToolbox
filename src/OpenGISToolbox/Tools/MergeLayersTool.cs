using System;
using System.Collections.Generic;
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
/// Merges two vector layers into a single output layer.
/// </summary>
public class MergeLayersTool : ToolBase
{
    private const string InputFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg|KML|*.kml";
    private const string OutputFilter = "Shapefile|*.shp";

    public override string Id => "merge-layers";
    public override string Name => "Merge Layers";
    public override string NameZh => "合并图层";
    public override string Description => "Merge two vector layers into one";
    public override string DescriptionZh => "将两个矢量图层合并为一个";
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
                FileFilter = InputFilter
            },
            new ToolParameter
            {
                Name = "input2",
                Label = "Input File 2",
                LabelZh = "输入文件 2",
                Description = L("Second input vector file", "第二个输入矢量文件"),
                Type = ParameterType.InputFile,
                Required = true,
                FileFilter = InputFilter
            },
            new ToolParameter
            {
                Name = "output",
                Label = "Output File",
                LabelZh = "输出文件",
                Description = L("Output vector file", "输出矢量文件"),
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
        var input1Path = GetRequired(parameters, "input1");
        var input2Path = GetRequired(parameters, "input2");
        var outputPath = GetRequired(parameters, "output");

        progress?.Report(L("Reading first input file...", "读取第一个输入文件..."));
        var format1 = DetectFormat(input1Path);
        var layer1 = await Task.Run(() => OguLayerUtil.ReadLayer(format1, input1Path), ct);

        progress?.Report(L("Reading second input file...", "读取第二个输入文件..."));
        var format2 = DetectFormat(input2Path);
        var layer2 = await Task.Run(() => OguLayerUtil.ReadLayer(format2, input2Path), ct);

        progress?.Report(L(
            $"Read {layer1.GetFeatureCount()} + {layer2.GetFeatureCount()} features. Merging...",
            $"已读取 {layer1.GetFeatureCount()} + {layer2.GetFeatureCount()} 个要素，正在合并..."));

        var mergedLayer = new OguLayer
        {
            Name = Path.GetFileNameWithoutExtension(outputPath),
            GeometryType = layer1.GeometryType,
            Wkid = layer1.Wkid
        };

        // Add fields from layer1
        var fieldNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in layer1.Fields)
        {
            mergedLayer.AddField(field.Clone());
            fieldNames.Add(field.Name);
        }

        // Add fields from layer2 (dedup by name)
        foreach (var field in layer2.Fields)
        {
            if (!fieldNames.Contains(field.Name))
            {
                mergedLayer.AddField(field.Clone());
                fieldNames.Add(field.Name);
            }
        }

        // Clone features from both layers
        int fid = 0;
        foreach (var feature in layer1.Features)
        {
            ct.ThrowIfCancellationRequested();
            var cloned = feature.Clone();
            cloned.Fid = fid++;
            mergedLayer.AddFeature(cloned);
        }

        foreach (var feature in layer2.Features)
        {
            ct.ThrowIfCancellationRequested();
            var cloned = feature.Clone();
            cloned.Fid = fid++;
            mergedLayer.AddFeature(cloned);
        }

        progress?.Report(L($"Writing {mergedLayer.GetFeatureCount()} features...",
            $"正在写入 {mergedLayer.GetFeatureCount()} 个要素..."));
        await Task.Run(() => OguLayerUtil.WriteLayer(DataFormatType.SHP, mergedLayer, outputPath), ct);

        return new ToolResult
        {
            Success = true,
            Message = L(
                $"Merge completed. {mergedLayer.GetFeatureCount()} features written ({layer1.GetFeatureCount()} + {layer2.GetFeatureCount()}).",
                $"合并完成，共写入 {mergedLayer.GetFeatureCount()} 个要素（{layer1.GetFeatureCount()} + {layer2.GetFeatureCount()}）。"),
            OutputPath = outputPath
        };
    }
}
