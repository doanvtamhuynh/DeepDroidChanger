using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using DeepDroidChanger.ViewDevices.Contracts;
using DeepDroidChanger.ViewModels;
using DeepDroidChanger.Views;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpAdbClient;

namespace DeepDroidChanger.Tests.Views.ViewDevice;

[TestClass]
[DoNotParallelize]
public sealed class ViewDeviceToolsWindowSmokeTests
{
    [TestMethod]
    public void ViewDeviceToolsWindow_InstantiatesWithApplicationResources()
    {
        WpfTestDispatcher.Run(() =>
        {
            EnsureApplicationResources();
            ViewDeviceViewModel viewModel = CreateViewModel();
            ViewDeviceToolsWindow? window = null;
            try
            {
                window = new ViewDeviceToolsWindow(viewModel);

                Assert.IsNotNull(window.FindName("RootBorder"));
                Assert.IsNotNull(window.FindName("HeaderPanel"));
                Assert.IsNotNull(window.FindName("ActionsScrollViewer"));
                Assert.IsNotNull(window.FindName("ActionsPanel"));
            }
            finally
            {
                CloseWindow(window);
                viewModel.DisposeAsync().GetAwaiter().GetResult();
            }
        });
    }

    [TestMethod]
    public void ViewDeviceToolDetailWindow_InstantiatesWithRequiredHosts()
    {
        WpfTestDispatcher.Run(() =>
        {
            EnsureApplicationResources();
            ViewDeviceViewModel viewModel = CreateViewModel();
            ViewDeviceToolDetailWindow? window = null;
            try
            {
                window = new ViewDeviceToolDetailWindow(viewModel);

                Assert.IsFalse(window.ShowInTaskbar);
                Assert.AreEqual(ResizeMode.NoResize, window.ResizeMode);
                Assert.AreEqual(WindowStyle.None, window.WindowStyle);
                Assert.IsTrue(window.AllowsTransparency);
                Assert.IsTrue(window.ShowActivated);
                Assert.AreEqual(350, window.Width, 0.5);
                Assert.AreEqual(320, window.MinWidth, 0.5);
                Assert.IsNotNull(window.FindName("RenameContent"));
                Assert.IsNotNull(window.FindName("InputAdbContent"));
                Assert.IsNotNull(window.FindName("FileTransferContent"));
                Assert.IsNotNull(window.FindName("RenameTextBox"));
                Assert.IsNotNull(window.FindName("InputTextBox"));
                Assert.IsNotNull(window.FindName("ComputerFilePathTextBox"));
                Assert.IsNotNull(window.FindName("AndroidFilePathTextBox"));
                Assert.IsNotNull(window.FindName("ImportButton"));
                Assert.IsNotNull(window.FindName("ExportButton"));
                Assert.IsNull(window.FindName("AdbShellOutputBox"));
            }
            finally
            {
                CloseWindow(window);
                viewModel.DisposeAsync().GetAwaiter().GetResult();
            }
        });
    }

    [TestMethod]
    public void ViewDeviceToolDetailWindow_ReusesOneModeHost()
    {
        WpfTestDispatcher.Run(() =>
        {
            EnsureApplicationResources();
            ViewDeviceViewModel viewModel = CreateViewModel();
            ViewDeviceToolDetailWindow? window = null;
            try
            {
                window = new ViewDeviceToolDetailWindow(viewModel);
                FrameworkElement rename = (FrameworkElement)window.FindName("RenameContent")!;
                FrameworkElement input = (FrameworkElement)window.FindName("InputAdbContent")!;
                FrameworkElement transfer = (FrameworkElement)window.FindName("FileTransferContent")!;

                window.SetEditor(ViewDeviceToolEditor.Rename);
                Assert.AreEqual(Visibility.Visible, rename.Visibility);
                Assert.AreEqual(Visibility.Collapsed, input.Visibility);
                Assert.AreEqual(Visibility.Collapsed, transfer.Visibility);

                window.SetEditor(ViewDeviceToolEditor.InputAdb);
                Assert.AreEqual(Visibility.Collapsed, rename.Visibility);
                Assert.AreEqual(Visibility.Visible, input.Visibility);

                window.SetEditor(ViewDeviceToolEditor.FileTransfer);
                Assert.AreEqual(Visibility.Visible, transfer.Visibility);
                Assert.AreEqual(ViewDeviceToolEditor.FileTransfer, window.Editor);
            }
            finally
            {
                CloseWindow(window);
                viewModel.DisposeAsync().GetAwaiter().GetResult();
            }
        });
    }

