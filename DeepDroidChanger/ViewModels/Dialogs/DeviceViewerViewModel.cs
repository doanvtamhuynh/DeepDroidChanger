using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeepDroidChanger.Services;

namespace DeepDroidChanger.ViewModels;

public sealed partial class DeviceViewerViewModel : ObservableObject
{
    private readonly ILocalizationService _localizationService;

    [ObservableProperty]
    private string _windowTitle = string.Empty;

    [ObservableProperty]
    private bool _isActionsPanelExpanded;

    public DeviceViewerViewModel(ILocalizationService localizationService)
    {
        _localizationService = localizationService;
    }

    public void Initialize(string serial, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ArgumentNullException.ThrowIfNull(name);

        WindowTitle = string.Format(
            _localizationService.GetString("DeviceViewer_WindowTitleFormat"),
            name,
            serial);
        IsActionsPanelExpanded = false;
    }

    [RelayCommand]
    private void ToggleActionsPanel()
    {
        IsActionsPanelExpanded = !IsActionsPanelExpanded;
    }
}
