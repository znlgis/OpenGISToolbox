using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using OpenGISToolbox.Models;

namespace OpenGISToolbox.Tools;

/// <summary>
/// Downloads satellite imagery tiles from public tile services.
/// </summary>
public class SatelliteDownloadTool : ToolBase
{
    private static readonly Lazy<HttpClient> SharedHttpClient = new(() =>
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OpenGISToolbox/1.0");
        return client;
    });

    public override string Id => "satellite-download";
    public override string Name => "Satellite Image Download";
    public override string NameZh => "卫星影像下载";
    public override string Description => "Download satellite imagery tiles from public tile services (OpenStreetMap, Esri World Imagery)";
    public override string DescriptionZh => "从公共瓦片服务下载卫星影像瓦片（OpenStreetMap、Esri World Imagery）";
    public override ToolCategory Category => ToolCategory.RemoteSensing;

    public override List<ToolParameter> BuildParameters()
    {
        return new List<ToolParameter>
        {
            new ToolParameter
            {
                Name = "longitude",
                Label = "Longitude",
                LabelZh = "经度",
                Description = L("Longitude of the center point", "中心点经度"),
                Type = ParameterType.Number,
                Required = true,
                DefaultValue = "116.4"
            },
            new ToolParameter
            {
                Name = "latitude",
                Label = "Latitude",
                LabelZh = "纬度",
                Description = L("Latitude of the center point", "中心点纬度"),
                Type = ParameterType.Number,
                Required = true,
                DefaultValue = "39.9"
            },
            new ToolParameter
            {
                Name = "zoom",
                Label = "Zoom Level",
                LabelZh = "缩放级别",
                Description = L("Tile zoom level (0-19)", "瓦片缩放级别（0-19）"),
                Type = ParameterType.Integer,
                Required = true,
                DefaultValue = "10"
            },
            new ToolParameter
            {
                Name = "source",
                Label = "Tile Source",
                LabelZh = "瓦片源",
                Description = L("Tile service provider", "瓦片服务提供商"),
                Type = ParameterType.Dropdown,
                Required = true,
                DefaultValue = "OpenStreetMap",
                Options = new[] { "OpenStreetMap", "Esri World Imagery" }
            },
            new ToolParameter
            {
                Name = "output",
                Label = "Output File",
                LabelZh = "输出文件",
                Description = L("Output PNG file path", "输出 PNG 文件路径"),
                Type = ParameterType.OutputFile,
                Required = true,
                FileFilter = "PNG|*.png"
            }
        };
    }

    protected override async Task<ToolResult> ExecuteCoreAsync(
        Dictionary<string, string> parameters,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var longitude = GetRequiredDouble(parameters, "longitude");
        var latitude = GetRequiredDouble(parameters, "latitude");
        var zoom = GetRequiredInt(parameters, "zoom");
        var source = GetOptional(parameters, "source", "OpenStreetMap");
        var outputPath = GetRequired(parameters, "output");

        // Compute tile X/Y from lon/lat/zoom
        var n = Math.Pow(2, zoom);
        var tileX = (int)Math.Floor((longitude + 180.0) / 360.0 * n);
        var latRad = latitude * Math.PI / 180.0;
        var tileY = (int)Math.Floor((1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * n);

        var url = source switch
        {
            "Esri World Imagery" =>
                $"https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{zoom}/{tileY}/{tileX}",
            _ =>
                $"https://tile.openstreetmap.org/{zoom}/{tileX}/{tileY}.png"
        };

        progress?.Report(L($"Downloading tile ({tileX}, {tileY}) at zoom {zoom}...",
            $"正在下载瓦片（{tileX}, {tileY}），缩放级别 {zoom}..."));

        var client = SharedHttpClient.Value;
        var bytes = await client.GetByteArrayAsync(url);

        ct.ThrowIfCancellationRequested();

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        await File.WriteAllBytesAsync(outputPath, bytes, ct);

        return new ToolResult
        {
            Success = true,
            Message = L(
                $"Satellite tile downloaded. Source: {source}, Tile: ({tileX}, {tileY}), Zoom: {zoom}",
                $"卫星瓦片下载完成。来源: {source}，瓦片: ({tileX}, {tileY})，缩放级别: {zoom}"),
            OutputPath = outputPath
        };
    }
}
