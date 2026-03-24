using System.ComponentModel;
using OpenGISToolbox.Services;

namespace OpenGISToolbox.Models;

public class ToolCategoryItem : INotifyPropertyChanged
{
    public ToolCategory Category { get; set; }

    public string DisplayName
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
        }
    } = string.Empty;

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
