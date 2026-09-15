using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenGIS.Utils.DataSource;
using OpenGIS.Utils.Engine.Enums;
using OpenGIS.Utils.Engine.Model.Layer;
using OpenGIS.Utils.Engine.Util;
using OpenGISToolbox.Models;

namespace OpenGISToolbox.Tools;

/// <summary>
/// Exports (writes) a local vector file to a PostGIS table.
/// </summary>
public class PostgisExportTool : ToolBase
{
    public override string Id => "postgis-export";
    public override string Name => "PostGIS Export";
    public override string NameZh => "PostGIS导出";
    public override string Description => "Upload a local vector file to a PostGIS table.";
    public override string DescriptionZh => "将本地矢量文件上传到PostGIS表。";
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
                Description = L("Vector file to export to PostGIS", "要导出到PostGIS的矢量文件"),
                Type = ParameterType.InputFile,
                Required = true,
                FileFilter = "Vector Files|*.shp;*.geojson;*.json;*.gpkg;*.kml;*.dxf"
            },
            new ToolParameter
            {
                Name = "connectionString",
                Label = "Connection String",
                LabelZh = "连接字符串",
                Description = L("PostgreSQL/PostGIS connection string", "PostgreSQL/PostGIS连接字符串"),
                Type = ParameterType.Text,
                Required = true
            },
            new ToolParameter
            {
                Name = "tableName",
                Label = "Table Name",
                LabelZh = "表名",
                Description = L("Target PostGIS table name", "目标PostGIS表名"),
                Type = ParameterType.Text,
                Required = true
            }
        };
    }

    protected override async Task<ToolResult> ExecuteCoreAsync(
        Dictionary<string, string> parameters,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var inputPath = GetRequired(parameters, "input");
        var connectionString = GetRequired(parameters, "connectionString");
        var tableName = GetRequired(parameters, "tableName");

        var format = DetectFormat(inputPath);

        progress?.Report(L("Reading input file...", "读取输入文件..."));
        var layer = await Task.Run(() => OguLayerUtil.ReadLayer(format, inputPath), ct);

        // Shapefile polygon layers are declared non-multi (wkbPolygon) yet frequently
        // carry multipart records that GDAL's PostgreSQL COPY path serializes as
        // MultiPolygon, which a single-type column rejects ("Geometry type
        // (MultiPolygon) does not match column type (Polygon)") — the whole load
        // fails with zero rows written. Promote the declared column type AND the
        // feature WKTs to the Multi variant (PostGIS multi types accept single-part
        // geometries too), so mixed single/multi sources load reliably.
        // Also relax DBF-derived real-number metadata: shapefile doubles carry
        // width/precision (e.g. 24,15) that map to numeric(24,15) in PostGIS and
        // overflow on ordinary values like population counts (1.3e9 > 10^9).
        PrepareLayerForPostgis(layer);

        progress?.Report(L($"Read {layer.GetFeatureCount()} features. Writing to PostGIS table '{tableName}'...", $"已读取 {layer.GetFeatureCount()} 个要素，正在写入PostGIS表 '{tableName}'..."));
        await Task.Run(() => PostgisUtil.WritePostGIS(layer, connectionString, tableName), ct);

        return new ToolResult
        {
            Success = true,
            Message = L($"Export completed. {layer.GetFeatureCount()} features written to PostGIS table '{tableName}'.", $"导出完成，共写入 {layer.GetFeatureCount()} 个要素到PostGIS表 '{tableName}'。")
        };
    }

    /// <summary>
    /// Normalizes a layer for reliable PostgreSQL/PostGIS loading:
    /// 1) promotes single-part geometry columns to their MULTI variant (and wraps
    ///    feature WKTs accordingly) so multipart features never violate the column
    ///    type constraint;
    /// 2) clears DBF-derived width/precision on DOUBLE/FLOAT fields so the driver
    ///    creates unconstrained numerics instead of overflow-prone numeric(w,s).
    /// </summary>
    private static void PrepareLayerForPostgis(OguLayer layer)
    {
        if (layer is null) return;

        switch (layer.GeometryType)
        {
            case GeometryType.POLYGON: layer.GeometryType = GeometryType.MULTIPOLYGON; break;
            case GeometryType.LINESTRING: layer.GeometryType = GeometryType.MULTILINESTRING; break;
            case GeometryType.POINT: layer.GeometryType = GeometryType.MULTIPOINT; break;
        }

        foreach (var feature in layer.Features)
        {
            var wkt = feature.Wkt;
            if (string.IsNullOrWhiteSpace(wkt)) continue;
            feature.Wkt = WrapSingleToMulti(wkt);
        }

        foreach (var field in layer.Fields)
        {
            // Widen numerics rather than dropping to float8: PostGIS COPY text
            // formatting differs per column type, and float8 conversion has been
            // observed to abort/reorder COPY streams on DBF-derived values. Keeping
            // the numeric type with ≥15 integer digits fixes overflow safely.
            if (field.DataType is FieldDataType.DOUBLE or FieldDataType.FLOAT
                && field.Precision is > 0 && field.Length is { } len && len - field.Precision < 15)
            {
                field.Length = field.Precision + 15;
            }
        }
    }

    /// <summary>
    /// Wraps "POLYGON ((..))" → "MULTIPOLYGON (((..)))", "LINESTRING (..)" →
    /// "MULTILINESTRING ((..))", "POINT (x y)" → "MULTIPOINT ((x y))".
    /// Already-multi WKTs pass through unchanged.
    /// </summary>
    private static string WrapSingleToMulti(string wkt)
    {
        var trimmed = wkt.TrimStart();
        var open = wkt.IndexOf('(');
        if (open < 0) return wkt;

        var head = trimmed.Split(new[] { '(', ' ' }, 2, StringSplitOptions.RemoveEmptyEntries)[0]
            .ToUpperInvariant();
        return head switch
        {
            "POLYGON" => "MULTIPOLYGON (" + wkt.Substring(open) + ")",
            "LINESTRING" => "MULTILINESTRING (" + wkt.Substring(open) + ")",
            "POINT" => "MULTIPOINT (" + wkt.Substring(open) + ")",
            _ => wkt
        };
    }
}
