using CommunityToolkit.Mvvm.ComponentModel;

namespace DeepDroidChanger.ViewModels;

public sealed partial class DeviceInfoFormViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _model = string.Empty;

    [ObservableProperty]
    private string _serial = string.Empty;

    [ObservableProperty]
    private string _imei = string.Empty;

    [ObservableProperty]
    private string _iccid = string.Empty;

    [ObservableProperty]
    private string _imsi = string.Empty;

    [ObservableProperty]
    private string _operator = string.Empty;

    [ObservableProperty]
    private string _phoneNumber = string.Empty;

    [ObservableProperty]
    private string _mac = string.Empty;

    [ObservableProperty]
    private string _latitude = string.Empty;

    [ObservableProperty]
    private string _longitude = string.Empty;
}
