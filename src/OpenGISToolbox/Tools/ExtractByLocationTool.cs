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
/// Extracts features from a target layer whose spatial relationship to any
/// feature of a reference layer satisfies a chosen predicate (Intersects /
/// Contains / Within / Disjoint). Disjoint keeps targets that touch none of the
/// references. An empty result is a legitimate, successful outcome.
/// </summary>
public class ExtractByLocationTool : ToolBase
{
    private const string FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg";

    public override string Id => "extract-by-location";
    public override string Name => "Extract by Location";
    public override string NameZh => "按位置提取";
    public override string Description => "Select features from a target layer by their spatial relationship to a reference layer";
    public override string DescriptionZh => "按目标图层要素与参考图层的空间关系进行筛选提取";
    public override ToolCategory Category => ToolCategory.Analysis;

    public override List<ToolParameter> BuildParameters() => new()
    {
        new ToolParameter
        {
            Name = "input", Label = "Target Layer", LabelZh = "目标图层",
            Description = L("Layer to select features from", "要从中筛选要素的图层"),
            Type = ParameterType.InputFile, Required = true, FileFilter = FileFilter
        },
        new ToolParameter
        {
            Name = "reference", Label = "Reference Layer", LabelZh = "参考图层",
            Description = L("Reference layer defining the spatial predicate", "定义空间关系的参考图层"),
            Type = ParameterType.InputFile, Required = true, FileFilter = FileFilter
        },
        new ToolParameter
        {
            Name = "output", Label = "Output File", LabelZh = "输出文件",
            Description = L("Output vector file", "输出矢量文件"),
            Type = ParameterType.OutputFile, Required = true, FileFilter = FileFilter
        },
        new ToolParameter
        {
            Name = "predicate", Label = "Spatial Predicate", LabelZh = "空间谓词",
            Description = L("Relationship of target to reference for selection", "目标相对参考满足该关系则选中"),
            Type = ParameterType.Dropdown, Required = true, DefaultValue = "Intersects",
            Options = new[] { "Intersects", "Contains", "Within", "Disjoint" }
        }
    };

    protected override async Task<ToolResult> ExecuteCoreAsync(
        Dictionary<string, string> parameters, IProgress<string>? progress, CancellationToken ct)
    {
        var inputPath = GetRequired(parameters, "input");
        var refPath = GetRequired(parameters, "reference");
        var outputPath = GetRequired(parameters, "output");
        var predicate = GetOptional(parameters, "predicate", "Intersects");

        var target = await Task.Run(() => OguLayerUtil.ReadLayer(DetectFormat(inputPath), inputPath), ct);
        var reference = await Task.Run(() => OguLayerUtil.ReadLayer(DetectFormat(refPath), refPath), ct);

        var refWkts = reference.Features.Where(f => !string.IsNullOrWhiteSpace(f.Wkt))
            .Select(f => f.Wkt!).ToList();

        progress?.Report(L(
            $"Testing {target.GetFeatureCount()} targets against {refWkts.Count} reference features ({predicate})...",
            $"正在按 {predicate} 将 {target.GetFeatureCount()} 个目标与 {refWkts.Count} 个参考要素比对..."));

        var outputLayer = new OguLayer
        {
            Name = Path.GetFileNameWithoutExtension(outputPath),
            GeometryType = target.GeometryType,
            Wkid = target.Wkid
        };
        foreach (var field in target.Fields)
            outputLayer.AddField(field.Clone());

        int fid = 0;
        int selected = 0;
        foreach (var feature in target.Features)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(feature.Wkt)) continue;

            var t = feature.Wkt!;
            bool match = predicate switch
            {
                // A target "contains" some reference feature.
                "Contains" => refWkts.Any(r => Safe(() => GeometryUtil.ContainsWkt(t, r))),
                // A target is "within" some reference feature (reference contains target).
                "Within" => refWkts.Any(r => Safe(() => GeometryUtil.ContainsWkt(r, t))),
                // Disjoint = no reference intersects the target.
                "Disjoint" => !refWkts.Any(r => Safe(() => GeometryUtil.IntersectsWkt(t, r))),
                // Default: intersects at least one reference feature.
                _ => refWkts.Any(r => Safe(() => GeometryUtil.IntersectsWkt(t, r)))
            };
            if (!match) continue;

            var cloned = feature.Clone();
            cloned.Fid = fid++;
            outputLayer.AddFeature(cloned);
            selected++;
        }

        var outputFormat = DetectFormat(outputPath);
        await Task.Run(() => WriteLayerSafe(outputFormat, outputLayer, outputPath, progress), ct);

        return new ToolResult
        {
            Success = true,
            Message = L(
                $"Extract by location completed. {selected} of {target.GetFeatureCount()} features selected ({predicate}).",
                $"按位置提取完成，{target.GetFeatureCount()} 个要素中选中 {selected} 个（{predicate}）。"),
            OutputPath = outputPath
        };
    }

    // Predicates can raise on degenerate geometries; treat those as non-matches.
    private static bool Safe(Func<bool> test)
    {
        try { return test(); }
        catch { return false; }
    }
}
