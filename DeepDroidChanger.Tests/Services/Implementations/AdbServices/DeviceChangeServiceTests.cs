using DeepDroidChanger.Constants;
using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DeepDroidChanger.Tests.Services.Implementations.AdbServices;

[TestClass]
public sealed partial class DeviceChangeServiceTests
{
    private const string CurrentAndroidId = "33537c391caed62e";
    private const string RegeneratedAndroidId = "94ab6d2e18f047c3";

    [TestMethod]
    public async Task ChangeSimAsync_WritesOnlySimPropertiesAndRebootsWithoutCleanup()
    {
        IAdbCommandService adb = CreateRootedAdb();
        IDeviceDataCleanupService cleanup = Substitute.For<IDeviceDataCleanupService>();
        DeviceChangeService service = CreateService(adb, cleanup);
        var profile = new SimProfile
        {
            Iccid = "8984041234567890123",
            Imsi = "452041234567890",
            PhoneNumber = "+84901234567",
            OperatorName = "Viettel",
            OperatorCountry = "vn",
            OperatorNumeric = "45204"
        };

        await service.ChangeSimAsync("SERIAL", profile, CancellationToken.None);

        await adb.Received(1).SetPropertyAsync(
            "SERIAL",
            DeviceSpoofPropertyConstants.SimEnabled,
            "1",
            Arg.Any<CancellationToken>());
        await adb.Received(1).SetPropertyAsync(
            "SERIAL",
            DeviceSpoofPropertyConstants.SimIccid,
            profile.Iccid,
            Arg.Any<CancellationToken>());
        await adb.Received(1).SetPropertyAsync(
            "SERIAL",
            DeviceSpoofPropertyConstants.SimOperatorNumeric,
            profile.OperatorNumeric,
            Arg.Any<CancellationToken>());
        await adb.DidNotReceive().SetPropertyAsync(
            "SERIAL",
            DeviceSpoofPropertyConstants.ProductModel,
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await adb.DidNotReceiveWithAnyArgs().SetWifiAsync(default!, default, default);
        await cleanup.DidNotReceiveWithAnyArgs().CleanAsync(default!, default!, default);
        await cleanup.DidNotReceiveWithAnyArgs().CleanPreservingSsaidAsync(default!, default!, default);
        await cleanup.DidNotReceiveWithAnyArgs().CleanPostRebootAsync(default!, default);
        await cleanup.DidNotReceiveWithAnyArgs().DeleteSsaidAsync(default!, default);
        await adb.Received(1).RebootAsync("SERIAL", Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ChangeWithoutWipeAsync_DefaultMode_DeletesSsaidWithoutChangingAndroidId()
    {
        IAdbCommandService adb = CreateRootedAdb();
        IDeviceDataCleanupService cleanup = Substitute.For<IDeviceDataCleanupService>();
        DeviceChangeService service = CreateService(adb, cleanup);
        DeviceInfoApiDevice profile = CreateProfile();

        await service.ChangeWithoutWipeAsync(
            "SERIAL",
            profile,
            true,
            new DeviceChangeOptions
            {
                UseDefaultMode = true,
                ChangeAndroidId = true
            },
            null,
            CancellationToken.None);

        await cleanup.DidNotReceiveWithAnyArgs().CleanAsync(default!, default!, default);
        await cleanup.DidNotReceiveWithAnyArgs().CleanPreservingSsaidAsync(default!, default!, default);
        await cleanup.DidNotReceiveWithAnyArgs().CleanPostRebootAsync(default!, default);
        await cleanup.Received(1).DeleteSsaidAsync("SERIAL", Arg.Any<CancellationToken>());
        await adb.Received(1).SetPropertyAsync(
            "SERIAL",
            DeviceSpoofPropertyConstants.ProductModel,
            profile.Model!,
            Arg.Any<CancellationToken>());
        await adb.Received(1).SetPropertyAsync(
            "SERIAL",
            DeviceSpoofPropertyConstants.SimIccid,
            profile.Iccid,
            Arg.Any<CancellationToken>());
        await adb.DidNotReceive().PutSettingAsync(
            "SERIAL",
            "secure",
            "android_id",
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await adb.DidNotReceive().DeleteSettingAsync(
            "SERIAL",
            "secure",
            "android_id",
            Arg.Any<CancellationToken>());
        await adb.DidNotReceive().GetSettingAsync(
            "SERIAL",
            "secure",
            "android_id",
            Arg.Any<CancellationToken>());
        await adb.Received(1).RebootAsync("SERIAL", Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ChangeWithoutWipeAsync_AdvancedAndroidIdEnabled_DeletesSettingAfterSsaidAndVerifiesNewId()
    {
        IAdbCommandService adb = CreateRootedAdb();
        adb.GetSettingAsync("SERIAL", "secure", "android_id", Arg.Any<CancellationToken>())
            .Returns(CurrentAndroidId, RegeneratedAndroidId);
        IDeviceDataCleanupService cleanup = Substitute.For<IDeviceDataCleanupService>();
        DeviceChangeService service = CreateService(adb, cleanup);

        await service.ChangeWithoutWipeAsync(
            "SERIAL",
            CreateProfile(),
            true,
            new DeviceChangeOptions
            {
                UseDefaultMode = false,
                ChangeAndroidId = true
            },
            null,
            CancellationToken.None);

        await adb.Received(1).DeleteSettingAsync(
            "SERIAL",
            "secure",
            "android_id",
            Arg.Any<CancellationToken>());
        await adb.Received(2).GetSettingAsync(
            "SERIAL",
            "secure",
            "android_id",
            Arg.Any<CancellationToken>());
        Received.InOrder(() =>
        {
            adb.GetSettingAsync("SERIAL", "secure", "android_id", Arg.Any<CancellationToken>());
            cleanup.DeleteSsaidAsync("SERIAL", Arg.Any<CancellationToken>());
            adb.DeleteSettingAsync("SERIAL", "secure", "android_id", Arg.Any<CancellationToken>());
            adb.SetPropertyAsync(
                "SERIAL",
                DeviceSpoofPropertyConstants.ProductModel,
                Arg.Any<string>(),
                Arg.Any<CancellationToken>());
            adb.RebootAsync("SERIAL", Arg.Any<CancellationToken>());
            adb.GetSettingAsync("SERIAL", "secure", "android_id", Arg.Any<CancellationToken>());
        });
    }

    [TestMethod]
    public async Task ChangeWithoutWipeAsync_AdvancedAndroidIdDisabled_DeletesSsaidWithoutChangingAndroidId()
    {
        IAdbCommandService adb = CreateRootedAdb();
        IDeviceDataCleanupService cleanup = Substitute.For<IDeviceDataCleanupService>();
        DeviceChangeService service = CreateService(adb, cleanup);

        await service.ChangeWithoutWipeAsync(
            "SERIAL",
            CreateProfile(),
            true,
            new DeviceChangeOptions
            {
                UseDefaultMode = false,
                ChangeAndroidId = false
            },
            null,
            CancellationToken.None);

        await cleanup.Received(1).DeleteSsaidAsync("SERIAL", Arg.Any<CancellationToken>());
        await adb.DidNotReceive().DeleteSettingAsync(
            "SERIAL",
            "secure",
            "android_id",
            Arg.Any<CancellationToken>());
        await adb.DidNotReceive().GetSettingAsync(
            "SERIAL",
            "secure",
            "android_id",
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task WipeWithoutChangeAsync_CleansWithRequestedOptionsWithoutWritingIdentity()
    {
        IAdbCommandService adb = CreateRootedAdb();
        IDeviceDataCleanupService cleanup = Substitute.For<IDeviceDataCleanupService>();
        DeviceChangeService service = CreateService(adb, cleanup);
        var options = new DeviceChangeOptions
        {
            UseDefaultMode = false,
            ChangeAndroidId = true,
            ClearAllPackages = false,
            ClearSelectedPackages = true,
            SelectedPackages = ["com.example.app"],
            ClearGoogleAccounts = false
        };

        await service.WipeWithoutChangeAsync("SERIAL", options, null, CancellationToken.None);

        await cleanup.Received(1).CleanPreservingSsaidAsync(
            "SERIAL",
            options,
            Arg.Any<CancellationToken>());
        await cleanup.DidNotReceiveWithAnyArgs().CleanAsync(default!, default!, default);
        await cleanup.DidNotReceiveWithAnyArgs().DeleteSsaidAsync(default!, default);
        await cleanup.Received(1).CleanPostRebootAsync(
            "SERIAL",
            Arg.Any<CancellationToken>());
        await adb.DidNotReceiveWithAnyArgs().SetPropertyAsync(default!, default!, default!, default);
        await adb.DidNotReceiveWithAnyArgs().PutSettingAsync(default!, default!, default!, default!, default);
        await adb.DidNotReceiveWithAnyArgs().DeleteSettingAsync(default!, default!, default!, default);
        await adb.Received(1).RebootAsync("SERIAL", Arg.Any<CancellationToken>());
        Received.InOrder(() =>
        {
            cleanup.CleanPreservingSsaidAsync("SERIAL", options, Arg.Any<CancellationToken>());
            adb.RebootAsync("SERIAL", Arg.Any<CancellationToken>());
            adb.RunAdbShellAsync(
                "SERIAL",
                DeviceChangeConstants.BootCompletedCommand,
                Arg.Any<CancellationToken>());
            cleanup.CleanPostRebootAsync("SERIAL", Arg.Any<CancellationToken>());
        });
    }

    [TestMethod]
    public async Task ChangeAsync_DefaultMode_ClearsDataAndAppliesFullGeneratedProfile()
    {
        IAdbCommandService adb = CreateRootedAdb();
        IDeviceDataCleanupService cleanup = Substitute.For<IDeviceDataCleanupService>();
        DeviceChangeService service = CreateService(adb, cleanup);
        DeviceInfoApiDevice profile = CreateProfile();
        IProgress<DeviceChangeStage> progress = Substitute.For<IProgress<DeviceChangeStage>>();
        var options = new DeviceChangeOptions
        {
            UseDefaultMode = true,
            ChangeAndroidId = true
        };

        await service.ChangeAsync("SERIAL", profile, true, options, progress, CancellationToken.None);

        await adb.Received(1).SetPropertyAsync(
            "SERIAL",
            DeviceSpoofPropertyConstants.ProductModel,
            profile.Model!,
            Arg.Any<CancellationToken>());
        await adb.Received(1).SetPropertyAsync(
            "SERIAL",
            DeviceSpoofPropertyConstants.BuildDate,
            profile.BuildDate!,
            Arg.Any<CancellationToken>());
        await adb.Received(1).SetPropertyAsync(
            "SERIAL",
            DeviceSpoofPropertyConstants.BuildDateUtc,
            profile.BuildDateUtc!,
            Arg.Any<CancellationToken>());
        await adb.Received(1).SetPropertyAsync(
            "SERIAL",
            DeviceSpoofPropertyConstants.Bootloader,
            profile.Bootloader!,
            Arg.Any<CancellationToken>());
        await adb.DidNotReceive().PutSettingAsync(
            "SERIAL",
            "secure",
            "android_id",
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await adb.DidNotReceive().DeleteSettingAsync(
            "SERIAL",
            "secure",
            "android_id",
            Arg.Any<CancellationToken>());
        await adb.Received(1).SetPropertyAsync(
            "SERIAL",
            DeviceSpoofPropertyConstants.WifiMac,
            profile.WifiMacAddress,
            Arg.Any<CancellationToken>());
        await adb.Received(1).SetPropertyAsync(
            "SERIAL",
            DeviceSpoofPropertyConstants.SimEnabled,
            "1",
            Arg.Any<CancellationToken>());
        await adb.DidNotReceive().SetPropertyAsync(
            "SERIAL",
            DeviceSpoofPropertyConstants.BypassReadOnlyProperties,
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await cleanup.Received(1).CleanAsync("SERIAL", options, Arg.Any<CancellationToken>());
        await cleanup.Received(1).CleanPostRebootAsync(
            "SERIAL",
            Arg.Any<CancellationToken>());
        Received.InOrder(() =>
        {
            progress.Report(DeviceChangeStage.ApplyingProfile);
            adb.SetPropertyAsync(
                "SERIAL",
                DeviceSpoofPropertyConstants.ProductModel,
                profile.Model!,
                Arg.Any<CancellationToken>());
            progress.Report(DeviceChangeStage.ClearingData);
            cleanup.CleanAsync("SERIAL", options, Arg.Any<CancellationToken>());
        });
        await adb.Received(1).RebootAsync("SERIAL", Arg.Any<CancellationToken>());
        Received.InOrder(() =>
        {
            cleanup.CleanAsync("SERIAL", options, Arg.Any<CancellationToken>());
            adb.RebootAsync("SERIAL", Arg.Any<CancellationToken>());
            adb.RunAdbShellAsync(
                "SERIAL",
                DeviceChangeConstants.BootCompletedCommand,
                Arg.Any<CancellationToken>());
            cleanup.CleanPostRebootAsync("SERIAL", Arg.Any<CancellationToken>());
        });
        await adb.DidNotReceive().GetSettingAsync(
            "SERIAL",
            "secure",
            "android_id",
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ChangeAsync_ProfileStillContainsOldValueAfterReboot_ThrowsVerificationFailure()
    {
        IAdbCommandService adb = CreateRootedAdb();
        adb.GetPropertyAsync(
                "SERIAL",
                "ro.product.model",
                Arg.Any<CancellationToken>())
            .Returns("OLD-MODEL");
        DeviceChangeService service = CreateService(adb);

        InvalidOperationException exception =
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                service.ChangeAsync(
                    "SERIAL",
                    CreateProfile(),
                    changeSim: true,
                    new DeviceChangeOptions { UseDefaultMode = true },
                    progress: null,
                    CancellationToken.None));

        StringAssert.Contains(exception.Message, "Device profile verification failed");
        StringAssert.Contains(exception.Message, DeviceSpoofPropertyConstants.ProductModel);
        await adb.Received(1).RebootAsync("SERIAL", Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ChangeAsync_DefaultMode_ChangeSimDisabled_ClearsSimSpoofProperties()
    {
        IAdbCommandService adb = CreateRootedAdb();
        DeviceInfoApiDevice profile = CreateProfile();
        adb.GetSettingAsync("SERIAL", "secure", "android_id", Arg.Any<CancellationToken>())
            .Returns(CurrentAndroidId, "9999888877776666");
        DeviceChangeService service = CreateService(adb);

        await service.ChangeAsync(
            "SERIAL",
            profile,
            false,
            new DeviceChangeOptions { UseDefaultMode = true },
            null,
            CancellationToken.None);

        await adb.Received(1).SetPropertyAsync(
            "SERIAL",
            DeviceSpoofPropertyConstants.SimEnabled,
            "0",
            Arg.Any<CancellationToken>());
        await adb.Received(1).SetPropertyAsync(
            "SERIAL",
            DeviceSpoofPropertyConstants.SimIccid,
            string.Empty,
            Arg.Any<CancellationToken>());
        await adb.DidNotReceive().SetPropertyAsync(
            "SERIAL",
            DeviceSpoofPropertyConstants.SimIccid,
            profile.Iccid,
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ChangeAsync_AdvancedCleanup_RunsConfiguredCleanup()
    {
        IAdbCommandService adb = CreateRootedAdb();
        IDeviceDataCleanupService cleanup = Substitute.For<IDeviceDataCleanupService>();
        DeviceChangeService service = CreateService(adb, cleanup);
        var options = new DeviceChangeOptions
        {
            UseDefaultMode = false,
            ClearAllPackages = false,
            ClearSelectedPackages = true,
            SelectedPackages = ["com.example.app"],
            ClearGoogleAccounts = false
        };

        await service.ChangeAsync("SERIAL", CreateProfile(), true, options, null, CancellationToken.None);

        await cleanup.Received(1).CleanAsync("SERIAL", options, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ChangeAsync_AdvancedMacDisabled_DoesNotWriteMacPropertiesOrDisableWifi()
    {
        IAdbCommandService adb = CreateRootedAdb();
        DeviceChangeService service = CreateService(adb);

        await service.ChangeAsync(
            "SERIAL",
            CreateProfile(),
            true,
            new DeviceChangeOptions
            {
                UseDefaultMode = false,
                ClearAllPackages = false,
                ChangeMacAddress = false
            },
            null,
            CancellationToken.None);

        await adb.Received(1).SetWifiAsync("SERIAL", false, Arg.Any<CancellationToken>());
        await adb.DidNotReceive().SetWifiAsync("SERIAL", true, Arg.Any<CancellationToken>());
        await adb.DidNotReceive().SetPropertyAsync(
            "SERIAL",
            DeviceSpoofPropertyConstants.WifiMac,
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await adb.DidNotReceive().SetPropertyAsync(
            "SERIAL",
            DeviceSpoofPropertyConstants.BluetoothMac,
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ChangeAsync_AdvancedMacEnabled_WritesWifiBluetoothBssidAndRandomizationSetting()
    {
        IAdbCommandService adb = CreateRootedAdb();
        DeviceInfoApiDevice profile = CreateProfile();
        DeviceChangeService service = CreateService(adb);

        await service.ChangeAsync(
            "SERIAL",
            profile,
            true,
            new DeviceChangeOptions
            {
                UseDefaultMode = false,
                ChangeMacAddress = true,
                ClearAllPackages = false,
                ClearGoogleAccounts = false
            },
            null,
            CancellationToken.None);

        await adb.Received(1).SetWifiAsync("SERIAL", false, Arg.Any<CancellationToken>());
        await adb.Received(1).SetPropertyAsync(
            "SERIAL",
            DeviceSpoofPropertyConstants.WifiMac,
            profile.WifiMacAddress,
            Arg.Any<CancellationToken>());
        await adb.Received(1).SetPropertyAsync(
            "SERIAL",
            DeviceSpoofPropertyConstants.BluetoothMac,
            profile.BluetoothMacAddress,
            Arg.Any<CancellationToken>());
        await adb.Received(1).SetPropertyAsync(
            "SERIAL",
            DeviceSpoofPropertyConstants.WifiBssid,
            profile.WifiBssid,
            Arg.Any<CancellationToken>());
        await adb.Received(1).PutSettingAsync(
            "SERIAL",
            "global",
            "non_persistent_mac_randomization_force_enabled",
            "1",
            Arg.Any<CancellationToken>());
        await adb.Received(1).PutSettingAsync(
            "SERIAL",
            "global",
            "device_name",
            profile.Name!,
            Arg.Any<CancellationToken>());
        await adb.Received(1).PutSettingAsync(
            "SERIAL",
            "secure",
            "bluetooth_name",
            profile.Name!,
            Arg.Any<CancellationToken>());
        await adb.Received(1).PutSettingAsync(
            "SERIAL",
            "global",
            "wifi_p2p_device_name",
            profile.Name!,
            Arg.Any<CancellationToken>());
        await adb.Received(1).DeleteSettingAsync(
            "SERIAL",
            "secure",
            "bluetooth_address",
            Arg.Any<CancellationToken>());
        await adb.Received(1).DeleteSettingAsync(
            "SERIAL",
            "secure",
            "bluetooth_addr_valid",
            Arg.Any<CancellationToken>());
        await adb.Received(1).RunAdbShellAsync(
            "SERIAL",
            DeviceChangeConstants.DisableBluetoothCommand,
            Arg.Any<CancellationToken>());
        await adb.DidNotReceive().SetWifiAsync("SERIAL", true, Arg.Any<CancellationToken>());
        Received.InOrder(() =>
        {
            adb.SetWifiAsync("SERIAL", false, Arg.Any<CancellationToken>());
            adb.SetPropertyAsync(
                "SERIAL",
                DeviceSpoofPropertyConstants.ProductBrand,
                profile.Brand!,
                Arg.Any<CancellationToken>());
            adb.RebootAsync("SERIAL", Arg.Any<CancellationToken>());
        });
    }

    [TestMethod]
    public async Task ChangeAsync_CleanupFailure_AppliesProfileButStopsBeforeRebooting()
    {
        IAdbCommandService adb = CreateRootedAdb();
        IDeviceDataCleanupService cleanup = Substitute.For<IDeviceDataCleanupService>();
        var failure = new InvalidOperationException("cleanup failed");
        cleanup.CleanAsync(
                "SERIAL",
                Arg.Any<DeviceChangeOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException(failure));
        DeviceChangeService service = CreateService(adb, cleanup);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            service.ChangeAsync(
                "SERIAL",
                CreateProfile(),
                true,
                new DeviceChangeOptions
                {
                    UseDefaultMode = false,
                    ClearAllPackages = false,
                    ClearGoogleAccounts = false
                },
                null,
                CancellationToken.None));

        await adb.Received(1).SetPropertyAsync(
            "SERIAL",
            DeviceSpoofPropertyConstants.ProductModel,
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await adb.DidNotReceive().SetPropertyAsync(
            "SERIAL",
            DeviceSpoofPropertyConstants.BypassReadOnlyProperties,
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await adb.DidNotReceiveWithAnyArgs().RebootAsync(default!, default);
    }

    [TestMethod]
    public async Task ChangeAsync_NonRootDevice_StopsBeforeChangingIdentityOrData()
    {
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        adb.RunAdbAsync("SERIAL", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, string.Empty, string.Empty));
        adb.RunAdbShellAsync("SERIAL", "id -u", Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, "2000", string.Empty));
        IDeviceDataCleanupService cleanup = Substitute.For<IDeviceDataCleanupService>();
        DeviceChangeService service = CreateService(adb, cleanup);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            service.ChangeAsync(
                "SERIAL",
                CreateProfile(),
                true,
                new DeviceChangeOptions(),
                null,
                CancellationToken.None));

        await adb.DidNotReceiveWithAnyArgs().SetPropertyAsync(default!, default!, default!, default);
        await cleanup.DidNotReceiveWithAnyArgs().CleanAsync(default!, default!, default);
        await adb.DidNotReceiveWithAnyArgs().RebootAsync(default!, default);
    }

    [TestMethod]
    public async Task ChangeAsync_AdvancedChangeSimDisabled_ClearsSimProperties()
    {
        IAdbCommandService adb = CreateRootedAdb();
        DeviceChangeService service = CreateService(adb);

        await service.ChangeAsync(
            "SERIAL",
            CreateProfile(),
            false,
            new DeviceChangeOptions
            {
                UseDefaultMode = false,
                ClearAllPackages = false
            },
            null,
            CancellationToken.None);

        await adb.Received(1).SetPropertyAsync(
            "SERIAL",
            DeviceSpoofPropertyConstants.SimEnabled,
            "0",
            Arg.Any<CancellationToken>());
        await adb.Received(1).SetPropertyAsync(
            "SERIAL",
            DeviceSpoofPropertyConstants.SimImsi,
            string.Empty,
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ChangeAsync_AdvancedChangeSimEnabled_WritesGeneratedSimProperties()
    {
        IAdbCommandService adb = CreateRootedAdb();
        DeviceInfoApiDevice profile = CreateProfile();
        DeviceChangeService service = CreateService(adb);

        await service.ChangeAsync(
            "SERIAL",
            profile,
            true,
            new DeviceChangeOptions
            {
                UseDefaultMode = false,
                ClearAllPackages = false,
                ClearGoogleAccounts = false
            },
            null,
            CancellationToken.None);

        await adb.Received(1).SetPropertyAsync(
            "SERIAL",
            DeviceSpoofPropertyConstants.SimEnabled,
            "1",
            Arg.Any<CancellationToken>());
        await adb.Received(1).SetPropertyAsync(
            "SERIAL",
            DeviceSpoofPropertyConstants.SimIccid,
            profile.Iccid,
            Arg.Any<CancellationToken>());
        await adb.Received(1).SetPropertyAsync(
            "SERIAL",
            DeviceSpoofPropertyConstants.SimImsi,
            profile.Imsi,
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ChangeAsync_AdvancedAndroidIdEnabled_DeletesSettingAfterCleanupAndVerifiesNewId()
    {
        IAdbCommandService adb = CreateRootedAdb();
        DeviceInfoApiDevice profile = CreateProfile();
        adb.GetSettingAsync("SERIAL", "secure", "android_id", Arg.Any<CancellationToken>())
            .Returns(CurrentAndroidId, RegeneratedAndroidId);
        IDeviceDataCleanupService cleanup = Substitute.For<IDeviceDataCleanupService>();
        DeviceChangeService service = CreateService(adb, cleanup);

        await service.ChangeAsync(
            "SERIAL",
            profile,
            true,
            new DeviceChangeOptions
            {
                UseDefaultMode = false,
                ChangeAndroidId = true,
                ClearAllPackages = false,
                ClearGoogleAccounts = false
            },
            null,
            CancellationToken.None);

        await cleanup.Received(1).CleanAsync(
            "SERIAL",
            Arg.Any<DeviceChangeOptions>(),
            Arg.Any<CancellationToken>());
        await adb.DidNotReceive().PutSettingAsync(
            "SERIAL",
            "secure",
            "android_id",
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await adb.Received(1).DeleteSettingAsync(
            "SERIAL",
            "secure",
            "android_id",
            Arg.Any<CancellationToken>());
        await adb.Received(2).GetSettingAsync(
            "SERIAL",
            "secure",
            "android_id",
            Arg.Any<CancellationToken>());
        Received.InOrder(() =>
        {
            adb.GetSettingAsync("SERIAL", "secure", "android_id", Arg.Any<CancellationToken>());
            adb.SetPropertyAsync(
                "SERIAL",
                DeviceSpoofPropertyConstants.ProductModel,
                Arg.Any<string>(),
                Arg.Any<CancellationToken>());
            cleanup.CleanAsync("SERIAL", Arg.Any<DeviceChangeOptions>(), Arg.Any<CancellationToken>());
            adb.DeleteSettingAsync("SERIAL", "secure", "android_id", Arg.Any<CancellationToken>());
            adb.RebootAsync("SERIAL", Arg.Any<CancellationToken>());
            adb.GetSettingAsync("SERIAL", "secure", "android_id", Arg.Any<CancellationToken>());
        });
    }

    [TestMethod]
    public async Task ChangeAsync_AdvancedAndroidIdDisabled_CleansSsaidWithoutReadingOrDeletingSetting()
    {
        IAdbCommandService adb = CreateRootedAdb();
        IDeviceDataCleanupService cleanup = Substitute.For<IDeviceDataCleanupService>();
        DeviceChangeService service = CreateService(adb, cleanup);
        var options = new DeviceChangeOptions
        {
            UseDefaultMode = false,
            ChangeAndroidId = false,
            ClearAllPackages = false,
            ClearGoogleAccounts = false
        };

        await service.ChangeAsync(
            "SERIAL",
            CreateProfile(),
            true,
            options,
            null,
            CancellationToken.None);

        await cleanup.Received(1).CleanAsync("SERIAL", options, Arg.Any<CancellationToken>());
        await adb.DidNotReceive().DeleteSettingAsync(
            "SERIAL",
            "secure",
            "android_id",
            Arg.Any<CancellationToken>());
        await adb.DidNotReceive().GetSettingAsync(
            "SERIAL",
            "secure",
            "android_id",
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ChangeAsync_UnchangedAndroidIdAfterCleanup_Throws()
    {
        IAdbCommandService adb = CreateRootedAdb();
        adb.GetSettingAsync("SERIAL", "secure", "android_id", Arg.Any<CancellationToken>())
            .Returns(CurrentAndroidId, CurrentAndroidId);
        DeviceChangeService service = CreateService(adb);

        InvalidOperationException exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            service.ChangeAsync(
                "SERIAL",
                CreateProfile(),
                true,
                new DeviceChangeOptions
                {
                    UseDefaultMode = false,
                    ChangeAndroidId = true,
                    ClearAllPackages = false,
                    ClearGoogleAccounts = false
                },
                null,
                CancellationToken.None));

        Assert.Contains("Android ID was not regenerated", exception.Message, StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task ChangeAsync_MissingRegeneratedAndroidId_Throws()
    {
        IAdbCommandService adb = CreateRootedAdb();
        adb.GetSettingAsync("SERIAL", "secure", "android_id", Arg.Any<CancellationToken>())
            .Returns(CurrentAndroidId, "null");
        DeviceChangeService service = CreateService(adb);

        InvalidOperationException exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            service.ChangeAsync(
                "SERIAL",
                CreateProfile(),
                true,
                new DeviceChangeOptions
                {
                    UseDefaultMode = false,
                    ChangeAndroidId = true,
                    ClearAllPackages = false,
                    ClearGoogleAccounts = false
                },
                null,
                CancellationToken.None));

        Assert.Contains("Android ID was not regenerated", exception.Message, StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task ChangeAsync_DeleteAndroidIdFailure_StopsAfterProfileAndCleanupBeforeRebooting()
    {
        IAdbCommandService adb = CreateRootedAdb();
        var failure = new InvalidOperationException("delete setting failed");
        adb.DeleteSettingAsync(
                "SERIAL",
                "secure",
                "android_id",
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException(failure));
        IDeviceDataCleanupService cleanup = Substitute.For<IDeviceDataCleanupService>();
        DeviceChangeService service = CreateService(adb, cleanup);

        InvalidOperationException exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            service.ChangeAsync(
                "SERIAL",
                CreateProfile(),
                true,
                new DeviceChangeOptions
                {
                    UseDefaultMode = false,
                    ChangeAndroidId = true,
                    ClearAllPackages = false,
                    ClearGoogleAccounts = false
                },
                null,
                CancellationToken.None));

        Assert.AreSame(failure, exception);
        await cleanup.Received(1).CleanAsync(
            "SERIAL",
            Arg.Any<DeviceChangeOptions>(),
            Arg.Any<CancellationToken>());
        await adb.Received(1).SetPropertyAsync(
            "SERIAL",
            DeviceSpoofPropertyConstants.ProductModel,
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await adb.DidNotReceiveWithAnyArgs().RebootAsync(default!, default);
    }

    private static IAdbCommandService CreateRootedAdb()
    {
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        adb.RunAdbAsync("SERIAL", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, string.Empty, string.Empty));
        adb.RunAdbShellAsync("SERIAL", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.ArgAt<string>(1) switch
            {
                "id -u" => new CommandResult(0, "0", string.Empty),
                "getprop sys.boot_completed" => new CommandResult(0, "1", string.Empty),
                _ => new CommandResult(0, string.Empty, string.Empty)
            });
        adb.GetPropertyAsync("SERIAL", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.ArgAt<string>(1) switch
            {
                "ro.product.brand" => "samsung",
                "ro.product.manufacturer" => "samsung",
                "ro.product.model" => "SM-S928B",
                "ro.product.device" => "e3q",
                "ro.product.name" => "e3qxxx",
                "ro.build.fingerprint" => "samsung/e3qxxx/e3q:15/AP3A/test:user/release-keys",
                _ => string.Empty
            });
        adb.GetSettingAsync(
                "SERIAL",
                DeviceChangeConstants.GlobalSettingsNamespace,
                DeviceChangeConstants.DeviceNameSetting,
                Arg.Any<CancellationToken>())
            .Returns("e3qxxx");
        adb.GetSettingAsync(
                "SERIAL",
                DeviceChangeConstants.SecureSettingsNamespace,
                DeviceChangeConstants.BluetoothNameSetting,
                Arg.Any<CancellationToken>())
            .Returns("e3qxxx");
        return adb;
    }

    private static DeviceChangeService CreateService(
        IAdbCommandService adb,
        IDeviceDataCleanupService? cleanupService = null,
        IDeviceIntegrityService? integrityService = null,
        IDeviceLocationService? locationService = null,
        IDeviceTimezoneService? timezoneService = null,
        ILocationDataService? locationDataService = null,
        IRandomService? randomService = null)
    {
        return new DeviceChangeService(
            adb,
            cleanupService ?? Substitute.For<IDeviceDataCleanupService>(),
            integrityService ?? Substitute.For<IDeviceIntegrityService>(),
            locationService,
            timezoneService,
            locationDataService,
            randomService,
            NullLogger<DeviceChangeService>.Instance);
    }

    private static DeviceInfoApiDevice CreateProfile()
    {
        return new DeviceInfoApiDevice
        {
            Brand = "samsung",
            Model = "SM-S928B",
            Code = "e3q",
            Name = "e3qxxx",
            Manufacturer = "samsung",
            Fingerprint = "samsung/e3qxxx/e3q:15/AP3A/test:user/release-keys",
            BuildDate = "Thu Jun 04 00:00:00 UTC 2026",
            BuildDateUtc = "1780531200",
            Bootloader = "test",
            Serial = "NEW-SERIAL",
            Imei = "123456789012345",
            Imei1 = "123456789012352",
            Iccid = "8984041234567890123",
            Imsi = "452041234567890",
            SimPhoneNumber = "+84901234567",
            SimOperatorName = "Viettel",
            SimOperatorCountry = "vn",
            SimOperatorNumeric = "45204",
            WifiMacAddress = "00:11:22:33:44:55",
            BluetoothMacAddress = "00:11:22:33:44:66",
            WifiBssid = "00:11:22:33:44:77"
        };
    }

    [TestMethod]
    public async Task ChangeAsync_AppliesIntegrityUpdate_WhenInAdvancedModeWithUpdateIntegrityChecked()
    {
        IAdbCommandService adb = CreateRootedAdb();
        IDeviceIntegrityService integrityService = Substitute.For<IDeviceIntegrityService>();
        DeviceChangeService service = CreateService(adb, integrityService: integrityService);
        var options = new DeviceChangeOptions
        {
            UseDefaultMode = false,
            UpdateIntegrity = true
        };

        await service.ChangeAsync("SERIAL", CreateProfile(), changeSim: true, options, progress: null, CancellationToken.None);

        await integrityService.Received(1).ApplyAsync(
            "SERIAL",
            Arg.Is<UpdateIntegrityDialogResult>(r => r.UpdateIntegrityFromServer && r.UpdateIntegrityEnabled && r.UpdateKeyboxEnabled),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ChangeAsync_SkipsIntegrityUpdate_WhenInDefaultMode()
    {
        IAdbCommandService adb = CreateRootedAdb();
        IDeviceIntegrityService integrityService = Substitute.For<IDeviceIntegrityService>();
        DeviceChangeService service = CreateService(adb, integrityService: integrityService);
        var options = new DeviceChangeOptions
        {
            UseDefaultMode = true,
            UpdateIntegrity = true
        };

        await service.ChangeAsync("SERIAL", CreateProfile(), changeSim: true, options, progress: null, CancellationToken.None);

        await integrityService.DidNotReceiveWithAnyArgs().ApplyAsync(default!, default!, default);
    }

    [TestMethod]
    public async Task ChangeAsync_AppliesLocationAndTimezone_WhenInAdvancedModeWithLocationAndTimezoneChecked()
    {
        IAdbCommandService adb = CreateRootedAdb();
        IDeviceLocationService locationService = Substitute.For<IDeviceLocationService>();
        IDeviceTimezoneService timezoneService = Substitute.For<IDeviceTimezoneService>();
        ILocationDataService locationDataService = Substitute.For<ILocationDataService>();

        var testLocations = new List<LocationOption>
        {
            new LocationOption("vn", "Viet Nam", "Hanoi", "Asia/Ho_Chi_Minh", "UTC+7", 21.0300, 105.8690)
        };
        locationDataService.GetLocationsAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<LocationOption>>(testLocations));

        DeviceChangeService service = CreateService(
            adb,
            locationService: locationService,
            timezoneService: timezoneService,
            locationDataService: locationDataService);

        DeviceInfoApiDevice profile = CreateProfile();
        profile.SimOperatorCountry = "vn";

        var options = new DeviceChangeOptions
        {
            UseDefaultMode = false,
            ChangeLocation = true,
            ChangeTimezone = true
        };

        await service.ChangeAsync("SERIAL", profile, changeSim: true, options, progress: null, CancellationToken.None);

        await locationService.Received(1).ApplyLocationAsync("SERIAL", "21.0300", "105.8690", Arg.Any<CancellationToken>());
        await timezoneService.Received(1).ApplyTimezoneAsync("SERIAL", "Asia/Ho_Chi_Minh", Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ChangeAsync_SkipsLocationAndTimezone_WhenInDefaultMode()
    {
        IAdbCommandService adb = CreateRootedAdb();
        IDeviceLocationService locationService = Substitute.For<IDeviceLocationService>();
        IDeviceTimezoneService timezoneService = Substitute.For<IDeviceTimezoneService>();
        ILocationDataService locationDataService = Substitute.For<ILocationDataService>();

        DeviceChangeService service = CreateService(
            adb,
            locationService: locationService,
            timezoneService: timezoneService,
            locationDataService: locationDataService);

        var options = new DeviceChangeOptions
        {
            UseDefaultMode = true,
            ChangeLocation = true,
            ChangeTimezone = true
        };

        await service.ChangeAsync("SERIAL", CreateProfile(), changeSim: true, options, progress: null, CancellationToken.None);

        await locationDataService.DidNotReceiveWithAnyArgs().GetLocationsAsync(Arg.Any<CancellationToken>());
        await locationService.DidNotReceiveWithAnyArgs().ApplyLocationAsync(default!, default!, default!, default);
        await timezoneService.DidNotReceiveWithAnyArgs().ApplyTimezoneAsync(default!, default!, default);
    }

    [TestMethod]
    public async Task ChangeAsync_RandomlySelectsLocationAndTimezone_FromCountryLocations()
    {
        IAdbCommandService adb = CreateRootedAdb();
        IDeviceLocationService locationService = Substitute.For<IDeviceLocationService>();
        IDeviceTimezoneService timezoneService = Substitute.For<IDeviceTimezoneService>();
        ILocationDataService locationDataService = Substitute.For<ILocationDataService>();
        IRandomService randomService = Substitute.For<IRandomService>();

        var testLocations = new List<LocationOption>
        {
            new LocationOption("us", "United States", "New York", "America/New_York", "UTC-5", 40.7128, -74.0060),
            new LocationOption("us", "United States", "Los Angeles", "America/Los_Angeles", "UTC-8", 34.0522, -118.2437)
        };
        locationDataService.GetLocationsAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<LocationOption>>(testLocations));

        randomService.PickRandom(Arg.Any<IReadOnlyList<LocationOption>>()).Returns(testLocations[1]);
        randomService.PickRandom(Arg.Any<IReadOnlyList<string>>()).Returns("America/Los_Angeles");

        DeviceChangeService service = CreateService(
            adb,
            locationService: locationService,
            timezoneService: timezoneService,
            locationDataService: locationDataService,
            randomService: randomService);

        DeviceInfoApiDevice profile = CreateProfile();
        profile.SimOperatorCountry = "us";

        var options = new DeviceChangeOptions
        {
            UseDefaultMode = false,
            ChangeLocation = true,
            ChangeTimezone = true
        };

        await service.ChangeAsync("SERIAL", profile, changeSim: true, options, progress: null, CancellationToken.None);

        await locationService.Received(1).ApplyLocationAsync("SERIAL", "34.0522", "-118.2437", Arg.Any<CancellationToken>());
        await timezoneService.Received(1).ApplyTimezoneAsync("SERIAL", "America/Los_Angeles", Arg.Any<CancellationToken>());
    }
}
