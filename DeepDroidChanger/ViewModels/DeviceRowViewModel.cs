using CommunityToolkit.Mvvm.ComponentModel;
using DeepDroidChanger.Models;

namespace DeepDroidChanger.ViewModels;

public sealed class DeviceRowViewModel : ObservableObject
{
    private int _index;
    private bool _isSelected;
    private string _name;
    private string _type;
    private string _status;
    private string _process;
    private DeviceProcessState _processState;
    private AdbDeviceStatus _connectionStatus;
    private bool _isActionBusy;
    private bool _isGmsDisabled;
    private bool _isPlayStoreDisabled;
    private bool _isWifiEnabled;
    private bool _isContextMenuStateLoading;

    public DeviceRowViewModel(
        int index,
        bool isSelected,
        string serial,
        string name,
        string type,
        string active,
        AdbDeviceStatus connectionStatus,
        string status,
        string process)
    {
        _index = index;
        _isSelected = isSelected;
        Serial = serial;
        _name = name;
        _type = type;
        Active = active;
        _connectionStatus = connectionStatus;
        _status = status;
        _process = process;
        _processState = DeviceProcessState.Ready;
    }

    public int Index
    {
        get => _index;
        private set => SetProperty(ref _index, value);
    }
    public string Serial { get; }
    public string Active { get; }
    public AdbDeviceStatus ConnectionStatus
    {
        get => _connectionStatus;
        set => SetProperty(ref _connectionStatus, value);
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string Type
    {
        get => _type;
        set => SetProperty(ref _type, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public string Process
    {
        get => _process;
        set => SetProperty(ref _process, value);
    }

    public DeviceProcessState ProcessState
    {
        get => _processState;
        private set => SetProperty(ref _processState, value);
    }

    internal void SetProcess(string message, string resourceKey)
    {
        DeviceProcessState nextState = GetProcessState(resourceKey);
        if (nextState == DeviceProcessState.Ready
            && ProcessState is DeviceProcessState.Succeeded
                or DeviceProcessState.Failed
                or DeviceProcessState.Canceled)
        {
            return;
        }

        bool repeatsCurrentMessage = string.Equals(Process, message, StringComparison.Ordinal);
        Process = message;
        ProcessState = nextState;
        if (repeatsCurrentMessage)
            OnPropertyChanged(nameof(Process));
    }

    internal void RestoreProcess(string message, DeviceProcessState state)
    {
        Process = message;
        ProcessState = state;
    }

    private static DeviceProcessState GetProcessState(string resourceKey)
    {
        if (string.Equals(resourceKey, "Log_Ready", StringComparison.Ordinal))
            return DeviceProcessState.Ready;

        if (resourceKey.Contains("Partial", StringComparison.Ordinal))
            return DeviceProcessState.Failed;

        if (resourceKey.Contains("Canceled", StringComparison.Ordinal))
            return DeviceProcessState.Canceled;

        if (resourceKey.Contains("Success", StringComparison.Ordinal)
            || resourceKey.EndsWith("Enabled", StringComparison.Ordinal)
            || resourceKey.EndsWith("Disabled", StringComparison.Ordinal)
            || resourceKey.EndsWith("Sent", StringComparison.Ordinal)
            || resourceKey.EndsWith("Saved", StringComparison.Ordinal)
            || resourceKey.EndsWith("NoOutput", StringComparison.Ordinal)
            || resourceKey.EndsWith("CompleteFormat", StringComparison.Ordinal))
        {
            return DeviceProcessState.Succeeded;
        }

        if (resourceKey.Contains("Failed", StringComparison.Ordinal)
            || resourceKey.Contains("Failure", StringComparison.Ordinal)
            || resourceKey.EndsWith("Required", StringComparison.Ordinal)
            || resourceKey.EndsWith("DeviceMustBeOnline", StringComparison.Ordinal)
            || resourceKey.EndsWith("NoFiles", StringComparison.Ordinal)
            || resourceKey.EndsWith("NoInternet", StringComparison.Ordinal)
            || resourceKey.EndsWith("Empty", StringComparison.Ordinal)
            || resourceKey.EndsWith("AlreadyExists", StringComparison.Ordinal)
            || resourceKey.EndsWith("VersionDowngrade", StringComparison.Ordinal)
            || resourceKey.EndsWith("UnknownResult", StringComparison.Ordinal)
            || resourceKey.Contains("Missing", StringComparison.Ordinal)
            || resourceKey.Contains("Invalid", StringComparison.Ordinal)
            || resourceKey.Contains("Unsupported", StringComparison.Ordinal)
            || resourceKey.Contains("Insufficient", StringComparison.Ordinal)
            || resourceKey.Contains("NoMatching", StringComparison.Ordinal))
        {
            return DeviceProcessState.Failed;
        }

        return DeviceProcessState.InProgress;
    }

    public bool IsActionBusy
    {
        get => _isActionBusy;
        set
        {
            if (SetProperty(ref _isActionBusy, value))
                OnPropertyChanged(nameof(CanEdit));
        }
    }

    public bool CanEdit => !IsActionBusy;

    internal void UpdateSnapshot(
        int index,
        string name,
        string type,
        AdbDeviceStatus connectionStatus,
        string status,
        bool isActionBusy)
    {
        Index = index;
        Name = name;
        Type = type;
        ConnectionStatus = connectionStatus;
        Status = status;
        IsActionBusy = isActionBusy;
    }

    public bool IsGmsDisabled
    {
        get => _isGmsDisabled;
        set => SetProperty(ref _isGmsDisabled, value);
    }

    public bool IsPlayStoreDisabled
    {
        get => _isPlayStoreDisabled;
        set => SetProperty(ref _isPlayStoreDisabled, value);
    }

    public bool IsWifiEnabled
    {
        get => _isWifiEnabled;
        set => SetProperty(ref _isWifiEnabled, value);
    }

    public bool IsContextMenuStateLoading
    {
        get => _isContextMenuStateLoading;
        set
        {
            if (SetProperty(ref _isContextMenuStateLoading, value))
                OnPropertyChanged(nameof(CanToggleContextMenuActions));
        }
    }

    public bool CanToggleContextMenuActions => !IsContextMenuStateLoading;
}
