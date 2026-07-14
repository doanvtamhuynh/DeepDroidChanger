using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using DeepDroidChanger.ViewModels;
using NSubstitute;

namespace DeepDroidChanger.Tests.ViewModels.Dialogs;

[TestClass]
public sealed class RandomDeviceInfoViewModelTests
{
    [TestMethod]
    public void Initialize_MapsEveryRandomProfileFieldAndUsesLocalizedFallback()
    {
        ILocalizationService localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        var viewModel = new RandomDeviceInfoViewModel(localization);
        var profile = new DeviceInfoApiDevice
        {
            Model = "Pixel 9",
            Release = "15",
            Sdk = "35",
            WifiMacAddress = "00:11:22:33:44:55"
        };

        viewModel.Initialize(profile);

        Assert.AreEqual(typeof(DeviceInfoApiDevice).GetProperties().Length, viewModel.Fields.Count);
        Assert.HasCount(33, viewModel.Fields);
        Assert.AreEqual("RandomDeviceInfo_FieldModel", viewModel.Fields[0].Label);
        Assert.AreEqual("Pixel 9", viewModel.Fields[0].Value);
        Assert.AreEqual("15", viewModel.Fields.Single(field => field.Label == "RandomDeviceInfo_FieldOsVersion").Value);
        Assert.AreEqual("35", viewModel.Fields.Single(field => field.Label == "RandomDeviceInfo_FieldSdk").Value);
        Assert.AreEqual(
            "RandomDeviceInfo_NotAvailable",
            viewModel.Fields.Single(field => field.Label == "RandomDeviceInfo_FieldGaid").Value);
        Assert.AreEqual(
            "00:11:22:33:44:55",
            viewModel.Fields.Single(field => field.Label == "RandomDeviceInfo_FieldWifiMac").Value);
    }

    [TestMethod]
    public void UpdateCommand_AppliesEditedFieldsAndRaisesUpdateRequested()
    {
        ILocalizationService localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        var viewModel = new RandomDeviceInfoViewModel(localization);
        var profile = new DeviceInfoApiDevice { Model = "Original", Release = "14" };
        viewModel.Initialize(profile);
        bool requested = false;
        viewModel.UpdateRequested += (_, _) => requested = true;
        IReadOnlyDictionary<string, string> propertyToField = new Dictionary<string, string>
        {
            [nameof(DeviceInfoApiDevice.Model)] = "Model",
            [nameof(DeviceInfoApiDevice.Gaid)] = "Gaid",
            [nameof(DeviceInfoApiDevice.Board)] = "Board",
            [nameof(DeviceInfoApiDevice.Baseband)] = "Baseband",
            [nameof(DeviceInfoApiDevice.SecurityPatch)] = "SecurityPatch",
            [nameof(DeviceInfoApiDevice.Name)] = "Name",
            [nameof(DeviceInfoApiDevice.Fingerprint)] = "Fingerprint",
            [nameof(DeviceInfoApiDevice.BuildDisplayId)] = "BuildDisplayId",
            [nameof(DeviceInfoApiDevice.Manufacturer)] = "Manufacturer",
            [nameof(DeviceInfoApiDevice.BuildDateUtc)] = "BuildDateUtc",
            [nameof(DeviceInfoApiDevice.Hardware)] = "Hardware",
            [nameof(DeviceInfoApiDevice.Imei)] = "Imei",
            [nameof(DeviceInfoApiDevice.Gpu)] = "Gpu",
            [nameof(DeviceInfoApiDevice.Imei1)] = "SecondaryImei",
            [nameof(DeviceInfoApiDevice.BuildHost)] = "BuildHost",
            [nameof(DeviceInfoApiDevice.Gsf)] = "Gsf",
            [nameof(DeviceInfoApiDevice.Platform)] = "Platform",
            [nameof(DeviceInfoApiDevice.Bootloader)] = "Bootloader",
            [nameof(DeviceInfoApiDevice.Brand)] = "Brand",
            [nameof(DeviceInfoApiDevice.Product)] = "Product",
            [nameof(DeviceInfoApiDevice.Code)] = "Code",
            [nameof(DeviceInfoApiDevice.Release)] = "OsVersion",
            [nameof(DeviceInfoApiDevice.Sdk)] = "Sdk",
            [nameof(DeviceInfoApiDevice.Serial)] = "Serial",
            [nameof(DeviceInfoApiDevice.AndroidId)] = "AndroidId",
            [nameof(DeviceInfoApiDevice.Imsi)] = "Imsi",
            [nameof(DeviceInfoApiDevice.Iccid)] = "Iccid",
            [nameof(DeviceInfoApiDevice.SimPhoneNumber)] = "PhoneNumber",
            [nameof(DeviceInfoApiDevice.SimOperatorNumeric)] = "OperatorNumeric",
            [nameof(DeviceInfoApiDevice.SimOperatorCountry)] = "OperatorCountry",
            [nameof(DeviceInfoApiDevice.SimOperatorName)] = "OperatorName",
            [nameof(DeviceInfoApiDevice.WifiMacAddress)] = "WifiMac",
            [nameof(DeviceInfoApiDevice.BluetoothMacAddress)] = "BluetoothMac"
        };
        foreach (string fieldKey in propertyToField.Values)
            viewModel.Fields.Single(field => field.Key == fieldKey).Value = string.Concat(" edited-", fieldKey, " ");

        Assert.AreEqual("Original", profile.Model, "Editing inputs must not apply before Update.");

        viewModel.UpdateCommand.Execute(null);

        Assert.IsTrue(requested);
        Assert.HasCount(typeof(DeviceInfoApiDevice).GetProperties().Length, propertyToField);
        foreach ((string propertyName, string fieldKey) in propertyToField)
        {
            object? actualValue = typeof(DeviceInfoApiDevice).GetProperty(propertyName)?.GetValue(profile);
            Assert.AreEqual(string.Concat("edited-", fieldKey), actualValue, propertyName);
        }
    }
}
