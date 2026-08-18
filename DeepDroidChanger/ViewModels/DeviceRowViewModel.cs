using CommunityToolkit.Mvvm.ComponentModel;
using DeepDroidChanger.Models;
using DeepDroidChanger.Services;

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
    private bool _hasActionStopButton;
    private bool _canStopAction;
    private bool _isActionStopping;
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

    internal void RestoreProcess(string message, DeviceProcessState state)
    {
        bool repeatsCurrentMessage = string.Equals(Process, message, StringComparison.Ordinal);
        Process = message;
        ProcessState = state;
        if (repeatsCurrentMessage)
            OnPropertyChanged(nameof(Process));
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

    public bool HasActionStopButton
    {
        get => _hasActionStopButton;
        private set => SetProperty(ref _hasActionStopButton, value);
    }

    public bool CanStopAction
    {
        get => _canStopAction;
        private set => SetProperty(ref _canStopAction, value);
    }

    public bool IsActionStopping
    {
        get => _isActionStopping;
        private set => SetProperty(ref _isActionStopping, value);
    }

    internal void RestoreAction(DeviceActionOperationSnapshot? operation)
    {
        IsActionBusy = operation != null;
        HasActionStopButton = operation?.CanCancel == true;
        CanStopAction = operation is
        {
            State: DeviceActionRuntimeState.Running,
            CanCancel: true
        };
        IsActionStopping = operation?.State == DeviceActionRuntimeState.Stopping;
    }

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
