using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenGISToolbox.Models;
using OpenGISToolbox.Services;

namespace OpenGISToolbox.ViewModels;

public partial class ToolExecutionViewModel : ViewModelBase
{
    [ObservableProperty]
    private ToolInfo? _selectedTool;

    [ObservableProperty]
    private string _logOutput = string.Empty;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _hasResult;

    [ObservableProperty]
    private string _resultMessage = string.Empty;

    [ObservableProperty]
    private bool _resultSuccess;

    public ObservableCollection<ToolParameterViewModel> Parameters { get; } = new();

    partial void OnSelectedToolChanged(ToolInfo? value)
    {
        Parameters.Clear();
        HasResult = false;
        ResultMessage = string.Empty;
        LogOutput = string.Empty;

        if (value != null)
        {
            foreach (var param in value.Parameters)
                Parameters.Add(new ToolParameterViewModel(param));
        }

        ExecuteCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanExecute))]
    private async Task ExecuteAsync()
    {
        if (SelectedTool?.ExecuteAsync == null)
            return;

        HasResult = false;
        ResultMessage = string.Empty;
        LogOutput = string.Empty;
        IsRunning = true;

        try
        {
            var parameters = new Dictionary<string, string>();
            foreach (var p in Parameters)
                parameters[p.Parameter.Name] = p.Value;

            // Validate required parameters
            var missing = Parameters
                .Where(p => p.Required && string.IsNullOrWhiteSpace(p.Value))
                .Select(p => p.Label)
                .ToList();

            if (missing.Count > 0)
            {
                ResultSuccess = false;
                ResultMessage = LanguageManager.GetLocalizedString("MissingRequired", "Missing required parameters: ") + string.Join(", ", missing);
                HasResult = true;
                return;
            }

            var progress = new Progress<string>(message =>
            {
                LogOutput += message + Environment.NewLine;
            });

            var result = await SelectedTool.ExecuteAsync(parameters, progress, CancellationToken.None);

            ResultSuccess = result.Success;
            ResultMessage = result.Message;
            if (result.Duration != TimeSpan.Zero)
            {
                var durationFmt = LanguageManager.GetLocalizedString("DurationFormat", " (Duration: {0:F2}s)");
                ResultMessage += string.Format(durationFmt, result.Duration.TotalSeconds);
            }
            HasResult = true;
        }
        catch (Exception ex)
        {
            ResultSuccess = false;
            var errorPrefix = LanguageManager.GetLocalizedString("ErrorPrefix", "Error: ");
            ResultMessage = $"{errorPrefix}{ex.Message}";
            HasResult = true;
            var exceptionPrefix = LanguageManager.GetLocalizedString("ExceptionPrefix", "Exception: ");
            LogOutput += $"{exceptionPrefix}{ex.Message}{Environment.NewLine}";
        }
        finally
        {
            IsRunning = false;
        }
    }

    private bool CanExecute() => SelectedTool != null && !IsRunning;

    partial void OnIsRunningChanged(bool value) => ExecuteCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    private void ClearLog()
    {
        LogOutput = string.Empty;
        HasResult = false;
        ResultMessage = string.Empty;
    }
}
