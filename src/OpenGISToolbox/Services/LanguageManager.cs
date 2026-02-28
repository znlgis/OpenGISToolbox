using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Markup.Xaml.Styling;

namespace OpenGISToolbox.Services;

public class LanguageManager : INotifyPropertyChanged
{
    private static readonly Lazy<LanguageManager> _instance = new(() => new LanguageManager());
    public static LanguageManager Instance => _instance.Value;

    private string _currentLanguage = "en";
    private ResourceInclude? _currentResource;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? LanguageChanged;

    public string CurrentLanguage
    {
        get => _currentLanguage;
        private set
        {
            if (_currentLanguage == value) return;
            _currentLanguage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
        }
    }

    public void SwitchLanguage(string language)
    {
        if (_currentLanguage == language) return;

        var app = Application.Current;
        if (app == null) return;

        var uri = new Uri($"avares://OpenGISToolbox/Assets/Lang/{language}.axaml");
        var newResource = new ResourceInclude(uri) { Source = uri };

        if (_currentResource != null)
            app.Resources.MergedDictionaries.Remove(_currentResource);

        app.Resources.MergedDictionaries.Add(newResource);
        _currentResource = newResource;

        CurrentLanguage = language;
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Initialize(string language = "en")
    {
        SwitchLanguage(language);
    }
}
