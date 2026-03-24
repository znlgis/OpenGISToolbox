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
/// Joins attributes from one layer to another based on spatial relationship.
/// </summary>
public class SpatialJoinTool : ToolBase
{
    private const string InputFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg";
    private const string OutputFilter = "Shapefile|*.shp";

    public override string Id => "spatial-join";
    public override string Name => "Spatial Join";
    public override string NameZh => "空间连接";
    public override string Description => "Join attributes from one layer to another based on spatial relationship";
    public override string DescriptionZh => "基于空间关系将一个图层的属性连接到另一个图层";
    public override ToolCategory Category => ToolCategory.Geometry;

    public override List<ToolParameter> BuildParameters()
    {
        return new List<ToolParameter>
        {
            new ToolParameter
            {
                Name = "input",
                Label = "Target Layer",
                LabelZh = "目标图层",
                Description = L("Target vector layer", "目标矢量图层"),
                Type = ParameterType.InputFile,
                Required = true,
                FileFilter = InputFilter
            },
            new ToolParameter
            {
                Name = "joinLayer",
                Label = "Join Layer",
                LabelZh = "连接图层",
                Description = L("Layer to join attributes from", "要连接属性的图层"),
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
            },
            new ToolParameter
            {
                Name = "joinType",
                Label = "Spatial Relationship",
                LabelZh = "空间关系",
                Description = L("Spatial relationship for joining", "用于连接的空间关系"),
                Type = ParameterType.Dropdown,
                Required = true,
                Options = new[] { "Intersects", "Contains", "Within" },
                DefaultValue = "Intersects"
            }
        };
    }

    protected override async Task<ToolResult> ExecuteCoreAsync(
        Dictionary<string, string> parameters,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var inputPath = GetRequired(parameters, "input");
        var joinPath = GetRequired(parameters, "joinLayer");
        var outputPath = GetRequired(parameters, "output");
        var joinType = GetOptional(parameters, "joinType", "Intersects");

        progress?.Report(L("Reading target layer...", "读取目标图层..."));
        var inputFormat = DetectFormat(inputPath);
        var targetLayer = await Task.Run(() => OguLayerUtil.ReadLayer(inputFormat, inputPath), ct);

        progress?.Report(L("Reading join layer...", "读取连接图层..."));
        var joinFormat = DetectFormat(joinPath);
        var joinLayer = await Task.Run(() => OguLayerUtil.ReadLayer(joinFormat, joinPath), ct);

        progress?.Report(L(
            $"Performing spatial join ({joinType}) on {targetLayer.GetFeatureCount()} features...",
            $"正在对 {targetLayer.GetFeatureCount()} 个要素执行空间连接（{joinType}）..."));

        var outputLayer = new OguLayer
        {
            Name = Path.GetFileNameWithoutExtension(outputPath),
            GeometryType = targetLayer.GeometryType,
            Wkid = targetLayer.Wkid
        };

        // Add fields from target layer
        var fieldNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in targetLayer.Fields)
        {
            outputLayer.AddField(field.Clone());
            fieldNames.Add(field.Name);
        }

        // Add non-duplicate fields from join layer
        var joinFieldNames = new List<string>();
        foreach (var field in joinLayer.Fields)
        {
            if (!fieldNames.Contains(field.Name))
            {
                outputLayer.AddField(field.Clone());
                fieldNames.Add(field.Name);
                joinFieldNames.Add(field.Name);
            }
        }

        int fid = 0;
        int joinedCount = 0;
        foreach (var targetFeature in targetLayer.Features)
        {
            ct.ThrowIfCancellationRequested();

            var outFeature = targetFeature.Clone();
            outFeature.Fid = fid++;

            if (!string.IsNullOrEmpty(targetFeature.Wkt))
            {
                foreach (var joinFeature in joinLayer.Features)
                {
                    ct.ThrowIfCancellationRequested();

                    if (string.IsNullOrEmpty(joinFeature.Wkt))
                        continue;

                    bool matches;
                    switch (joinType)
                    {
                        case "Contains":
                            matches = GeometryUtil.ContainsWkt(targetFeature.Wkt, joinFeature.Wkt);
                            break;
                        case "Within":
                            matches = GeometryUtil.ContainsWkt(joinFeature.Wkt, targetFeature.Wkt);
                            break;
                        default: // "Intersects"
                            matches = GeometryUtil.IntersectsWkt(targetFeature.Wkt, joinFeature.Wkt);
                            break;
                    }

                    if (matches)
                    {
                        // Copy non-duplicate fields from the first matching join feature
                        foreach (var fname in joinFieldNames)
                        {
                            var val = joinFeature.GetValue(fname);
                            if (val != null)
                                outFeature.SetValue(fname, val);
                        }
                        joinedCount++;
                        break;
                    }
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
                $"Spatial join completed. {joinedCount} of {targetLayer.GetFeatureCount()} features joined.",
                $"空间连接完成，{joinedCount}/{targetLayer.GetFeatureCount()} 个要素已连接。"),
            OutputPath = outputPath
        };
    }
}
