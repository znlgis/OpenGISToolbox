namespace OpenGISToolbox.Models;

public enum ParameterType
{
    InputFile,       // File browser for input
    OutputFile,      // File browser for output
    Number,          // Numeric input
    Text,            // Text input
    Dropdown,        // Dropdown selection
    Integer          // Integer input
}

public class ToolParameter
{
    public string Name { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ParameterType Type { get; set; }
    public string? DefaultValue { get; set; }
    public string[]? Options { get; set; }  // For dropdown
    public bool Required { get; set; } = true;
    public string? FileFilter { get; set; }  // For file dialogs, e.g. "Shapefile|*.shp"
}
