using System;

namespace OpenGISToolbox.Models;

public record ToolResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? OutputPath { get; init; }
    public TimeSpan Duration { get; init; }
}
