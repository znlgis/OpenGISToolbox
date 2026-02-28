using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenGISToolbox.Models;

namespace OpenGISToolbox.ViewModels;

public partial class ToolParameterViewModel : ViewModelBase
{
    public ToolParameter Parameter { get; }

    [ObservableProperty]
    private string _value;

    public string Label => Parameter.Label;
    public ParameterType Type => Parameter.Type;
    public string[]? Options => Parameter.Options;
    public string? FileFilter => Parameter.FileFilter;
    public bool Required => Parameter.Required;

    public ToolParameterViewModel(ToolParameter parameter)
    {
        Parameter = parameter;
        _value = parameter.DefaultValue ?? string.Empty;
    }

    [RelayCommand]
    private async Task BrowseFileAsync()
    {
        var topLevel = TopLevel.GetTopLevel(
            App.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null);

        if (topLevel == null)
            return;

        if (Parameter.Type == ParameterType.InputFile)
        {
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = "Select Input File",
                    AllowMultiple = false
                });

            if (files.Count > 0)
                Value = files[0].Path.LocalPath;
        }
        else if (Parameter.Type == ParameterType.OutputFile)
        {
            var file = await topLevel.StorageProvider.SaveFilePickerAsync(
                new FilePickerSaveOptions
                {
                    Title = "Select Output File"
                });

            if (file != null)
                Value = file.Path.LocalPath;
        }
    }
}
