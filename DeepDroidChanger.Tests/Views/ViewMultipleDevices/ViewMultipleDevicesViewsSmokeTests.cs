using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using DeepDroidChanger.Tests.ViewModels;
using DeepDroidChanger.ViewDevices.Contracts;
using DeepDroidChanger.ViewModels;
using DeepDroidChanger.Views;
using Microsoft.Extensions.Logging.Abstractions;
using MaterialDesignThemes.Wpf;
using ScrcpyNet.Wpf;

namespace DeepDroidChanger.Tests.Views.ViewMultipleDevices;

[TestClass]
[DoNotParallelize]
public sealed class ViewMultipleDevicesViewsSmokeTests
{
    [TestMethod]
    public void ViewMultipleDevicesViews_InstantiateWithApplicationResources()
    {
        WpfTestDispatcher.Run(() =>
        {
            WpfTestDispatcher.EnsureApplicationResources();

            FakeTracker tracker = new();
            ViewDevicePresentationCoordinator coordinator = new();
            ViewMultipleDevicesViewModel viewModel = new(
                new FakeDeviceStoreService(),
                tracker,
                new FakeSessionFactory(),
                new FakeClipboardService(),
                new FakeLocalizationService(),
                new ImmediateUiDispatcher(),
                NullLoggerFactory.Instance,
                NullLogger<ViewMultipleDevicesViewModel>.Instance,
                new NoOpViewDeviceWindowService(),
                coordinator);

            try
            {
                _ = new ViewMultipleDevicesView(
                    viewModel,
                    NullLogger<ViewMultipleDevicesView>.Instance);
                ViewMultipleDeviceTile tile = new();
                Assert.IsNotNull(tile.FindName("ScrcpyDisplay"));
                Assert.IsInstanceOfType(tile.FindName("ScrcpyDisplay"), typeof(ScrcpyDisplay));
                Assert.IsNull(tile.FindName("NativeHost"));
            }
            finally
            {
                viewModel.DisposeAsync().GetAwaiter().GetResult();
            }
        });
    }

    [TestMethod]
    public void ViewMultipleDeviceTile_UsesOpenViewButtonWithoutOnlineStatusPill()
    {
        WpfTestDispatcher.Run(() =>
        {
            WpfTestDispatcher.EnsureApplicationResources();
            FakeTracker tracker = new([new AdbDevice("SERIAL", AdbDeviceStatus.Online)]);
            ViewMultipleDeviceItemViewModel item = new(
                "SERIAL",
                "Device",
                new FakeSessionFactory(),
                tracker,
                new FakeClipboardService(),
                new FakeLocalizationService(),
                new ImmediateUiDispatcher(),
                NullLogger<ViewMultipleDeviceItemViewModel>.Instance,
                new ViewDevicePresentationCoordinator(),
                openViewDeviceRequest: (_, _) => Task.CompletedTask);
            ViewMultipleDeviceTile tile = new()
            {
                DataContext = item
            };

            try
            {
                Button openView = (Button)tile.FindName("OpenViewDeviceButton")!;
                Binding? binding = BindingOperations.GetBinding(openView, Button.CommandProperty);
                Assert.IsNotNull(binding);
                Assert.AreEqual(
                    nameof(ViewMultipleDeviceItemViewModel.OpenViewDeviceCommand),
                    binding!.Path.Path);
                Assert.AreEqual(30, openView.Width);
                Assert.AreEqual(30, openView.Height);
                Assert.IsInstanceOfType(openView.Content, typeof(PackIcon));
                Assert.AreEqual(PackIconKind.OpenInNew, ((PackIcon)openView.Content).Kind);
                Assert.IsNull(tile.TryFindResource("ViewMultipleDeviceStatusPillStyle"));
                Assert.IsNull(tile.TryFindResource("ViewMultipleDeviceStatusDotStyle"));
                Assert.IsNull(tile.FindName("StatusText"));
            }
            finally
            {
                item.DisposeAsync().GetAwaiter().GetResult();
            }
        });
    }

}
