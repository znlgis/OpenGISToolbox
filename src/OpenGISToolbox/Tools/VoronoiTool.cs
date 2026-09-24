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
/// Builds a Voronoi (Thiessen) diagram from a point layer: one polygon per input
/// site, clipped to the sites' bounding box so the cells tile the extent with no
/// overlaps. Site attributes are carried onto the corresponding cell. Implemented
/// in managed code (see <see cref="TriangulationOps"/>) — no engine release needed.
/// </summary>
public class VoronoiTool : ToolBase
{
    private const string InputFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg";

    public override string Id => "voronoi";
    public override string Name => "Voronoi Diagram";
    public override string NameZh => "泰森多边形";
    public override string Description => "Build Voronoi polygons from a point layer, clipped to the site extent";
    public override string DescriptionZh => "由点图层生成泰森多边形，裁剪至点位范围";
    public override ToolCategory Category => ToolCategory.Geometry;

    public override List<ToolParameter> BuildParameters() => new()
    {
        new ToolParameter
        {
            Name = "input", Label = "Input Points", LabelZh = "输入点",
            Description = L("Input point vector file", "输入点矢量文件"),
            Type = ParameterType.InputFile, Required = true, FileFilter = InputFilter
        },
        new ToolParameter
        {
            Name = "output", Label = "Output File", LabelZh = "输出文件",
            Description = L("Output polygon vector file", "输出面矢量文件"),
            Type = ParameterType.OutputFile, Required = true, FileFilter = InputFilter
        }
    };

    protected override async Task<ToolResult> ExecuteCoreAsync(
        Dictionary<string, string> parameters, IProgress<string>? progress, CancellationToken ct)
    {
        var inputPath = GetRequired(parameters, "input");
        var outputPath = GetRequired(parameters, "output");

        var layer = await Task.Run(() => OguLayerUtil.ReadLayer(DetectFormat(inputPath), inputPath), ct);

        var sites = new List<TriangulationOps.Pt>();
        var features = new List<OguFeature>();
        foreach (var feature in layer.Features)
        {
            if (string.IsNullOrWhiteSpace(feature.Wkt)) continue;
            var xy = GeometryOps.ExtractCoordinates(feature.Wkt).FirstOrDefault();
            // FirstPoint on an empty scan yields (0,0); guard by requiring a real coord.
            if (GeometryOps.ExtractCoordinates(feature.Wkt).Count == 0) continue;
            sites.Add(new TriangulationOps.Pt(xy.X, xy.Y));
            features.Add(feature);
        }

        if (sites.Count < 1)
            throw new ArgumentException(L("No point features found in the input.", "输入中没有找到点要素。"));

        double minx = sites.Min(s => s.X), maxx = sites.Max(s => s.X);
        double miny = sites.Min(s => s.Y), maxy = sites.Max(s => s.Y);
        // Degenerate single-line/point extents get a tiny pad so cells remain valid.
        if (maxx - minx <= 0) { minx -= 0.5; maxx += 0.5; }
        if (maxy - miny <= 0) { miny -= 0.5; maxy += 0.5; }

        progress?.Report(L($"Computing {sites.Count} Voronoi cells...", $"正在计算 {sites.Count} 个泰森胞元..."));
        var cells = TriangulationOps.VoronoiCells(sites, minx, miny, maxx, maxy);

        var outputLayer = new OguLayer
        {
            Name = Path.GetFileNameWithoutExtension(outputPath),
            GeometryType = GeometryType.POLYGON,
            Wkid = layer.Wkid
        };
        foreach (var field in layer.Fields)
            outputLayer.AddField(field.Clone());
        outputLayer.AddField(new OguField { Name = "cell_id", DataType = FieldDataType.INTEGER });

        int fid = 0;
        foreach (var (siteIndex, _, wkt) in cells)
        {
            ct.ThrowIfCancellationRequested();
            var src = features[siteIndex];
            var outFeature = src.Clone();
            outFeature.Fid = fid;
            outFeature.Wkt = wkt;
            outFeature.SetValue("cell_id", siteIndex);
            outputLayer.AddFeature(outFeature);
            fid++;
        }

        await Task.Run(() => WriteLayerSafe(DetectFormat(outputPath), outputLayer, outputPath, progress), ct);

        return new ToolResult
        {
            Success = true,
            Message = L(
                $"Voronoi diagram completed. {outputLayer.GetFeatureCount()} cells from {sites.Count} sites.",
                $"泰森多边形生成完成，{sites.Count} 个点产生 {outputLayer.GetFeatureCount()} 个胞元。"),
            OutputPath = outputPath
        };
    }
}
