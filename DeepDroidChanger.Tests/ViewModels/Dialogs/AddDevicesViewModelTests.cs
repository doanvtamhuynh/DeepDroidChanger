using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using DeepDroidChanger.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DeepDroidChanger.Tests.ViewModels.Dialogs;

[TestClass]
public sealed class AddDevicesViewModelTests
{
    [TestMethod]
    public async Task InitializeAsync_OnlyLoadsAddableOnlineDevices()
    {
        IAdbDeviceService adb = Substitute.For<IAdbDeviceService>();
        adb.GetConnectedDevicesAsync(CancellationToken.None).Returns(
        [
            new AdbDevice("ONLINE", AdbDeviceStatus.Online),
            new AdbDevice("OFFLINE", AdbDeviceStatus.Offline),
            new AdbDevice("UNAUTHORIZED", AdbDeviceStatus.Unauthorized)
        ]);
        adb.GetDeviceTypeAsync("ONLINE", CancellationToken.None).Returns("Phone");
        IDeviceStoreService store = Substitute.For<IDeviceStoreService>();
        store.LoadAsync(CancellationToken.None).Returns([]);
        IUiDispatcherService dispatcher = Substitute.For<IUiDispatcherService>();
        dispatcher.InvokeAsync(Arg.Any<Action>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callInfo.Arg<Action>()();
                return Task.CompletedTask;
            });
        var viewModel = new AddDevicesViewModel(
            adb,
            store,
            dispatcher,
            Substitute.For<ILocalizationService>(),
            new PollingService(),
            NullLogger<AddDevicesViewModel>.Instance);

        try
        {
            await viewModel.InitializeAsync(CancellationToken.None);

            Assert.HasCount(1, viewModel.Devices);
            Assert.AreEqual("ONLINE", viewModel.Devices[0].Serial);
            await adb.DidNotReceive().GetDeviceTypeAsync("OFFLINE", Arg.Any<CancellationToken>());
            await adb.DidNotReceive().GetDeviceTypeAsync("UNAUTHORIZED", Arg.Any<CancellationToken>());
        }
        finally
        {
            await viewModel.DeactivateAsync();
            viewModel.Dispose();
        }
    }

    [TestMethod]
    public async Task DeactivateAsync_WaitsForPollingToStop()
    {
        IAdbDeviceService adb = Substitute.For<IAdbDeviceService>();
        adb.GetConnectedDevicesAsync(Arg.Any<CancellationToken>()).Returns([]);
        IDeviceStoreService store = Substitute.For<IDeviceStoreService>();
        store.LoadAsync(Arg.Any<CancellationToken>()).Returns([]);
        IUiDispatcherService dispatcher = Substitute.For<IUiDispatcherService>();
        dispatcher.InvokeAsync(Arg.Any<Action>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callInfo.Arg<Action>()();
                return Task.CompletedTask;
            });
        var pollingStopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IPollingService polling = Substitute.For<IPollingService>();
        polling.RunAsync(Arg.Any<TimeSpan>(), Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                CancellationToken token = callInfo.ArgAt<CancellationToken>(2);
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                }
                finally
                {
                    pollingStopped.TrySetResult();
                }
            });
        var viewModel = new AddDevicesViewModel(
            adb,
            store,
            dispatcher,
            Substitute.For<ILocalizationService>(),
            polling,
            NullLogger<AddDevicesViewModel>.Instance);

        try
        {
            await viewModel.InitializeAsync(CancellationToken.None);
            await viewModel.DeactivateAsync();

            Assert.IsTrue(pollingStopped.Task.IsCompleted);
        }
        finally
        {
            viewModel.Dispose();
        }
    }

    [TestMethod]
    public async Task ToggleDeviceSelectionCommand_TogglesSelectedDeviceAndSelectionState()
    {
        IAdbDeviceService adb = Substitute.For<IAdbDeviceService>();
        adb.GetConnectedDevicesAsync(CancellationToken.None).Returns(
        [
            new AdbDevice("SERIAL", AdbDeviceStatus.Online)
        ]);
        adb.GetDeviceTypeAsync("SERIAL", CancellationToken.None).Returns("Phone");
        IDeviceStoreService store = Substitute.For<IDeviceStoreService>();
        store.LoadAsync(CancellationToken.None).Returns([]);
        IUiDispatcherService dispatcher = Substitute.For<IUiDispatcherService>();
        dispatcher.InvokeAsync(Arg.Any<Action>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callInfo.Arg<Action>()();
                return Task.CompletedTask;
            });
        var viewModel = new AddDevicesViewModel(
            adb,
            store,
            dispatcher,
            Substitute.For<ILocalizationService>(),
            new PollingService(),
            NullLogger<AddDevicesViewModel>.Instance);

        try
        {
            await viewModel.InitializeAsync(CancellationToken.None);
            AddDeviceRowViewModel device = viewModel.Devices.Single();

            viewModel.ToggleDeviceSelectionCommand.Execute(device);

            Assert.IsTrue(device.IsSelected);
            Assert.IsTrue(viewModel.SelectAll);
            Assert.IsTrue(viewModel.AddCommand.CanExecute(null));

            viewModel.ToggleDeviceSelectionCommand.Execute(device);

            Assert.IsFalse(device.IsSelected);
            Assert.IsFalse(viewModel.SelectAll);
            Assert.IsFalse(viewModel.AddCommand.CanExecute(null));
        }
        finally
        {
            await viewModel.DeactivateAsync();
            viewModel.Dispose();
        }
    }
}
