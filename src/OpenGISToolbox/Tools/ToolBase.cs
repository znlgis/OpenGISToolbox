using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenGIS.Utils.DataSource;
using OpenGIS.Utils.Engine.Enums;
using OpenGIS.Utils.Engine.Model.Layer;
using OpenGISToolbox.Models;
using OpenGISToolbox.Services;

namespace OpenGISToolbox.Tools;

/// <summary>
/// Base class for all GIS tools. Implements the Template Method pattern
/// to provide consistent timing, error handling, parameter validation,
/// and progress reporting across all tools.
/// </summary>
public abstract class ToolBase
{
    /// <summary>Tool unique identifier.</summary>
    public abstract string Id { get; }

    /// <summary>English display name.</summary>
    public abstract string Name { get; }

    /// <summary>Chinese display name.</summary>
    public abstract string NameZh { get; }

    /// <summary>English description.</summary>
    public abstract string Description { get; }

    /// <summary>Chinese description.</summary>
    public abstract string DescriptionZh { get; }

    /// <summary>Tool category for UI grouping.</summary>
    public abstract ToolCategory Category { get; }

    /// <summary>Parameter definitions for the tool.</summary>
    public abstract List<ToolParameter> BuildParameters();

    /// <summary>
    /// Core execution logic. Subclasses implement this method only.
    /// Timing, error handling, and fatal exception filtering are handled by the base class.
    /// </summary>
    protected abstract Task<ToolResult> ExecuteCoreAsync(
        Dictionary<string, string> parameters,
        IProgress<string>? progress,
        CancellationToken ct);

    /// <summary>
    /// Builds a <see cref="ToolInfo"/> for registration in the tool registry.
    /// Wraps <see cref="ExecuteCoreAsync"/> with timing and robust error handling.
    /// </summary>
    public ToolInfo ToToolInfo()
    {
        return new ToolInfo
        {
            Id = Id,
            Name = Name,
            NameZh = NameZh,
            Description = Description,
            DescriptionZh = DescriptionZh,
            Category = Category,
            Parameters = BuildParameters(),
            ExecuteAsync = ExecuteWithWrapperAsync
        };
    }

    private async Task<ToolResult> ExecuteWithWrapperAsync(
        Dictionary<string, string> parameters,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await ExecuteCoreAsync(parameters, progress, ct);
            sw.Stop();
            // Ensure Duration is set if the tool didn't set it
            if (result.Duration == TimeSpan.Zero)
                return result with { Duration = sw.Elapsed };
            return result;
        }
        catch (OperationCanceledException)
        {
            throw; // Let the caller (ToolExecutionViewModel) handle cancellation
        }
        catch (OutOfMemoryException)
        {
            throw; // Fatal: do not swallow
        }
        catch (StackOverflowException)
        {
            throw; // Fatal: do not swallow
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ToolResult
            {
                Success = false,
                Message = L($"Error: {ex.Message}", $"错误：{ex.Message}"),
                Duration = sw.Elapsed
            };
        }
    }

    // ─── Shared helpers available to all tools ───

    /// <summary>Returns the localized string based on current language setting.</summary>
    protected static string L(string en, string zh) =>
        LanguageManager.Instance.CurrentLanguage == "zh" ? zh : en;

    /// <summary>Detect vector format from file extension.</summary>
    protected static DataFormatType DetectFormat(string filePath)
    {
        var ext = Path.GetExtension(filePath)?.ToLowerInvariant();
        return ext switch
        {
            ".shp" => DataFormatType.SHP,
            ".geojson" or ".json" => DataFormatType.GEOJSON,
            ".gpkg" => DataFormatType.GEOPACKAGE,
            ".kml" => DataFormatType.KML,
            ".dxf" => DataFormatType.DXF,
            ".gdb" => DataFormatType.FILEGDB,
            _ => throw new ArgumentException(L($"Unsupported file format: {ext}", $"不支持的文件格式：{ext}"))
        };
    }

    /// <summary>
    /// Safely get a required string parameter. A parameter that declares a
    /// <see cref="ToolParameter.DefaultValue"/> falls back to it when the caller
    /// omitted the key, so headless invocations (CLI/harness/API) behave like the
    /// UI, which prefills defaults. Parameters without a default still fail.
    /// </summary>
    protected string GetRequired(Dictionary<string, string> parameters, string key)
    {
        if (parameters.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            return value;

        var declaredDefault = BuildParameters()
            .FirstOrDefault(p => p.Name == key && p.Required)?.DefaultValue;
        if (!string.IsNullOrWhiteSpace(declaredDefault))
            return declaredDefault;

        throw new ArgumentException(L($"Required parameter '{key}' is missing or empty.", $"必填参数 '{key}' 缺失或为空。"));
    }

    /// <summary>Safely get an optional string parameter.</summary>
    protected static string GetOptional(Dictionary<string, string> parameters, string key, string defaultValue = "")
    {
        return parameters.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : defaultValue;
    }

    /// <summary>Safely parse a required double parameter.</summary>
    protected double GetRequiredDouble(Dictionary<string, string> parameters, string key)
    {
        var raw = GetRequired(parameters, key);
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
            throw new ArgumentException(L($"Parameter '{key}' value '{raw}' is not a valid number.", $"参数 '{key}' 的值 '{raw}' 不是有效数字。"));
        return result;
    }

    /// <summary>Safely parse a required integer parameter.</summary>
    protected int GetRequiredInt(Dictionary<string, string> parameters, string key)
    {
        var raw = GetRequired(parameters, key);
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
            throw new ArgumentException(L($"Parameter '{key}' value '{raw}' is not a valid integer.", $"参数 '{key}' 的值 '{raw}' 不是有效整数。"));
        return result;
    }

    /// <summary>
    /// Writes a vector layer, first dropping features that carry no geometry.
    /// Real-world files legitimately contain NullShape records; the GDAL-backed
    /// engine counts every geometry-less feature as a write failure and aborts
    /// the whole layer, so a single null record would otherwise fail an
    /// otherwise-fine conversion. Such features cannot round-trip through the
    /// WKT pipeline anyway, so we drop them and surface the count via progress.
    /// Returns the number of dropped features.
    /// </summary>
    protected static int WriteLayerSafe(
        DataFormatType format, OguLayer layer, string path, IProgress<string>? progress)
    {
        var nulls = layer.Features.Count(f => string.IsNullOrWhiteSpace(f.Wkt));
        if (nulls > 0)
        {
            layer.Features = layer.Features
                .Where(f => !string.IsNullOrWhiteSpace(f.Wkt)).ToList();
            progress?.Report(L(
                $"Skipped {nulls} feature(s) without geometry.",
                $"已跳过 {nulls} 个无几何要素。"));
        }
        OguLayerUtil.WriteLayer(format, layer, path);
        return nulls;
    }

    /// <summary>Escape a CSV field value.</summary>
    protected static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }
}
