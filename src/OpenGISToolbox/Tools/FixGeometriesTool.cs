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
/// Fixes invalid geometries in a vector layer using the buffer(0) technique.
/// </summary>
public class FixGeometriesTool : ToolBase
{
    private const string FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg";

    public override string Id => "fix-geometries";
    public override string Name => "Fix Geometries";
    public override string NameZh => "修复几何";
    public override string Description => "Fix invalid geometries using the buffer(0) technique";
    public override string DescriptionZh => "使用 buffer(0) 技术修复无效几何体";
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

        progress?.Report(L($"Read {layer.GetFeatureCount()} features. Checking geometries...",
            $"已读取 {layer.GetFeatureCount()} 个要素，正在检查几何体..."));

        int fixedCount = 0;
        foreach (var feature in layer.Features)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrEmpty(feature.Wkt))
                continue;

            var geom = GeometryUtil.Wkt2Geometry(feature.Wkt);
            var validationResult = GeometryUtil.IsValid(geom);
            if (!validationResult.IsValid)
            {
                var buffered = GeometryUtil.Buffer(geom, 0);
                feature.Wkt = GeometryUtil.Geometry2Wkt(buffered);
                fixedCount++;
            }
        }

        progress?.Report(L($"Writing {layer.GetFeatureCount()} features...",
            $"正在写入 {layer.GetFeatureCount()} 个要素..."));
        var outputFormat = DetectFormat(outputPath);
        await Task.Run(() => OguLayerUtil.WriteLayer(outputFormat, layer, outputPath), ct);

        return new ToolResult
        {
            Success = true,
            Message = L(
                $"Fix geometries completed. {fixedCount} of {layer.GetFeatureCount()} features were fixed.",
                $"几何修复完成，共修复 {fixedCount}/{layer.GetFeatureCount()} 个要素。"),
            OutputPath = outputPath
        };
    }
}
