using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OpenGIS.Utils.Engine.Enums;
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

    /// <summary>Safely get a required string parameter.</summary>
    protected static string GetRequired(Dictionary<string, string> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            throw new ArgumentException(L($"Required parameter '{key}' is missing or empty.", $"必填参数 '{key}' 缺失或为空。"));
        return value;
    }

    /// <summary>Safely get an optional string parameter.</summary>
    protected static string GetOptional(Dictionary<string, string> parameters, string key, string defaultValue = "")
    {
        return parameters.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : defaultValue;
    }

    /// <summary>Safely parse a required double parameter.</summary>
    protected static double GetRequiredDouble(Dictionary<string, string> parameters, string key)
    {
        var raw = GetRequired(parameters, key);
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
            throw new ArgumentException(L($"Parameter '{key}' value '{raw}' is not a valid number.", $"参数 '{key}' 的值 '{raw}' 不是有效数字。"));
        return result;
    }

    /// <summary>Safely parse a required integer parameter.</summary>
    protected static int GetRequiredInt(Dictionary<string, string> parameters, string key)
    {
        var raw = GetRequired(parameters, key);
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
            throw new ArgumentException(L($"Parameter '{key}' value '{raw}' is not a valid integer.", $"参数 '{key}' 的值 '{raw}' 不是有效整数。"));
        return result;
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
