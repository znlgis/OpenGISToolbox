using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    // Cached tool list — ToolRegistry returns a Lazy singleton so this is just a reference
    private readonly List<ToolInfo> _allTools;

    public MainWindowViewModel()
    {
        _executionViewModel = new ToolExecutionViewModel();

        _allTools = ToolRegistry.GetAllTools();

        var groups = _allTools.GroupBy(t => t.Category);
        foreach (var group in groups)
        {
            Categories.Add(new ToolCategoryItem
            {
                Category = group.Key,
                DisplayName = GetCategoryDisplayName(group.Key),
                ToolCount = group.Count()
            });
        }

        foreach (var tool in _allTools)
            FilteredTools.Add(tool);

        // LanguageChanged subscription is intentionally not unsubscribed because
        // this ViewModel lives for the entire application lifetime (created once in App.axaml.cs).
        LanguageManager.Instance.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        foreach (var cat in Categories)
        {
            cat.DisplayName = GetCategoryDisplayName(cat.Category);
            cat.RefreshToolCountDisplay();
        }

        // Refresh tool list to update DisplayName/DisplayDescription bindings
        var selectedId = SelectedTool?.Id;
        var currentCategoryItem = SelectedCategoryItem;

        FilteredTools.Clear();
        var filtered = currentCategoryItem != null
            ? _allTools.Where(t => t.Category == currentCategoryItem.Category)
            : _allTools;
        foreach (var tool in filtered)
            FilteredTools.Add(tool);

        // Restore selection by Id
        if (selectedId != null)
        {
            var restored = FilteredTools.FirstOrDefault(t => t.Id == selectedId);
            if (restored != null)
                SelectedTool = restored;
        }
    }

    partial void OnSelectedCategoryItemChanged(ToolCategoryItem? value)
    {
        FilteredTools.Clear();
        var filtered = value != null ? _allTools.Where(t => t.Category == value.Category) : _allTools;
        foreach (var tool in filtered)
            FilteredTools.Add(tool);
    }

    partial void OnSelectedToolChanged(ToolInfo? value)
    {
        ExecutionViewModel.SelectedTool = value;
    }

    [RelayCommand]
    private void SwitchLanguage()
    {
        var lang = LanguageManager.Instance.CurrentLanguage == "en" ? "zh" : "en";
        LanguageManager.Instance.SwitchLanguage(lang);
    }

    private static string GetCategoryDisplayName(ToolCategory category)
    {
        return LanguageManager.GetLocalizedString(GetCategoryResourceKey(category), category.ToString());
    }

    private static string GetCategoryResourceKey(ToolCategory category) => category switch
    {
        ToolCategory.Conversion => "CategoryConversion",
        ToolCategory.Geometry => "CategoryGeometry",
        ToolCategory.Validation => "CategoryValidation",
        ToolCategory.Coordinate => "CategoryCoordinate",
        ToolCategory.Analysis => "CategoryAnalysis",
        ToolCategory.Utility => "CategoryUtility",
        ToolCategory.Raster => "CategoryRaster",
        ToolCategory.RemoteSensing => "CategoryRemoteSensing",
        ToolCategory.GPS => "CategoryGPS",
        ToolCategory.Geocoding => "CategoryGeocoding",
        _ => category.ToString()
    };
}

public class ToolCategoryItem : INotifyPropertyChanged
{
    private string _displayName = string.Empty;

    public ToolCategory Category { get; set; }

    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (_displayName == value) return;
            _displayName = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
        }
    }

    public int ToolCount { get; set; }

    public string ToolCountDisplay
    {
        get
        {
            var fmt = LanguageManager.GetLocalizedString("ToolsFormat", "{0} tools");
            return string.Format(fmt, ToolCount);
        }
    }

    public void RefreshToolCountDisplay()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ToolCountDisplay)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
