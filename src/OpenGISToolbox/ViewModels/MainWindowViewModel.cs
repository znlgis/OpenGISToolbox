using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenGISToolbox.Models;
using OpenGISToolbox.Services;

namespace OpenGISToolbox.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private ToolCategoryItem? _selectedCategoryItem;

    [ObservableProperty]
    private ToolInfo? _selectedTool;

    [ObservableProperty]
    private ToolExecutionViewModel _executionViewModel;

    public ObservableCollection<ToolCategoryItem> Categories { get; } = new();
    public ObservableCollection<ToolInfo> FilteredTools { get; } = new();

    public MainWindowViewModel()
    {
        _executionViewModel = new ToolExecutionViewModel();

        var allTools = ToolRegistry.GetAllTools();

        var groups = allTools.GroupBy(t => t.Category);
        foreach (var group in groups)
        {
            Categories.Add(new ToolCategoryItem
            {
                Category = group.Key,
                DisplayName = GetCategoryDisplayName(group.Key),
                ToolCount = group.Count()
            });
        }

        foreach (var tool in allTools)
            FilteredTools.Add(tool);
    }

    partial void OnSelectedCategoryItemChanged(ToolCategoryItem? value)
    {
        FilteredTools.Clear();
        var allTools = ToolRegistry.GetAllTools();
        var filtered = value != null ? allTools.Where(t => t.Category == value.Category) : allTools;
        foreach (var tool in filtered)
            FilteredTools.Add(tool);
    }

    partial void OnSelectedToolChanged(ToolInfo? value)
    {
        ExecutionViewModel.SelectedTool = value;
    }

    private static string GetCategoryDisplayName(ToolCategory category) => category switch
    {
        ToolCategory.Conversion => "格式转换 (Conversion)",
        ToolCategory.Geometry => "几何处理 (Geometry)",
        ToolCategory.Validation => "几何验证 (Validation)",
        ToolCategory.Coordinate => "坐标转换 (Coordinate)",
        ToolCategory.Analysis => "空间分析 (Analysis)",
        ToolCategory.Utility => "实用工具 (Utility)",
        _ => category.ToString()
    };
}

public class ToolCategoryItem
{
    public ToolCategory Category { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public int ToolCount { get; set; }
}
