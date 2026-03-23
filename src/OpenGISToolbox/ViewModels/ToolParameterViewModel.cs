using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenGISToolbox.Models;
using OpenGISToolbox.Services;

namespace OpenGISToolbox.ViewModels;

public partial class ToolParameterViewModel : ViewModelBase
{
    public ToolParameter Parameter { get; }

    [ObservableProperty]
    private string _value;

    public string Label => Parameter.DisplayLabel;
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

        if (Parameter.Type == ParameterType.FolderPath)
        {
            var options = new FolderPickerOpenOptions
            {
                Title = LanguageManager.GetLocalizedString("SelectFolder", "Select Folder"),
                AllowMultiple = false
            };

            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(options);

            if (folders.Count > 0)
                Value = folders[0].Path.LocalPath;

            return;
        }

        var fileTypes = ParseFileFilter(Parameter.FileFilter);

        if (Parameter.Type == ParameterType.InputFile)
        {
            var options = new FilePickerOpenOptions
            {
                Title = LanguageManager.GetLocalizedString("SelectInputFile", "Select Input File"),
                AllowMultiple = false
            };

            if (fileTypes.Count > 0)
                options.FileTypeFilter = fileTypes;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(options);

            if (files.Count > 0)
                Value = files[0].Path.LocalPath;
        }
        else if (Parameter.Type == ParameterType.OutputFile)
        {
            var options = new FilePickerSaveOptions
            {
                Title = LanguageManager.GetLocalizedString("SelectOutputFilePicker", "Select Output File")
            };

            if (fileTypes.Count > 0)
            {
                options.FileTypeChoices = fileTypes;
                var firstPattern = fileTypes[0].Patterns?.FirstOrDefault();
                if (firstPattern != null)
                    options.DefaultExtension = firstPattern.TrimStart('*').TrimStart('.');
            }

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(options);

            if (file != null)
                Value = file.Path.LocalPath;
        }
    }

    private static List<FilePickerFileType> ParseFileFilter(string? filter)
    {
        var fileTypes = new List<FilePickerFileType>();
        if (string.IsNullOrEmpty(filter))
            return fileTypes;

        var parts = filter.Split('|');
        for (int i = 0; i + 1 < parts.Length; i += 2)
        {
            var name = parts[i].Trim();
            var patterns = parts[i + 1].Split(';')
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrEmpty(p))
                .ToList();

            fileTypes.Add(new FilePickerFileType(name) { Patterns = patterns });
        }

        return fileTypes;
    }
}
