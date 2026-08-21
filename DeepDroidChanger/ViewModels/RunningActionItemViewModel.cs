using CommunityToolkit.Mvvm.ComponentModel;
using DeepDroidChanger.Services;

namespace DeepDroidChanger.ViewModels;

public sealed partial class RunningActionItemViewModel : ObservableObject
{
    public RunningActionItemViewModel(
        Guid sessionId,
        DeviceActionKind kind,
        DeviceActionSource source,
        string actionName,
        string sourceName,
        IReadOnlyList<string> devices,
        bool canStop,
        bool isStopping)
    {
        SessionId = sessionId;
        Kind = kind;
        Source = source;
        ActionName = actionName;
        SourceName = sourceName;
        Devices = devices;
        CanStop = canStop;
        IsStopping = isStopping;
    }

    public Guid SessionId { get; }
    public DeviceActionKind Kind { get; }
    public DeviceActionSource Source { get; }
    public string ActionName { get; }
    public string SourceName { get; }
    public IReadOnlyList<string> Devices { get; private set; }
    public string DeviceSummary => string.Join(", ", Devices);
    public string DeviceTooltip => DeviceSummary;

    [ObservableProperty]
    private bool _canStop;

    [ObservableProperty]
    private bool _isStopping;

    internal void UpdateDevices(IReadOnlyList<string> devices)
    {
        Devices = devices;
        OnPropertyChanged(nameof(Devices));
        OnPropertyChanged(nameof(DeviceSummary));
        OnPropertyChanged(nameof(DeviceTooltip));
    }
}
