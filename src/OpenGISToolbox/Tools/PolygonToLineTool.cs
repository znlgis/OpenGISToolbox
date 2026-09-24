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
/// Converts polygon boundaries into line features (Polygon to Line). Each output
/// line follows a ring of the source polygon; a polygon with holes yields a
/// MULTILINESTRING of its outer and inner rings.
/// </summary>
public class PolygonToLineTool : ToolBase
{
    private const string FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg";

    public override string Id => "polygon-to-line";
    public override string Name => "Polygon to Line";
    public override string NameZh => "面转线";
    public override string Description => "Convert polygon boundaries to line features";
    public override string DescriptionZh => "将多边形边界转换为线要素";
    public override ToolCategory Category => ToolCategory.Geometry;

    public override List<ToolParameter> BuildParameters() => new()
    {
        new ToolParameter
        {
            Name = "input", Label = "Input File", LabelZh = "输入文件",
            Description = L("Input polygon vector file", "输入面矢量文件"),
            Type = ParameterType.InputFile, Required = true, FileFilter = FileFilter
        },
        new ToolParameter
        {
            Name = "output", Label = "Output File", LabelZh = "输出文件",
            Description = L("Output line vector file", "输出线矢量文件"),
            Type = ParameterType.OutputFile, Required = true, FileFilter = FileFilter
        }
    };

    protected override async Task<ToolResult> ExecuteCoreAsync(
        Dictionary<string, string> parameters, IProgress<string>? progress, CancellationToken ct)
    {
        var inputPath = GetRequired(parameters, "input");
        var outputPath = GetRequired(parameters, "output");

        var inputFormat = DetectFormat(inputPath);
        var layer = await Task.Run(() => OguLayerUtil.ReadLayer(inputFormat, inputPath), ct);

        var outputLayer = new OguLayer
        {
            Name = Path.GetFileNameWithoutExtension(outputPath),
            GeometryType = GeometryType.MULTILINESTRING,
            Wkid = layer.Wkid
        };
        foreach (var field in layer.Fields)
            outputLayer.AddField(field.Clone());

        int fid = 0;
        int converted = 0;
        foreach (var feature in layer.Features)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(feature.Wkt)) continue;

            var geom = GeometryUtil.Wkt2Geometry(feature.Wkt);
            if (geom == null) continue;
            string boundaryWkt;
            try
            {
                var boundary = GeometryUtil.Boundary(geom);
                boundaryWkt = GeometryUtil.Geometry2Wkt(boundary);
            }
            finally
            {
                geom.Dispose();
            }
            if (string.IsNullOrWhiteSpace(boundaryWkt)) continue;

            var outFeature = feature.Clone();
            outFeature.Fid = fid++;
            outFeature.Wkt = boundaryWkt;
            outputLayer.AddFeature(outFeature);
            converted++;
        }

        var outputFormat = DetectFormat(outputPath);
        await Task.Run(() => WriteLayerSafe(outputFormat, outputLayer, outputPath, progress), ct);

        return new ToolResult
        {
            Success = true,
            Message = L(
                $"Converted {converted} polygons to boundary lines.",
                $"已将 {converted} 个多边形转换为边界线。"),
            OutputPath = outputPath
        };
    }
}