    [TestMethod]
    public void ViewDeviceToolDetailWindow_FileTransferActionsUseEqualCenteredColumns()
    {
        WpfTestDispatcher.Run(() =>
        {
            EnsureApplicationResources();
            ViewDeviceViewModel viewModel = CreateViewModel();
            ViewDeviceToolDetailWindow? window = null;
            try
            {
                window = new ViewDeviceToolDetailWindow(viewModel);
                window.SetEditor(ViewDeviceToolEditor.FileTransfer);
                window.Show();
                window.UpdateLayout();

                Button import = (Button)window.FindName("ImportButton")!;
                Button export = (Button)window.FindName("ExportButton")!;
                Assert.AreEqual(import.ActualWidth, export.ActualWidth, 0.5);
                Assert.AreEqual(import.ActualHeight, export.ActualHeight, 0.5);
                Assert.AreEqual(HorizontalAlignment.Stretch, import.HorizontalAlignment);
                Assert.AreEqual(HorizontalAlignment.Stretch, export.HorizontalAlignment);
                Assert.AreEqual(HorizontalAlignment.Center, import.HorizontalContentAlignment);
                Assert.AreEqual(HorizontalAlignment.Center, export.HorizontalContentAlignment);
            }
            finally
            {
                CloseWindow(window);
                viewModel.DisposeAsync().GetAwaiter().GetResult();
            }
        });
    }

    [TestMethod]
    public void ViewDeviceToolsWindow_ContainsCompactQuickControls()
    {
        WpfTestDispatcher.Run(() =>
        {
            EnsureApplicationResources();
            ViewDeviceViewModel viewModel = CreateViewModel();
            ViewDeviceToolsWindow? window = null;
            try
            {
                window = new ViewDeviceToolsWindow(viewModel);
                Grid quickControls = (Grid)window.FindName("QuickControlsPanel")!;
                Button volumeDown = (Button)window.FindName("VolumeDownButton")!;
                Button volumeUp = (Button)window.FindName("VolumeUpButton")!;
                Button power = (Button)window.FindName("PowerButton")!;
                Button reconnect = (Button)window.FindName("ReconnectButton")!;
                Button screen = (Button)window.FindName("ScreenButton")!;
                Button reboot = (Button)window.FindName("RebootButton")!;
                window.UpdateLayout();

                Assert.AreEqual(2, quickControls.RowDefinitions.Count);
                Assert.AreEqual(3, quickControls.ColumnDefinitions.Count);
                Assert.IsTrue(quickControls.ColumnDefinitions.All(column =>
                    column.Width.GridUnitType == GridUnitType.Star));
                Assert.AreEqual(0, Grid.GetRow(volumeDown));
                Assert.AreEqual(0, Grid.GetColumn(volumeDown));
                Assert.AreEqual(0, Grid.GetRow(volumeUp));
                Assert.AreEqual(1, Grid.GetColumn(volumeUp));
                Assert.AreEqual(2, Grid.GetColumn(power));
                Assert.AreEqual(1, Grid.GetRow(reconnect));
                Assert.AreEqual(0, Grid.GetColumn(reconnect));
                Assert.AreEqual(1, Grid.GetRow(screen));
                Assert.AreEqual(1, Grid.GetColumn(screen));
                Assert.AreEqual(1, Grid.GetRow(reboot));
                Assert.AreEqual(2, Grid.GetColumn(reboot));
                Assert.AreEqual(40, volumeDown.Height);
                Assert.AreEqual(40, volumeUp.Height);
                Assert.AreEqual(40, power.Height);
                Assert.AreEqual(40, reconnect.Height);
                Assert.AreEqual(40, screen.Height);
                Assert.AreEqual(40, reboot.Height);
                Assert.AreEqual("Vol", ((TextBlock)((StackPanel)volumeDown.Content).Children[1]).Text);
                Assert.AreEqual("Vol", ((TextBlock)((StackPanel)volumeUp.Content).Children[1]).Text);
                Assert.AreEqual("On/Off Screen", ((TextBlock)((StackPanel)screen.Content).Children[1]).Text);
                Assert.AreEqual("Reboot", ((TextBlock)((StackPanel)reboot.Content).Children[1]).Text);
            }
            finally
            {
                CloseWindow(window);
                viewModel.DisposeAsync().GetAwaiter().GetResult();
            }
        });
    }

