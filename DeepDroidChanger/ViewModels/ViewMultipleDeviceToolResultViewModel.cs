using CommunityToolkit.Mvvm.ComponentModel;
using DeepDroidChanger.Services;

namespace DeepDroidChanger.ViewModels;

public sealed class ViewMultipleDeviceToolResultViewModel(
    string serial, string deviceName, ILocalizationService localization) : ObservableObject
{
    private string _statusKey = "ViewMultipleDevices_ToolQueued";
    private string _details = string.Empty;
    public string Serial { get; } = serial;
    public string DeviceName { get; } = deviceName;
    public string Status => localization.GetString(_statusKey);
    public string Title => $"{DeviceName} · {Status}";
    public string Details => _details;
    internal string StatusKey => _statusKey;

    internal void Update(string statusKey, string details = "")
    {
        _statusKey = statusKey;
        _details = details;
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Details));
    }

    internal void RefreshLanguage()
    {
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(Title));
    }
}
