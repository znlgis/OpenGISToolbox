using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using OpenGISToolbox.Models;

namespace OpenGISToolbox.Tools;

/// <summary>
/// Processes GPX files: merge, extract tracks/waypoints, or convert to GeoJSON.
/// </summary>
public class GpxProcessingTool : ToolBase
{
    public override string Id => "gpx-processing";
    public override string Name => "GPX Processing";
    public override string NameZh => "GPX 处理";
    public override string Description => "Process GPX files: merge multiple GPX files, extract tracks/waypoints, or convert GPX to GeoJSON";
    public override string DescriptionZh => "处理 GPX 文件：合并多个 GPX 文件、提取轨迹/路点或将 GPX 转换为 GeoJSON";
    public override ToolCategory Category => ToolCategory.GPS;

    public override List<ToolParameter> BuildParameters()
    {
        return new List<ToolParameter>
        {
            new ToolParameter
            {
                Name = "input",
                Label = "Input File",
                LabelZh = "输入文件",
                Description = L("Input GPX file", "输入 GPX 文件"),
                Type = ParameterType.InputFile,
                Required = true,
                FileFilter = "GPX files|*.gpx"
            },
            new ToolParameter
            {
                Name = "operation",
                Label = "Operation",
                LabelZh = "操作",
                Description = L("Processing operation to perform", "要执行的处理操作"),
                Type = ParameterType.Dropdown,
                Required = true,
                DefaultValue = "Extract Waypoints to CSV",
                Options = new[]
                {
                    "Extract Waypoints to CSV",
                    "Extract Tracks to GeoJSON",
                    "GPX Summary"
                }
            },
            new ToolParameter
            {
                Name = "output",
                Label = "Output File",
                LabelZh = "输出文件",
                Description = L("Output file path (not required for Summary)", "输出文件路径（摘要操作不需要）"),
                Type = ParameterType.OutputFile,
                Required = false,
                FileFilter = "CSV|*.csv|GeoJSON|*.geojson|Text|*.txt"
            }
        };
    }

    protected override async Task<ToolResult> ExecuteCoreAsync(
        Dictionary<string, string> parameters,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var inputPath = GetRequired(parameters, "input");
        var operation = GetRequired(parameters, "operation");
        var outputPath = GetOptional(parameters, "output");

        progress?.Report(L("Loading GPX file...", "正在加载 GPX 文件..."));
        var doc = await Task.Run(() => XDocument.Load(inputPath), ct);
        XNamespace gpxNs = "http://www.topografix.com/GPX/1/1";

        switch (operation)
        {
            case "Extract Waypoints to CSV":
                return await ExtractWaypointsToCsv(doc, gpxNs, outputPath, progress, ct);

            case "Extract Tracks to GeoJSON":
                return await ExtractTracksToGeoJson(doc, gpxNs, outputPath, progress, ct);

            case "GPX Summary":
                return BuildGpxSummary(doc, gpxNs);

            default:
                throw new ArgumentException(L(
                    $"Unknown operation: {operation}",
                    $"未知操作: {operation}"));
        }
    }

    private async Task<ToolResult> ExtractWaypointsToCsv(
        XDocument doc, XNamespace gpxNs, string outputPath,
        IProgress<string>? progress, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException(L(
                "Output file is required for waypoint extraction.",
                "提取路点需要指定输出文件。"));

        var waypoints = doc.Descendants(gpxNs + "wpt").ToList();
        progress?.Report(L($"Extracting {waypoints.Count} waypoints...",
            $"正在提取 {waypoints.Count} 个路点..."));

        var sb = new StringBuilder();
        sb.AppendLine("Name,Lat,Lon,Elevation,Description");

        foreach (var wpt in waypoints)
        {
            ct.ThrowIfCancellationRequested();

            var lat = wpt.Attribute("lat")?.Value ?? "0";
            var lon = wpt.Attribute("lon")?.Value ?? "0";
            var name = wpt.Element(gpxNs + "name")?.Value ?? "";
            var ele = wpt.Element(gpxNs + "ele")?.Value ?? "";
            var desc = wpt.Element(gpxNs + "desc")?.Value ?? "";

            sb.AppendLine($"{EscapeCsv(name)},{lat},{lon},{ele},{EscapeCsv(desc)}");
        }

        await File.WriteAllTextAsync(outputPath, sb.ToString(), ct);

        return new ToolResult
        {
            Success = true,
            Message = L(
                $"Extracted {waypoints.Count} waypoints to CSV.",
                $"已提取 {waypoints.Count} 个路点到 CSV。"),
            OutputPath = outputPath
        };
    }

    private async Task<ToolResult> ExtractTracksToGeoJson(
        XDocument doc, XNamespace gpxNs, string outputPath,
        IProgress<string>? progress, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException(L(
                "Output file is required for track extraction.",
                "提取轨迹需要指定输出文件。"));

        var tracks = doc.Descendants(gpxNs + "trk").ToList();
        progress?.Report(L($"Extracting {tracks.Count} tracks...",
            $"正在提取 {tracks.Count} 条轨迹..."));

        var features = new List<object>();

        foreach (var trk in tracks)
        {
            ct.ThrowIfCancellationRequested();

            var trackName = trk.Element(gpxNs + "name")?.Value ?? "";
            var segments = trk.Descendants(gpxNs + "trkseg").ToList();

            foreach (var seg in segments)
            {
                var points = seg.Descendants(gpxNs + "trkpt")
                    .Select(pt => new[]
                    {
                        double.Parse(pt.Attribute("lon")?.Value ?? "0", CultureInfo.InvariantCulture),
                        double.Parse(pt.Attribute("lat")?.Value ?? "0", CultureInfo.InvariantCulture)
                    })
                    .ToList();

                if (points.Count < 2)
                    continue;

                features.Add(new
                {
                    type = "Feature",
                    properties = new { name = trackName },
                    geometry = new
                    {
                        type = "LineString",
                        coordinates = points
                    }
                });
            }
        }

        var geoJson = new
        {
            type = "FeatureCollection",
            features
        };

        var json = JsonSerializer.Serialize(geoJson, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(outputPath, json, ct);

        return new ToolResult
        {
            Success = true,
            Message = L(
                $"Extracted {features.Count} track segments to GeoJSON.",
                $"已提取 {features.Count} 个轨迹段到 GeoJSON。"),
            OutputPath = outputPath
        };
    }

    private static ToolResult BuildGpxSummary(XDocument doc, XNamespace gpxNs)
    {
        var wptCount = doc.Descendants(gpxNs + "wpt").Count();
        var trkCount = doc.Descendants(gpxNs + "trk").Count();
        var trksegCount = doc.Descendants(gpxNs + "trkseg").Count();
        var trkptCount = doc.Descendants(gpxNs + "trkpt").Count();

        var sb = new StringBuilder();
        sb.AppendLine(L("=== GPX Summary ===", "=== GPX 摘要 ==="));
        sb.AppendLine(L($"Waypoints: {wptCount}", $"路点: {wptCount}"));
        sb.AppendLine(L($"Tracks: {trkCount}", $"轨迹: {trkCount}"));
        sb.AppendLine(L($"Track Segments: {trksegCount}", $"轨迹段: {trksegCount}"));
        sb.AppendLine(L($"Track Points: {trkptCount}", $"轨迹点: {trkptCount}"));

        return new ToolResult
        {
            Success = true,
            Message = sb.ToString()
        };
    }
}