    [TestMethod]
    public void ViewDeviceToolsWindow_ContainsDirectActionsAndNoLegacyEditors()
    {
        WpfTestDispatcher.Run(() =>
        {
            EnsureApplicationResources();
            ViewDeviceViewModel viewModel = CreateViewModel();
            ViewDeviceToolsWindow? window = null;
            try
            {
                window = new ViewDeviceToolsWindow(viewModel);
                string[] actionNames =
                [
                    "RenameConfigButton",
                    "InputAdbButton",
                    "RotateScreenButton",
                    "FileTransferButton",
                    "WifiButton",
                    "ScreenButton",
                    "ReconnectButton",
                    "RebootButton",
                    "ScreenshotButton",
                    "InstallPackageButton",
                    "GmsButton",
                    "PlayStoreButton"
                ];
                foreach (string actionName in actionNames)
                    Assert.IsNotNull(window.FindName(actionName), actionName);

                string[] removedNames =
                [
                    "ImportFileButton",
                    "ExportFileButton",
                    "WifiToggle",
                    "ScreenToggle",
                    "OtherActionExpander",
                    "OtherActionItems",
                    "DeviceInfoButton",
                    "RenameEditorPanel",
                    "InputAdbEditorPanel",
                    "ImportEditorPanel",
                    "ExportEditorPanel",
                    "InstallEditorPanel",
                    "DeviceInfoEditorPanel"
                ];
                foreach (string removedName in removedNames)
                    Assert.IsNull(window.FindName(removedName), removedName);

                AssertCommandBinding(
                    (Button)window.FindName("WifiButton")!,
                    nameof(ViewDeviceViewModel.ToggleWifiCommand));
                AssertCommandBinding(
                    (Button)window.FindName("ScreenButton")!,
                    nameof(ViewDeviceViewModel.ToggleScreenCommand));
                AssertCommandBinding(
                    (Button)window.FindName("ReconnectButton")!,
                    nameof(ViewDeviceViewModel.ReconnectCommand));
                AssertCommandBinding(
                    (Button)window.FindName("RebootButton")!,
                    nameof(ViewDeviceViewModel.RebootDeviceCommand));
                AssertCommandBinding(
                    (Button)window.FindName("GmsButton")!,
                    nameof(ViewDeviceViewModel.ToggleGmsCommand));
                AssertCommandBinding(
                    (Button)window.FindName("PlayStoreButton")!,
                    nameof(ViewDeviceViewModel.TogglePlayStoreCommand));
                AssertCommandBinding(
                    (Button)window.FindName("InstallPackageButton")!,
                    nameof(ViewDeviceViewModel.InstallPackageCommand));
            }
            finally
            {
                CloseWindow(window);
                viewModel.DisposeAsync().GetAwaiter().GetResult();
            }
        });
    }

    [TestMethod]
    public void ViewDeviceToolsWindow_DetailDismissesOnlyForOtherActions()
    {
        WpfTestDispatcher.Run(() =>
        {
            EnsureApplicationResources();
            ViewDeviceViewModel viewModel = CreateInitializedOfflineViewModel();
            ViewDeviceToolsWindow? window = null;
            try
            {
                window = new ViewDeviceToolsWindow(viewModel)
                {
                    Height = 240
                };
                window.Show();
                window.UpdateLayout();

                viewModel.RenameConfigCommand.Execute(null);
                FlushDispatcher();
                ViewDeviceToolDetailWindow detail = Application.Current!.Windows
                    .OfType<ViewDeviceToolDetailWindow>()
                    .Single(candidate => candidate.Owner == window);
                Assert.IsTrue(detail.IsVisible);

                RaisePreviewMouseDown((FrameworkElement)window.FindName("RootBorder")!);
                Assert.IsTrue(detail.IsVisible);
                Assert.AreEqual(ViewDeviceToolEditor.Rename, viewModel.ActiveToolEditor);

                RaisePreviewMouseDown((FrameworkElement)window.FindName("HeaderPanel")!);
                Assert.IsTrue(detail.IsVisible);
                Assert.AreEqual(ViewDeviceToolEditor.Rename, viewModel.ActiveToolEditor);

                ScrollBar scrollbar = FindVisualChild<ScrollBar>(window)
                    ?? throw new AssertFailedException("The actions scroll bar was not created.");
                RaisePreviewMouseDown(scrollbar);
                Assert.IsTrue(detail.IsVisible);
                Assert.AreEqual(ViewDeviceToolEditor.Rename, viewModel.ActiveToolEditor);

                foreach (string detailActionName in new[]
                         { "RenameConfigButton", "InputAdbButton", "FileTransferButton" })
                {
                    RaisePreviewMouseDown((Button)window.FindName(detailActionName)!);
                    Assert.IsTrue(detail.IsVisible, detailActionName);
                    Assert.AreEqual(
                        ViewDeviceToolEditor.Rename,
                        viewModel.ActiveToolEditor,
                        detailActionName);
                }

                RaisePreviewMouseDown((Button)window.FindName("RotateScreenButton")!);
                Assert.IsFalse(detail.IsVisible);
                Assert.AreEqual(ViewDeviceToolEditor.None, viewModel.ActiveToolEditor);
            }
            finally
            {
                CloseWindow(window);
                viewModel.DisposeAsync().GetAwaiter().GetResult();
            }
        });
    }

