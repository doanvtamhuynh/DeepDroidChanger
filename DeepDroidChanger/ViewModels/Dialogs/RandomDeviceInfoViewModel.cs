using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeepDroidChanger.Models;
using DeepDroidChanger.Services;

namespace DeepDroidChanger.ViewModels;

public sealed partial class RandomDeviceInfoViewModel : ObservableObject
{
    private readonly ILocalizationService _localizationService;
    private DeviceInfoApiDevice? _device;

    [ObservableProperty]
    private IReadOnlyList<RandomDeviceInfoField> _fields = Array.Empty<RandomDeviceInfoField>();

    public RandomDeviceInfoViewModel(ILocalizationService localizationService)
    {
        _localizationService = localizationService;
    }

    public event EventHandler? UpdateRequested;

    public void Initialize(DeviceInfoApiDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;

        Fields =
        [
            Field("Model", device.Model),
            Field("Gaid", device.Gaid),
            Field("Board", device.Board),
            Field("Baseband", device.Baseband),
            Field("SecurityPatch", device.SecurityPatch),
            Field("Name", device.Name),
            Field("Fingerprint", device.Fingerprint),
            Field("BuildDisplayId", device.BuildDisplayId),
            Field("Manufacturer", device.Manufacturer),
            Field("BuildDate", device.BuildDate),
            Field("BuildDateUtc", device.BuildDateUtc),
            Field("Hardware", device.Hardware),
            Field("Imei", device.Imei),
            Field("Gpu", device.Gpu),
            Field("SecondaryImei", device.Imei1),
            Field("BuildHost", device.BuildHost),
            Field("Gsf", device.Gsf),
            Field("Platform", device.Platform),
            Field("Bootloader", device.Bootloader),
            Field("Brand", device.Brand),
            Field("Product", device.Product),
            Field("Code", device.Code),
            Field("OsVersion", device.Release),
            Field("Sdk", device.Sdk),
            Field("Serial", device.Serial),
            Field("AndroidId", device.AndroidId),
            Field("Imsi", device.Imsi),
            Field("Iccid", device.Iccid),
            Field("PhoneNumber", device.SimPhoneNumber),
            Field("OperatorNumeric", device.SimOperatorNumeric),
            Field("OperatorCountry", device.SimOperatorCountry),
            Field("OperatorName", device.SimOperatorName),
            Field("WifiMac", device.WifiMacAddress),
            Field("BluetoothMac", device.BluetoothMacAddress),
            Field("BuildId", device.BuildId),
            Field("BuildIncremental", device.BuildIncremental),
            Field("BuildDescription", device.BuildDescription),
            Field("BuildFlavor", device.BuildFlavor),
            Field("BuildUser", device.BuildUser),
            Field("SettingDeviceName", device.SettingDeviceName),
            Field("SettingBluetoothName", device.SettingBluetoothName),
            Field("WifiBssid", device.WifiBssid),
            Field("WifiSsid", device.WifiSsid),
            Field("VbmetaDigest", device.VbmetaDigest)
        ];
    }

    [RelayCommand]
    private void Update()
    {
        if (_device == null)
            return;

        var values = Fields.ToDictionary(field => field.Key, field => NormalizeEditedValue(field.Value));
        _device.Model = values["Model"];
        _device.Gaid = values["Gaid"];
        _device.Board = values["Board"];
        _device.Baseband = values["Baseband"];
        _device.SecurityPatch = values["SecurityPatch"];
        _device.Name = values["Name"];
        _device.Fingerprint = values["Fingerprint"];
        _device.BuildDisplayId = values["BuildDisplayId"];
        _device.Manufacturer = values["Manufacturer"];
        _device.BuildDate = values["BuildDate"];
        _device.BuildDateUtc = values["BuildDateUtc"];
        _device.Hardware = values["Hardware"];
        _device.Imei = values["Imei"];
        _device.Gpu = values["Gpu"];
        _device.Imei1 = values["SecondaryImei"];
        _device.BuildHost = values["BuildHost"];
        _device.Gsf = values["Gsf"];
        _device.Platform = values["Platform"];
        _device.Bootloader = values["Bootloader"];
        _device.Brand = values["Brand"];
        _device.Product = values["Product"];
        _device.Code = values["Code"];
        _device.Release = values["OsVersion"];
        _device.Sdk = values["Sdk"];
        _device.Serial = values["Serial"];
        _device.AndroidId = values["AndroidId"];
        _device.Imsi = values["Imsi"];
        _device.Iccid = values["Iccid"];
        _device.SimPhoneNumber = values["PhoneNumber"];
        _device.SimOperatorNumeric = values["OperatorNumeric"];
        _device.SimOperatorCountry = values["OperatorCountry"];
        _device.SimOperatorName = values["OperatorName"];
        _device.WifiMacAddress = values["WifiMac"];
        _device.BluetoothMacAddress = values["BluetoothMac"];
        _device.BuildId = values["BuildId"];
        _device.BuildIncremental = values["BuildIncremental"];
        _device.BuildDescription = values["BuildDescription"];
        _device.BuildFlavor = values["BuildFlavor"];
        _device.BuildUser = values["BuildUser"];
        _device.SettingDeviceName = values["SettingDeviceName"];
        _device.SettingBluetoothName = values["SettingBluetoothName"];
        _device.WifiBssid = values["WifiBssid"];
        _device.WifiSsid = values["WifiSsid"];
        _device.VbmetaDigest = values["VbmetaDigest"];
        UpdateRequested?.Invoke(this, EventArgs.Empty);
    }

    private RandomDeviceInfoField Field(string name, string? value)
    {
        string displayValue = string.IsNullOrWhiteSpace(value)
            ? _localizationService.GetString("RandomDeviceInfo_NotAvailable")
            : value.Trim();
        return new RandomDeviceInfoField(
            name,
            _localizationService.GetString(string.Concat("RandomDeviceInfo_Field", name)),
            displayValue);
    }

    private string NormalizeEditedValue(string? value)
    {
        string normalized = value?.Trim() ?? string.Empty;
        return string.Equals(
            normalized,
            _localizationService.GetString("RandomDeviceInfo_NotAvailable"),
            StringComparison.Ordinal)
                ? string.Empty
                : normalized;
    }
}
