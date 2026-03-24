using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenGISToolbox.Models;

namespace OpenGISToolbox.Tools;

/// <summary>
/// Geocodes addresses to coordinates using OpenStreetMap Nominatim service.
/// </summary>
public class GeocodeAddressesTool : ToolBase
{
    private static readonly Lazy<HttpClient> SharedHttpClient = new(() =>
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OpenGISToolbox/1.0");
        return client;
    });

    public override string Id => "geocode-addresses";
    public override string Name => "Geocode Addresses";
    public override string NameZh => "地址编码";
    public override string Description => "Geocode addresses to coordinates using OpenStreetMap Nominatim service";
    public override string DescriptionZh => "使用 OpenStreetMap Nominatim 服务将地址编码为坐标";
    public override ToolCategory Category => ToolCategory.Geocoding;

    public override List<ToolParameter> BuildParameters()
    {
        return new List<ToolParameter>
        {
            new ToolParameter
            {
                Name = "input",
                Label = "Input File",
                LabelZh = "输入文件",
                Description = L("Text or CSV file with one address per line", "每行一个地址的文本或 CSV 文件"),
                Type = ParameterType.InputFile,
                Required = true,
                FileFilter = "Text files|*.txt|CSV files|*.csv"
            },
            new ToolParameter
            {
                Name = "output",
                Label = "Output File",
                LabelZh = "输出文件",
                Description = L("Output CSV file with geocoded coordinates", "包含编码坐标的输出 CSV 文件"),
                Type = ParameterType.OutputFile,
                Required = true,
                FileFilter = "CSV|*.csv"
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

        progress?.Report(L("Reading addresses...", "正在读取地址..."));
        var allLines = await File.ReadAllLinesAsync(inputPath, ct);
        var addresses = new List<string>();
        foreach (var line in allLines)
        {
            if (!string.IsNullOrWhiteSpace(line))
                addresses.Add(line.Trim());
        }

        if (addresses.Count == 0)
            throw new ArgumentException(L(
                "No addresses found in the input file.",
                "输入文件中未找到任何地址。"));

        progress?.Report(L($"Geocoding {addresses.Count} addresses...",
            $"正在编码 {addresses.Count} 个地址..."));

        var client = SharedHttpClient.Value;
        var sb = new StringBuilder();
        sb.AppendLine("Address,Latitude,Longitude,DisplayName");

        int successCount = 0;
        int failCount = 0;

        for (int i = 0; i < addresses.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var address = addresses[i];
            var encodedAddress = WebUtility.UrlEncode(address);
            var url = $"https://nominatim.openstreetmap.org/search?q={encodedAddress}&format=json&limit=1";

            progress?.Report(L(
                $"Geocoding ({i + 1}/{addresses.Count}): {address}",
                $"正在编码（{i + 1}/{addresses.Count}）: {address}"));

            string lat = "";
            string lon = "";
            string displayName = "";

            try
            {
                var json = await client.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.GetArrayLength() > 0)
                {
                    var first = root[0];
                    lat = first.GetProperty("lat").GetString() ?? "";
                    lon = first.GetProperty("lon").GetString() ?? "";
                    displayName = first.GetProperty("display_name").GetString() ?? "";
                    successCount++;
                }
                else
                {
                    failCount++;
                    progress?.Report(L(
                        $"Warning: No results for '{address}'",
                        $"警告: 未找到 '{address}' 的结果"));
                }
            }
            catch (HttpRequestException ex)
            {
                failCount++;
                progress?.Report(L(
                    $"Warning: HTTP error for '{address}': {ex.Message}",
                    $"警告: '{address}' 的 HTTP 错误: {ex.Message}"));
            }
            catch (JsonException ex)
            {
                failCount++;
                progress?.Report(L(
                    $"Warning: JSON parse error for '{address}': {ex.Message}",
                    $"警告: '{address}' 的 JSON 解析错误: {ex.Message}"));
            }

            sb.AppendLine($"{EscapeCsv(address)},{lat},{lon},{EscapeCsv(displayName)}");

            // Nominatim rate limit: 1 request per second
            if (i < addresses.Count - 1)
                await Task.Delay(1000, ct);
        }

        await File.WriteAllTextAsync(outputPath, sb.ToString(), ct);

        return new ToolResult
        {
            Success = true,
            Message = L(
                $"Geocoding completed. {successCount} succeeded, {failCount} failed out of {addresses.Count} addresses.",
                $"地址编码完成。{addresses.Count} 个地址中，{successCount} 个成功，{failCount} 个失败。"),
            OutputPath = outputPath
        };
    }
}
