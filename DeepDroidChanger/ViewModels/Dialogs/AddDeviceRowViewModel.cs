using CommunityToolkit.Mvvm.ComponentModel;
using DeepDroidChanger.Helpers;

namespace DeepDroidChanger.ViewModels;

public sealed class AddDeviceRowViewModel : ObservableObject
{
    private bool _isSelected;
    private string _name;
    private string _type;

    public AddDeviceRowViewModel(string serial, string type)
    {
        Serial = serial;
        _type = DeviceTypeHelper.Normalize(type);
        _name = DeviceTypeHelper.GetDefaultName(_type);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string Serial { get; }

    public string Type
    {
        get => _type;
        set
        {
            string normalizedType = DeviceTypeHelper.Normalize(value);
            string oldType = _type;
            if (!SetProperty(ref _type, normalizedType))
                return;

            if (Name == DeviceTypeHelper.GetDefaultName(oldType) || string.IsNullOrEmpty(Name))
                Name = DeviceTypeHelper.GetDefaultName(normalizedType);
        }
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }
}
