using System;

namespace OpenGISToolbox.Models;

public class ToolResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? OutputPath { get; set; }
    public TimeSpan Duration { get; set; }
}
