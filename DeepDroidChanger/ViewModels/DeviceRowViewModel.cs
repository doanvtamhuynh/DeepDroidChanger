using CommunityToolkit.Mvvm.ComponentModel;
using DeepDroidChanger.Models;

namespace DeepDroidChanger.ViewModels;

public sealed class DeviceRowViewModel : ObservableObject
{
    private bool _isSelected;
    private string _name;
    private string _type;
    private string _status;
    private string _process;

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
        Index = index;
        _isSelected = isSelected;
        Serial = serial;
        _name = name;
        _type = type;
        Active = active;
        ConnectionStatus = connectionStatus;
        _status = status;
        _process = process;
    }

    public int Index { get; }
    public string Serial { get; }
    public string Active { get; }
    public AdbDeviceStatus ConnectionStatus { get; set; }

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
}
