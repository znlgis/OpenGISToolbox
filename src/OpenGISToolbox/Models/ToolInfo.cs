using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenGISToolbox.Services;

namespace OpenGISToolbox.Models;

public class ToolInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string NameZh { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string DescriptionZh { get; set; } = string.Empty;
    public ToolCategory Category { get; set; }
    public List<ToolParameter> Parameters { get; set; } = new();
    public Func<Dictionary<string, string>, IProgress<string>?, CancellationToken, Task<ToolResult>>? ExecuteAsync { get; set; }

    public string DisplayName => LanguageManager.Instance.CurrentLanguage == "zh" && !string.IsNullOrEmpty(NameZh) ? NameZh : Name;
    public string DisplayDescription => LanguageManager.Instance.CurrentLanguage == "zh" && !string.IsNullOrEmpty(DescriptionZh) ? DescriptionZh : Description;
}