    private static void RaisePreviewMouseDown(UIElement source)
    {
        MouseButtonEventArgs eventArgs = new(
            Mouse.PrimaryDevice,
            0,
            MouseButton.Left)
        {
            RoutedEvent = UIElement.PreviewMouseDownEvent
        };
        source.RaiseEvent(eventArgs);
    }

    private static void FlushDispatcher()
    {
        DispatcherFrame frame = new();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static T? FindVisualChild<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
                return match;

            T? descendant = FindVisualChild<T>(child);
            if (descendant is not null)
                return descendant;
        }

        return null;
    }

    private static void EnsureApplicationResources()
    {
        WpfTestDispatcher.EnsureApplicationResources();
    }

    private static void CloseWindow(Window? window)
    {
        if (window is null)
            return;

        window.DataContext = null;
        if (window.IsVisible)
            window.Close();
    }

    private static void AssertCommandBinding(ButtonBase button, string commandName)
    {
        Binding? binding = BindingOperations.GetBinding(button, ButtonBase.CommandProperty);
        Assert.IsNotNull(binding);
        Assert.AreEqual(commandName, binding!.Path.Path);
    }

    private static ViewDeviceViewModel CreateViewModel()
    {
        ILocalizationService localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>() switch
        {
            "ViewDevice_VolumeShort" => "Vol",
            _ => call.Arg<string>()
        });

        return new ViewDeviceViewModel(
            Substitute.For<ISingleViewDeviceSessionFactory>(),
            Substitute.For<IAdbDeviceTrackerService>(),
            Substitute.For<IAdbCommandService>(),
            Substitute.For<IFilePickerDialogService>(),
            Substitute.For<IViewDeviceScreenshotService>(),
            localization,
            new ImmediateUiDispatcher(),
            Substitute.For<IViewDeviceClipboardService>(),
            NullLogger<ViewDeviceViewModel>.Instance,
            Substitute.For<IDeviceConfigService>(),
            Substitute.For<IDeviceActionService>(),
            Substitute.For<IPackageInstallService>(),
            Substitute.For<IPollingService>());
    }

    private static ViewDeviceViewModel CreateInitializedOfflineViewModel()
    {
        IAdbDeviceTrackerService tracker = Substitute.For<IAdbDeviceTrackerService>();
        tracker.Health.Returns(AdbDeviceTrackerHealth.Connected);
        tracker.CurrentSnapshot.Returns([new AdbDevice("SERIAL", AdbDeviceStatus.Offline)]);
        tracker.GetDevice("SERIAL").Returns(new AdbDevice("SERIAL", AdbDeviceStatus.Offline));

        ILocalizationService localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());

        ViewDeviceViewModel viewModel = new(
            Substitute.For<ISingleViewDeviceSessionFactory>(),
            tracker,
            Substitute.For<IAdbCommandService>(),
            Substitute.For<IFilePickerDialogService>(),
            Substitute.For<IViewDeviceScreenshotService>(),
            localization,
            new ImmediateUiDispatcher(),
            Substitute.For<IViewDeviceClipboardService>(),
            NullLogger<ViewDeviceViewModel>.Instance,
            Substitute.For<IDeviceConfigService>(),
            Substitute.For<IDeviceActionService>(),
            Substitute.For<IPackageInstallService>(),
            Substitute.For<IPollingService>());
        viewModel.InitializeAsync("SERIAL", "Device").GetAwaiter().GetResult();
        return viewModel;
    }

    private sealed class ImmediateUiDispatcher : IUiDispatcherService
    {
        public bool CheckAccess() => true;

        public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            action();
            return Task.CompletedTask;
        }
    }
}
