using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenGIS.Utils.DataSource;
using OpenGIS.Utils.Engine.Enums;
using OpenGIS.Utils.Engine.Model.Layer;
using OpenGIS.Utils.Geometry;
using OpenGISToolbox.Models;

namespace OpenGISToolbox.Tools;

/// <summary>
/// Parameterized concrete tool that handles all format conversion scenarios.
/// Each instance is configured with a specific source/target format pair.
/// </summary>
public class FormatConversionTool : ToolBase
{
    private readonly string _id;
    private readonly string _name;
    private readonly string _nameZh;
    private readonly string _description;
    private readonly string _descriptionZh;
    private readonly DataFormatType _sourceFormat;
    private readonly string _sourceExt;
    private readonly string _sourceFilter;
    private readonly DataFormatType _targetFormat;
    private readonly string _targetExt;
    private readonly string _targetFilter;

    public FormatConversionTool(
        string id,
        string name,
        string nameZh,
        string description,
        string descriptionZh,
        DataFormatType sourceFormat,
        string sourceExt,
        string sourceFilter,
        DataFormatType targetFormat,
        string targetExt,
        string targetFilter)
    {
        _id = id;
        _name = name;
        _nameZh = nameZh;
        _description = description;
        _descriptionZh = descriptionZh;
        _sourceFormat = sourceFormat;
        _sourceExt = sourceExt;
        _sourceFilter = sourceFilter;
        _targetFormat = targetFormat;
        _targetExt = targetExt;
        _targetFilter = targetFilter;
    }

    public override string Id => _id;
    public override string Name => _name;
    public override string NameZh => _nameZh;
    public override string Description => _description;
    public override string DescriptionZh => _descriptionZh;
    public override ToolCategory Category => ToolCategory.Conversion;

    public override List<ToolParameter> BuildParameters()
    {
        return new List<ToolParameter>
        {
            new ToolParameter
            {
                Name = "input",
                Label = "Input File",
                LabelZh = "输入文件",
                Description = L("Source file to convert", "要转换的源文件"),
                Type = ParameterType.InputFile,
                Required = true,
                FileFilter = _sourceFilter
            },
            new ToolParameter
            {
                Name = "output",
                Label = "Output File",
                LabelZh = "输出文件",
                Description = L("Destination file path", "目标文件路径"),
                Type = ParameterType.OutputFile,
                Required = true,
                FileFilter = _targetFilter
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
        var layer = await Task.Run(() => OguLayerUtil.ReadLayer(_sourceFormat, inputPath), ct);

        // The DXF driver stores entities (LINE/POLYLINE/POINT), never multi-part
        // geometries — every feature of a real-world MultiPolygon layer fails to
        // create and the whole write aborts. Explode multi-part geometries into
        // single-part features (preserving attributes) for DXF targets.
        if (_targetFormat == DataFormatType.DXF)
            layer = ExplodeForDxf(layer);

        progress?.Report(L($"Read {layer.GetFeatureCount()} features. Writing output...", $"已读取 {layer.GetFeatureCount()} 个要素，正在写入输出..."));
        await Task.Run(() => WriteLayerSafe(_targetFormat, layer, outputPath, progress), ct);

        return new ToolResult
        {
            Success = true,
            Message = L($"Conversion completed. {layer.GetFeatureCount()} features converted.", $"转换完成，共转换 {layer.GetFeatureCount()} 个要素。"),
            OutputPath = outputPath
        };
    }

    private static OguLayer ExplodeForDxf(OguLayer src)
    {
        var outLayer = new OguLayer
        {
            Name = src.Name,
            Wkid = src.Wkid,
            GeometryType = src.GeometryType switch
            {
                GeometryType.MULTIPOLYGON => GeometryType.POLYGON,
                GeometryType.MULTILINESTRING => GeometryType.LINESTRING,
                GeometryType.MULTIPOINT => GeometryType.POINT,
                _ => src.GeometryType
            }
        };
        // NOTE: attributes are intentionally NOT carried over. The DXF entity schema
        // is fixed; when its field creation is skipped, the write pipeline still maps
        // attribute indices ordinally and lands values on the wrong built-in columns
        // (breaking every feature). Geometry-only output is the reliable contract.

        var fid = 0;
        foreach (var feature in src.Features)
        {
            if (string.IsNullOrWhiteSpace(feature.Wkt)) continue;
            var wkt = feature.Wkt.TrimStart();
            var container = wkt.StartsWith("MULTIPOLYGON", StringComparison.OrdinalIgnoreCase) ? GeometryType.MULTIPOLYGON
                : wkt.StartsWith("MULTILINESTRING", StringComparison.OrdinalIgnoreCase) ? GeometryType.MULTILINESTRING
                : wkt.StartsWith("MULTIPOINT", StringComparison.OrdinalIgnoreCase) ? GeometryType.MULTIPOINT
                : GeometryType.UNKNOWN;
            if (container == GeometryType.UNKNOWN)
            {
                // Simple geometry: write it whole. (Iterating rings of a single
                // POLYGON would emit LINESTRING parts that the DXF layer rejects.)
                var whole = feature.Clone();
                whole.Fid = fid++;
                outLayer.AddFeature(whole);
                continue;
            }

            var geometry = GeometryUtil.Wkt2Geometry(feature.Wkt);
            if (geometry == null) continue;
            try
            {
                var parts = geometry.GetGeometryCount();
                for (var i = 0; i < parts; i++)
                {
                    var part = geometry.GetGeometryRef(i);
                    if (part == null) continue;
                    var single = feature.Clone();
                    single.Fid = fid++;
                    single.Wkt = GeometryUtil.Geometry2Wkt(part);
                    outLayer.AddFeature(single);
                }
            }
            finally
            {
                geometry.Dispose();
            }
        }
        return outLayer;
    }
}
