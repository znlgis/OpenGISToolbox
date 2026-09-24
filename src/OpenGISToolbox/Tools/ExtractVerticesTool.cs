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
/// Extracts every vertex of a layer as a point feature, tagging each with its
/// source feature id and vertex index. Vertices are read straight from the WKT so
/// ring-closing repeats are emitted too (faithful to the source geometry).
/// </summary>
public class ExtractVerticesTool : ToolBase
{
    private const string FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg";
    private const string SrcField = "src_fid";
    private const string IdxField = "vertex_idx";

    public override string Id => "extract-vertices";
    public override string Name => "Extract Vertices";
    public override string NameZh => "提取顶点";
    public override string Description => "Extract all vertices of a layer as point features";
    public override string DescriptionZh => "将图层的所有顶点提取为点要素";
    public override ToolCategory Category => ToolCategory.Geometry;

    public override List<ToolParameter> BuildParameters() => new()
    {
        new ToolParameter
        {
            Name = "input", Label = "Input File", LabelZh = "输入文件",
            Description = L("Input vector file", "输入矢量文件"),
            Type = ParameterType.InputFile, Required = true, FileFilter = FileFilter
        },
        new ToolParameter
        {
            Name = "output", Label = "Output File", LabelZh = "输出文件",
            Description = L("Output point vector file", "输出点矢量文件"),
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

        progress?.Report(L($"Read {layer.GetFeatureCount()} features. Extracting vertices...",
            $"已读取 {layer.GetFeatureCount()} 个要素，正在提取顶点..."));

        var outputLayer = new OguLayer
        {
            Name = Path.GetFileNameWithoutExtension(outputPath),
            GeometryType = GeometryType.POINT,
            Wkid = layer.Wkid
        };
        foreach (var field in layer.Fields)
            outputLayer.AddField(field.Clone());
        outputLayer.AddField(new OguField { Name = SrcField, DataType = FieldDataType.INTEGER });
        outputLayer.AddField(new OguField { Name = IdxField, DataType = FieldDataType.INTEGER });

        int fid = 0;
        int vertexTotal = 0;
        foreach (var feature in layer.Features)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var part in GeometryOps.ExplodeParts(feature.Wkt))
            {
                var coords = GeometryOps.ExtractCoordinates(part);
                for (var i = 0; i < coords.Count; i++)
                {
                    var p = new OguFeature
                    {
                        Fid = fid,
                        Wkt = string.Format(CultureInfo.InvariantCulture,
                            "POINT ({0} {1})", coords[i].X, coords[i].Y)
                    };
                    foreach (var (name, value) in feature.Attributes)
                    {
                        if (value?.Value != null) p.SetValue(name, value.Value);
                    }
                    p.SetValue(SrcField, feature.Fid);
                    p.SetValue(IdxField, i);
                    outputLayer.AddFeature(p);
                    vertexTotal++;
                    fid++;
                }
            }
        }

        var outputFormat = DetectFormat(outputPath);
        await Task.Run(() => WriteLayerSafe(outputFormat, outputLayer, outputPath, progress), ct);

        return new ToolResult
        {
            Success = true,
            Message = L(
                $"Extracted {vertexTotal} vertices from {layer.GetFeatureCount()} features.",
                $"已从 {layer.GetFeatureCount()} 个要素提取 {vertexTotal} 个顶点。"),
            OutputPath = outputPath
        };
    }
}
