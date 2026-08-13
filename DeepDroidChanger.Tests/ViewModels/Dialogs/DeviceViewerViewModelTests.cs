using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using DeepDroidChanger.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DeepDroidChanger.Tests.ViewModels.Dialogs;

[TestClass]
public sealed class DeviceViewerViewModelTests
{
    [TestMethod]
    public void StreamFailure_DoesNotRedefineOnlineDeviceConnection()
    {
        var viewModel = CreateViewModel(out _, out _);
        try
        {
            viewModel.SetDeviceConnectionState(DeviceConnectionState.Online);
            viewModel.MarkStreamError();

            Assert.AreEqual(DeviceConnectionState.Online, viewModel.DeviceConnectionState);
            Assert.IsTrue(viewModel.IsDeviceConnected);
            Assert.IsFalse(viewModel.IsDeviceDisconnected);
            Assert.IsTrue(viewModel.VolumeUpCommand.CanExecute(null));
        }
        finally
        {
            viewModel.Dispose();
        }
    }

    [TestMethod]
    public void OfflineDevice_DisablesInteractiveCommands_AndExclusiveWorkflowDisablesSensitiveCommands()
    {
        var viewModel = CreateViewModel(out _, out var coordinator);
        try
        {
            Assert.IsFalse(viewModel.VolumeUpCommand.CanExecute(null));
            Assert.IsFalse(viewModel.RunShellCommandCommand.CanExecute(null));

            viewModel.SetDeviceConnectionState(DeviceConnectionState.Online);
            Assert.IsTrue(viewModel.VolumeUpCommand.CanExecute(null));
            Assert.IsTrue(viewModel.RunShellCommandCommand.CanExecute(null));

            using var operation = coordinator.TryStart(
                "SERIAL",
                DeviceActionKind.ChangeDevice,
                canCancel: true);

            Assert.IsTrue(viewModel.VolumeUpCommand.CanExecute(null));
            Assert.IsFalse(viewModel.PowerCommand.CanExecute(null));
            Assert.IsFalse(viewModel.SendInputTextCommand.CanExecute(null));
            Assert.IsFalse(viewModel.RunShellCommandCommand.CanExecute(null));
        }
        finally
        {
            viewModel.Dispose();
        }
    }

    [TestMethod]
    public async Task QuickCommands_AreSerializedPerViewer()
    {
        var adb = Substitute.For<IAdbCommandService>();
        var concurrent = 0;
        var maximumConcurrent = 0;
        adb.SendKeyEventAsync("SERIAL", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => RunRecordedCommandAsync());

        var viewModel = CreateViewModel(out _, out _, adb);
        try
        {
            viewModel.SetDeviceConnectionState(DeviceConnectionState.Online);

            Task first = viewModel.VolumeUpCommand.ExecuteAsync(null);
            Task second = viewModel.VolumeDownCommand.ExecuteAsync(null);
            await Task.WhenAll(first, second);

            Assert.AreEqual(1, maximumConcurrent);
        }
        finally
        {
            viewModel.Dispose();
        }

        async Task RunRecordedCommandAsync()
        {
            var current = Interlocked.Increment(ref concurrent);
            InterlockedMax(ref maximumConcurrent, current);
            await Task.Delay(20);
            Interlocked.Decrement(ref concurrent);
        }

        static void InterlockedMax(ref int location, int value)
        {
            while (true)
            {
                var current = Volatile.Read(ref location);
                if (current >= value || Interlocked.CompareExchange(ref location, value, current) == current)
                    return;
            }
        }
    }

    private static DeviceViewerViewModel CreateViewModel(
        out IAdbCommandService adb,
        out DeviceActionCoordinatorService coordinator,
        IAdbCommandService? suppliedAdb = null)
    {
        adb = suppliedAdb ?? Substitute.For<IAdbCommandService>();
        coordinator = new DeviceActionCoordinatorService();
        var ip = Substitute.For<IIpGeolocationService>();
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(callInfo => callInfo.Arg<string>());

        var viewModel = new DeviceViewerViewModel(
            adb,
            ip,
            localization,
            coordinator,
            NullLogger<DeviceViewerViewModel>.Instance);
        viewModel.Initialize("SERIAL", "Device");
        return viewModel;
    }
}
