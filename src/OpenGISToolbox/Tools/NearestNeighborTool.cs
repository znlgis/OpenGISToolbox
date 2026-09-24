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
using OpenGIS.Utils.Geometry;
using OpenGISToolbox.Models;

namespace OpenGISToolbox.Tools;

/// <summary>
/// Nearest-neighbour join: for every feature of the target layer, finds the
/// closest feature of the reference layer and appends its id (nn_fid) and the
/// distance (nn_dist, in the layer CRS units). Distances use GEOS minimum
/// geometry distance; a bounding-box lower-bound prefilter keeps real layers fast.
/// </summary>
public class NearestNeighborTool : ToolBase
{
    private const string FileFilter = "Shapefile|*.shp|GeoJSON|*.geojson|GeoPackage|*.gpkg";
    private const string FidField = "nn_fid";
    private const string DistField = "nn_dist";

    public override string Id => "nearest-neighbor";
    public override string Name => "Nearest Neighbor";
    public override string NameZh => "最近邻连接";
    public override string Description => "Append each feature's nearest reference feature id and distance";
    public override string DescriptionZh => "为每个要素追加其在参考图层中最近邻要素的编号与距离";
    public override ToolCategory Category => ToolCategory.Analysis;

    public override List<ToolParameter> BuildParameters() => new()
    {
        new ToolParameter
        {
            Name = "input", Label = "Target Layer", LabelZh = "目标图层",
            Description = L("Features to find the nearest neighbour for", "需要求最近邻的要素图层"),
            Type = ParameterType.InputFile, Required = true, FileFilter = FileFilter
        },
        new ToolParameter
        {
            Name = "reference", Label = "Reference Layer", LabelZh = "参考图层",
            Description = L("Layer searched for the nearest feature", "用于搜索最近要素的图层"),
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
        var inputPath = GetRequired(parameters, "input");
        var refPath = GetRequired(parameters, "reference");
        var outputPath = GetRequired(parameters, "output");

        var target = await Task.Run(() => OguLayerUtil.ReadLayer(DetectFormat(inputPath), inputPath), ct);
        var reference = await Task.Run(() => OguLayerUtil.ReadLayer(DetectFormat(refPath), refPath), ct);

        progress?.Report(L($"Indexing {reference.GetFeatureCount()} reference features...",
            $"正在索引 {reference.GetFeatureCount()} 个参考要素..."));

        var refEntries = new List<(int Fid, OSGeo.OGR.Geometry Geom, double[] Bounds)>();
        foreach (var rf in reference.Features)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(rf.Wkt)) continue;
            var g = GeometryUtil.Wkt2Geometry(rf.Wkt);
            var b = GeometryOps.BoundsOfWkt(rf.Wkt);
            if (g == null || b == null) { g?.Dispose(); continue; }
            refEntries.Add((rf.Fid, g, b));
        }

        var outputLayer = new OguLayer
        {
            Name = Path.GetFileNameWithoutExtension(outputPath),
            GeometryType = target.GeometryType,
            Wkid = target.Wkid
        };
        foreach (var field in target.Fields)
            outputLayer.AddField(field.Clone());
        outputLayer.AddField(new OguField { Name = FidField, DataType = FieldDataType.INTEGER });
        outputLayer.AddField(new OguField { Name = DistField, DataType = FieldDataType.DOUBLE });

        int found = 0;
        foreach (var feature in target.Features)
        {
            ct.ThrowIfCancellationRequested();

            var outFeature = feature.Clone();
            outFeature.SetValue(FidField, -1);
            outFeature.SetValue(DistField, 0d);

            if (!string.IsNullOrWhiteSpace(feature.Wkt) && refEntries.Count > 0)
            {
                var tg = GeometryUtil.Wkt2Geometry(feature.Wkt);
                var tb = GeometryOps.BoundsOfWkt(feature.Wkt);
                if (tg != null && tb != null)
                {
                    double best = double.MaxValue;
                    int bestFid = -1;
                    try
                    {
                        foreach (var (fid, geom, bounds) in refEntries)
                        {
                            var gap = BoundsGap(tb, bounds);
                            if (gap > best) continue; // cannot beat current best
                            var d = GeometryUtil.Distance(tg, geom);
                            if (d < best)
                            {
                                best = d;
                                bestFid = fid;
                            }
                        }
                    }
                    finally
                    {
                        tg.Dispose();
                    }

                    if (bestFid >= 0)
                    {
                        outFeature.SetValue(FidField, bestFid);
                        outFeature.SetValue(DistField, best);
                        found++;
                    }
                }
                else tg?.Dispose();
            }

            outputLayer.AddFeature(outFeature);
        }

        foreach (var e in refEntries) e.Geom.Dispose();

        var outputFormat = DetectFormat(outputPath);
        await Task.Run(() => WriteLayerSafe(outputFormat, outputLayer, outputPath, progress), ct);

        return new ToolResult
        {
            Success = true,
            Message = L(
                $"Nearest neighbour join completed. {found} of {target.GetFeatureCount()} features matched (distance in CRS units).",
                $"最近邻连接完成，{target.GetFeatureCount()} 个要素中匹配 {found} 个（距离为坐标系单位）。"),
            OutputPath = outputPath
        };
    }

    /// <summary>Minimum separation between two axis-aligned boxes (0 when they overlap).</summary>
    private static double BoundsGap(double[] a, double[] b)
    {
        double dx = Math.Max(0, Math.Max(a[0] - b[2], b[0] - a[2]));
        double dy = Math.Max(0, Math.Max(a[1] - b[3], b[1] - a[3]));
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
