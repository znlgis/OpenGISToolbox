using System;
using System.Collections.Generic;
using System.Globalization;
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
/// Builds a Delaunay triangulation from a point layer (Bowyer–Watson, managed
/// code) and writes one polygon per triangle with a "tri_id" attribute.
/// </summary>
public class DelaunayTool : ToolBase
{
    private const string InputFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg";

    public override string Id => "delaunay";
    public override string Name => "Delaunay Triangulation";
    public override string NameZh => "Delaunay 三角网";
    public override string Description => "Build a Delaunay triangle mesh from a point layer";
    public override string DescriptionZh => "由点图层构建 Delaunay 三角网";
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
        foreach (var feature in layer.Features)
        {
            if (string.IsNullOrWhiteSpace(feature.Wkt)) continue;
            var coords = GeometryOps.ExtractCoordinates(feature.Wkt);
            if (coords.Count == 0) continue;
            sites.Add(new TriangulationOps.Pt(coords[0].X, coords[0].Y));
        }

        if (sites.Count < 3)
            throw new ArgumentException(L("At least 3 points are required.", "至少需要 3 个点。"));

        progress?.Report(L($"Triangulating {sites.Count} points...", $"正在对 {sites.Count} 个点进行三角剖分..."));
        var triangles = TriangulationOps.Delaunay(sites);

        var outputLayer = new OguLayer
        {
            Name = Path.GetFileNameWithoutExtension(outputPath),
            GeometryType = GeometryType.POLYGON,
            Wkid = layer.Wkid
        };
        outputLayer.AddField(new OguField { Name = "tri_id", DataType = FieldDataType.INTEGER });

        string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);
        int fid = 0;
        foreach (var t in triangles)
        {
            ct.ThrowIfCancellationRequested();
            var a = sites[t.A]; var b = sites[t.B]; var c = sites[t.C];
            var wkt = $"POLYGON (({F(a.X)} {F(a.Y)},{F(b.X)} {F(b.Y)},{F(c.X)} {F(c.Y)},{F(a.X)} {F(a.Y)}))";
            var triFeature = new OguFeature { Fid = fid, Wkt = wkt };
            triFeature.SetValue("tri_id", fid);
            outputLayer.AddFeature(triFeature);
            fid++;
        }

        await Task.Run(() => WriteLayerSafe(DetectFormat(outputPath), outputLayer, outputPath, progress), ct);

        return new ToolResult
        {
            Success = true,
            Message = L(
                $"Delaunay triangulation completed. {triangles.Count} triangles from {sites.Count} points.",
                $"Delaunay 三角网完成，{sites.Count} 个点生成 {triangles.Count} 个三角形。"),
            OutputPath = outputPath
        };
    }
}
