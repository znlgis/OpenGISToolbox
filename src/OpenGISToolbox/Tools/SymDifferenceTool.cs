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
/// Computes the symmetric difference of two vector layers: the region covered by
/// exactly one of the layers (A∪B minus A∩B). Engine SymDifference was available
/// but never surfaced as a tool.
/// </summary>
public class SymDifferenceTool : ToolBase
{
    private const string FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg";

    public override string Id => "sym-difference";
    public override string Name => "Symmetric Difference";
    public override string NameZh => "对称差";
    public override string Description => "Compute the region covered by exactly one of two layers (A Δ B)";
    public override string DescriptionZh => "计算仅被两个图层之一覆盖的区域（对称差 A Δ B）";
    public override ToolCategory Category => ToolCategory.Geometry;

    public override List<ToolParameter> BuildParameters() => new()
    {
        new ToolParameter
        {
            Name = "input1", Label = "Input File 1", LabelZh = "输入文件 1",
            Description = L("First input vector file", "第一个输入矢量文件"),
            Type = ParameterType.InputFile, Required = true, FileFilter = FileFilter
        },
        new ToolParameter
        {
            Name = "input2", Label = "Input File 2", LabelZh = "输入文件 2",
            Description = L("Second input vector file", "第二个输入矢量文件"),
            Type = ParameterType.InputFile, Required = true, FileFilter = FileFilter
        },
        new ToolParameter
        {
            Name = "output", Label = "Output File", LabelZh = "输出文件",
            Description = L("Output vector file", "输出矢量文件"),
            Type = ParameterType.OutputFile, Required = true, FileFilter = FileFilter
        }
    };

    protected override async Task<ToolResult> ExecuteCoreAsync(
        Dictionary<string, string> parameters, IProgress<string>? progress, CancellationToken ct)
    {
        var input1Path = GetRequired(parameters, "input1");
        var input2Path = GetRequired(parameters, "input2");
        var outputPath = GetRequired(parameters, "output");

        var layer1 = await Task.Run(() => OguLayerUtil.ReadLayer(DetectFormat(input1Path), input1Path), ct);
        var layer2 = await Task.Run(() => OguLayerUtil.ReadLayer(DetectFormat(input2Path), input2Path), ct);

        progress?.Report(L("Computing symmetric difference...", "正在计算对称差..."));

        var wkts1 = layer1.Features.Where(f => !string.IsNullOrWhiteSpace(f.Wkt)).Select(f => f.Wkt!).ToList();
        var wkts2 = layer2.Features.Where(f => !string.IsNullOrWhiteSpace(f.Wkt)).Select(f => f.Wkt!).ToList();
        if (wkts1.Count == 0 && wkts2.Count == 0)
            throw new ArgumentException(L("Both input layers are empty.", "两个输入图层均为空。"));

        string SymDiffOf(params List<string> wkts)
        {
            if (wkts.Count == 0) return "GEOMETRYCOLLECTION EMPTY";
            return GeometryUtil.UnionWkt(wkts);
        }

        var geomA = GeometryUtil.Wkt2Geometry(SymDiffOf(wkts1));
        var geomB = GeometryUtil.Wkt2Geometry(SymDiffOf(wkts2));
        string resultWkt;
        try
        {
            var sym = GeometryUtil.SymDifference(geomA, geomB);
            resultWkt = GeometryUtil.Geometry2Wkt(sym);
        }
        finally
        {
            geomA?.Dispose();
            geomB?.Dispose();
        }

        var outputLayer = new OguLayer
        {
            Name = Path.GetFileNameWithoutExtension(outputPath),
            GeometryType = GeometryType.MULTIPOLYGON,
            Wkid = layer1.Wkid ?? layer2.Wkid
        };
        outputLayer.AddField(new OguField { Name = "result", DataType = FieldDataType.STRING, Length = 20 });
        var resultFeature = new OguFeature
        {
            Fid = 0,
            Wkt = string.IsNullOrWhiteSpace(resultWkt) ? "GEOMETRYCOLLECTION EMPTY" : resultWkt
        };
        resultFeature.SetValue("result", "AB_symdiff");
        outputLayer.AddFeature(resultFeature);

        var outputFormat = DetectFormat(outputPath);
        await Task.Run(() => WriteLayerSafe(outputFormat, outputLayer, outputPath, progress), ct);

        var area = string.IsNullOrWhiteSpace(resultWkt) ? 0 : GeometryUtil.AreaWkt(resultWkt);
        return new ToolResult
        {
            Success = true,
            Message = L(
                $"Symmetric difference completed. Result area = {area:F6}.",
                $"对称差计算完成，结果面积 = {area:F6}。"),
            OutputPath = outputPath
        };
    }
}
