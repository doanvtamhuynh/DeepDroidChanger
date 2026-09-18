using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using DeepDroidChanger.Tests.ViewModels;
using DeepDroidChanger.ViewDevices.Contracts;
using DeepDroidChanger.ViewModels;
using DeepDroidChanger.Views;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpAdbClient;

namespace DeepDroidChanger.Tests.Views.ViewDevice;

[TestClass]
[DoNotParallelize]
public sealed class ViewDeviceWindowSmokeTests
{
    [TestMethod]
    public void ViewDeviceWindow_InstantiatesWithScrcpyDisplay()
    {
        WpfTestDispatcher.Run(() =>
        {
            EnsureApplicationResources();
            ViewDeviceViewModel viewModel = CreateViewModel();
            ViewDeviceWindow? window = null;
            try
            {
                window = new ViewDeviceWindow(viewModel);

                object? display = window.FindName("ScrcpyDisplay");
                Grid rotatedDisplayHost = (Grid)window.FindName("RotatedDisplayHost")!;
                RotateTransform rotationTransform = (RotateTransform)window.FindName("ViewRotationTransform")!;
                Assert.IsNotNull(display);
                Assert.AreSame(display, rotatedDisplayHost.Children[0]);
                Assert.AreEqual(0, rotationTransform.Angle);
                Assert.IsInstanceOfType(display, typeof(ScrcpyNet.Wpf.ScrcpyDisplay));
                ScrcpyNet.Wpf.ScrcpyDisplay scrcpyDisplay = (ScrcpyNet.Wpf.ScrcpyDisplay)display;
                Assert.IsNull(scrcpyDisplay.Scrcpy);
                scrcpyDisplay.ReleaseActiveInput();
                scrcpyDisplay.ReleaseActiveInput();
                Assert.AreEqual(656, window.Height);
                Assert.AreEqual(240, window.MinHeight);
                Assert.IsFalse(window.AllowDrop);
                Assert.IsTrue(scrcpyDisplay.AllowDrop);
                Assert.IsTrue(((Border)window.FindName("StreamContainer")!).AllowDrop);
                Assert.IsNotNull(window.FindName("PackageDropOverlay"));
                Assert.AreNotSame(rotatedDisplayHost, ((FrameworkElement)window.FindName("PackageDropOverlay")!).Parent);
                Assert.AreEqual(
                    "DeepDroidChanger.ViewDevices",
                    scrcpyDisplay.GetType().Assembly.GetName().Name);
                scrcpyDisplay.ApplyTemplate();
                Assert.IsNotNull(scrcpyDisplay.Template);
                Assert.IsNull(window.FindName("NativeHost"));
                Assert.IsNull(window.FindName("ActionsPanel"));
                Assert.IsNotNull(window.FindName("SettingsButton"));
                Assert.IsNotNull(window.FindName("NavigationPanel"));
            }
            finally
            {
                CloseWindow(window);
                viewModel.DisposeAsync().GetAwaiter().GetResult();
            }
        });
    }

    [TestMethod]
    public void ViewDeviceWindow_DoesNotContainLegacyToolbar()
    {
        WpfTestDispatcher.Run(() =>
        {
            EnsureApplicationResources();
            ViewDeviceViewModel viewModel = CreateViewModel();
            ViewDeviceWindow? window = null;
            try
            {
                window = new ViewDeviceWindow(viewModel);

                Assert.IsNull(window.FindName("ToolbarPanel"));
                Assert.IsNull(window.FindName("ToolbarActionsScrollViewer"));
                Assert.IsNull(window.FindName("ToolbarColumn"));
                Assert.IsNull(window.Resources["ViewDeviceToolbarButtonStyle"]);
            }
            finally
            {
                CloseWindow(window);
                viewModel.DisposeAsync().GetAwaiter().GetResult();
            }
        });
    }

    [TestMethod]
    public void ViewDeviceWindow_HasSettingsToggle()
    {
        WpfTestDispatcher.Run(() =>
        {
            EnsureApplicationResources();
            ViewDeviceViewModel viewModel = CreateViewModel();
            ViewDeviceWindow? window = null;
            try
            {
                window = new ViewDeviceWindow(viewModel);
                Button settingsButton = (Button)window.FindName("SettingsButton")!;

                Assert.AreEqual(false, settingsButton.Tag);
                Assert.AreEqual(36, settingsButton.Width);
                Assert.AreEqual(36, settingsButton.Height);
            }
            finally
            {
                CloseWindow(window);
                viewModel.DisposeAsync().GetAwaiter().GetResult();
            }
        });
    }

    [TestMethod]
    public void ViewDeviceWindow_HasBottomNavigation()
    {
        WpfTestDispatcher.Run(() =>
        {
            EnsureApplicationResources();
            ViewDeviceViewModel viewModel = CreateViewModel();
            ViewDeviceWindow? window = null;
            try
            {
                window = new ViewDeviceWindow(viewModel);
                Grid navigation = (Grid)window.FindName("NavigationPanel")!;
                Button recent = (Button)window.FindName("RecentButton")!;
                Button home = (Button)window.FindName("HomeButton")!;
                Button back = (Button)window.FindName("BackButton")!;
                window.UpdateLayout();

                Assert.AreEqual(5, navigation.ColumnDefinitions.Count);
                Assert.AreEqual(GridUnitType.Star, navigation.ColumnDefinitions[0].Width.GridUnitType);
                Assert.AreEqual(2, navigation.ColumnDefinitions[1].Width.Value);
                Assert.AreEqual(GridUnitType.Star, navigation.ColumnDefinitions[2].Width.GridUnitType);
                Assert.AreEqual(2, navigation.ColumnDefinitions[3].Width.Value);
                Assert.AreEqual(GridUnitType.Star, navigation.ColumnDefinitions[4].Width.GridUnitType);
                Assert.AreEqual(0, Grid.GetColumn(recent));
                Assert.AreEqual(2, Grid.GetColumn(home));
                Assert.AreEqual(4, Grid.GetColumn(back));
                AssertCommandBinding(recent, nameof(ViewDeviceViewModel.RecentCommand));
                AssertCommandBinding(home, nameof(ViewDeviceViewModel.HomeCommand));
                AssertCommandBinding(back, nameof(ViewDeviceViewModel.BackCommand));
            }
            finally
            {
                CloseWindow(window);
                viewModel.DisposeAsync().GetAwaiter().GetResult();
            }
        });
    }

    [TestMethod]
    public void ViewDeviceWindow_HeaderKeepsIdentityStatusAndSettingsSeparated()
    {
        WpfTestDispatcher.Run(() =>
        {
            EnsureApplicationResources();
            ViewDeviceViewModel viewModel = CreateViewModel();
            try
            {
                ViewDeviceWindow window = new(viewModel);
                Border header = (Border)window.FindName("HeaderPanel")!;
                Grid headerGrid = (Grid)header.Child;

                Assert.AreEqual(3, headerGrid.ColumnDefinitions.Count);
                Assert.AreEqual(GridUnitType.Star, headerGrid.ColumnDefinitions[0].Width.GridUnitType);
                Assert.AreEqual(GridUnitType.Auto, headerGrid.ColumnDefinitions[1].Width.GridUnitType);
                Assert.AreEqual(GridUnitType.Auto, headerGrid.ColumnDefinitions[2].Width.GridUnitType);
                Assert.AreEqual(0, Grid.GetColumn(headerGrid.Children[0]));
                Assert.AreEqual(1, Grid.GetColumn(headerGrid.Children[1]));
                Assert.AreEqual(2, Grid.GetColumn(headerGrid.Children[2]));

                Grid identity = (Grid)headerGrid.Children[0];
                Assert.AreEqual(2, identity.ColumnDefinitions.Count);
                Assert.AreEqual(0, Grid.GetColumn(identity.Children[0]));
                Assert.AreEqual(1, Grid.GetColumn(identity.Children[1]));
                Assert.AreEqual(120, ((TextBlock)identity.Children[1]).MaxWidth);

                StackPanel status = (StackPanel)headerGrid.Children[1];
                Assert.AreEqual(206, status.MaxWidth);
                Assert.AreEqual(190, ((TextBlock)status.Children[1]).MaxWidth);
                Assert.IsInstanceOfType(headerGrid.Children[2], typeof(Button));
            }
            finally
            {
                viewModel.DisposeAsync().GetAwaiter().GetResult();
            }
        });
    }

    [TestMethod]
    public void SettingsToggle_FirstClick_ShowsToolsWindow()
    {
        WpfTestDispatcher.Run(() =>
        {
            EnsureApplicationResources();
            ViewDeviceViewModel viewModel = CreateViewModel();
            ViewDeviceWindow? window = null;
            try
            {
                window = ShowWindow(viewModel);
                Button settingsButton = (Button)window.FindName("SettingsButton")!;
                settingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                FlushDispatcher();

                ViewDeviceToolsWindow toolsWindow = Application.Current!.Windows
                    .OfType<ViewDeviceToolsWindow>()
                    .Single(tools => ReferenceEquals(tools.Owner, window));
                Assert.IsTrue(toolsWindow.IsVisible);
                Assert.AreEqual(true, settingsButton.Tag);
            }
            finally
            {
                CloseWindow(window);
                viewModel.DisposeAsync().GetAwaiter().GetResult();
            }
        });
    }

    [TestMethod]
    public void SettingsToggle_SecondClick_HidesToolsWindow()
    {
        WpfTestDispatcher.Run(() =>
        {
            EnsureApplicationResources();
            ViewDeviceViewModel viewModel = CreateViewModel();
            ViewDeviceWindow? window = null;
            try
            {
                window = ShowWindow(viewModel);
                Button settingsButton = (Button)window.FindName("SettingsButton")!;
                settingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                FlushDispatcher();
                ViewDeviceToolsWindow toolsWindow = Application.Current!.Windows
                    .OfType<ViewDeviceToolsWindow>()
                    .Single(tools => ReferenceEquals(tools.Owner, window));

                settingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                FlushDispatcher();

                Assert.IsFalse(toolsWindow.IsVisible);
                Assert.AreEqual(false, settingsButton.Tag);
            }
            finally
            {
                CloseWindow(window);
                viewModel.DisposeAsync().GetAwaiter().GetResult();
            }
        });
    }

    [TestMethod]
    public void SettingsToggle_DoesNotResizeOwner()
    {
        WpfTestDispatcher.Run(() =>
        {
            EnsureApplicationResources();
            ViewDeviceViewModel viewModel = CreateViewModel();
            ViewDeviceWindow? window = null;
            try
            {
                window = ShowWindow(viewModel);
                Border stream = (Border)window.FindName("StreamContainer")!;
                double width = window.Width;
                double actualWidth = window.ActualWidth;
                double streamWidth = stream.ActualWidth;

                Button settingsButton = (Button)window.FindName("SettingsButton")!;
                settingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                FlushDispatcher();
                window.UpdateLayout();

                Assert.AreEqual(width, window.Width, 0.01);
                Assert.IsTrue(Math.Abs(window.ActualWidth - actualWidth) < 0.01);
                Assert.IsTrue(Math.Abs(stream.ActualWidth - streamWidth) < 0.01);
            }
            finally
            {
                CloseWindow(window);
                viewModel.DisposeAsync().GetAwaiter().GetResult();
            }
        });
    }

    [TestMethod]
    public void SettingsToggle_ReusesSameToolsWindowInstance()
    {
        WpfTestDispatcher.Run(() =>
        {
            EnsureApplicationResources();
            ViewDeviceViewModel viewModel = CreateViewModel();
            ViewDeviceWindow? window = null;
            try
            {
                window = ShowWindow(viewModel);
                Button settingsButton = (Button)window.FindName("SettingsButton")!;

                settingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                FlushDispatcher();
                ViewDeviceToolsWindow first = Application.Current!.Windows
                    .OfType<ViewDeviceToolsWindow>()
                    .Single(tools => ReferenceEquals(tools.Owner, window));

                settingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                settingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                FlushDispatcher();
                ViewDeviceToolsWindow second = Application.Current!.Windows
                    .OfType<ViewDeviceToolsWindow>()
                    .Single(tools => ReferenceEquals(tools.Owner, window));

                Assert.AreSame(first, second);
                Assert.IsTrue(second.IsVisible);
            }
            finally
            {
                CloseWindow(window);
                viewModel.DisposeAsync().GetAwaiter().GetResult();
            }
        });
    }

    [TestMethod]
    public void ToolsWindow_FollowsOwnerLocation()
    {
        WpfTestDispatcher.Run(() =>
        {
            EnsureApplicationResources();
            ViewDeviceViewModel viewModel = CreateViewModel();
            ViewDeviceWindow? window = null;
            try
            {
                window = ShowWindow(viewModel);
                window.Height = 500;
                window.UpdateLayout();
                FlushDispatcher();
                window.Left = 100;
                window.Top = 100;
                FlushDispatcher();

                Button settingsButton = (Button)window.FindName("SettingsButton")!;
                settingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                FlushDispatcher();
                ViewDeviceToolsWindow toolsWindow = Application.Current!.Windows
                    .OfType<ViewDeviceToolsWindow>()
                    .Single(tools => ReferenceEquals(tools.Owner, window));
                double deltaX = toolsWindow.Left - window.Left;
                double deltaY = toolsWindow.Top - window.Top;

                window.Left += 120;
                window.Top += 80;
                FlushDispatcher();

                Assert.AreEqual(deltaX, toolsWindow.Left - window.Left, 0.5);
                Assert.AreEqual(deltaY, toolsWindow.Top - window.Top, 0.5);
            }
            finally
            {
                CloseWindow(window);
                viewModel.DisposeAsync().GetAwaiter().GetResult();
            }
        });
    }

    [TestMethod]
    public void OwnerClose_ClosesToolsWindow()
    {
        WpfTestDispatcher.Run(() =>
        {
            EnsureApplicationResources();
            ViewDeviceViewModel viewModel = CreateViewModel();
            ViewDeviceWindow? window = null;
            try
            {
                window = ShowWindow(viewModel);
                Button settingsButton = (Button)window.FindName("SettingsButton")!;
                settingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                FlushDispatcher();
                ViewDeviceToolsWindow toolsWindow = Application.Current!.Windows
                    .OfType<ViewDeviceToolsWindow>()
                    .Single(tools => ReferenceEquals(tools.Owner, window));

                window.Close();
                FlushDispatcher();

                Assert.IsFalse(toolsWindow.IsVisible);
                Assert.IsFalse(Application.Current!.Windows.OfType<ViewDeviceToolsWindow>().Contains(toolsWindow));
                window = null;
            }
            finally
            {
                CloseWindow(window);
                viewModel.DisposeAsync().GetAwaiter().GetResult();
            }
        });
    }

    [TestMethod]
    public void DifferentOwners_HaveIndependentToolsWindows()
    {
        WpfTestDispatcher.Run(() =>
        {
            EnsureApplicationResources();
            ViewDeviceViewModel firstViewModel = CreateViewModel();
            ViewDeviceViewModel secondViewModel = CreateViewModel();
            ViewDeviceWindow? firstWindow = null;
            ViewDeviceWindow? secondWindow = null;
            try
            {
                firstWindow = ShowWindow(firstViewModel);
                secondWindow = ShowWindow(secondViewModel);
                Button firstSettings = (Button)firstWindow.FindName("SettingsButton")!;
                firstSettings.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                FlushDispatcher();

                ViewDeviceToolsWindow firstTools = Application.Current!.Windows
                    .OfType<ViewDeviceToolsWindow>()
                    .Single(tools => ReferenceEquals(tools.Owner, firstWindow));
                Assert.IsTrue(firstTools.IsVisible);
                Assert.IsFalse(Application.Current.Windows
                    .OfType<ViewDeviceToolsWindow>()
                    .Any(tools => ReferenceEquals(tools.Owner, secondWindow)));
            }
            finally
            {
                CloseWindow(firstWindow);
                CloseWindow(secondWindow);
                firstViewModel.DisposeAsync().GetAwaiter().GetResult();
                secondViewModel.DisposeAsync().GetAwaiter().GetResult();
            }
        });
    }

    [TestMethod]
    public void ViewDeviceWindow_PackageDropAcceptsExactlyOneSupportedPackage()
    {
        Assert.IsTrue(ViewDeviceWindow.TryGetSupportedPackagePath(
            new DataObject(
                DataFormats.FileDrop,
                new[] { @"C:\Temp\package.apk" }),
            out string apkPath));
        Assert.AreEqual(@"C:\Temp\package.apk", apkPath);

        Assert.IsTrue(ViewDeviceWindow.TryGetSupportedPackagePath(
            new DataObject(
                DataFormats.FileDrop,
                new[] { @"C:\Temp\package.XAPK" }),
            out _));
        Assert.IsFalse(ViewDeviceWindow.TryGetSupportedPackagePath(
            new DataObject(
                DataFormats.FileDrop,
                new[] { @"C:\Temp\package.zip" }),
            out _));
        Assert.IsFalse(ViewDeviceWindow.TryGetSupportedPackagePath(
                new DataObject(
                    DataFormats.FileDrop,
                    new[] { @"C:\Temp\a.apk", @"C:\Temp\b.apk" }),
                out _));
    }

    [TestMethod]
    public void ViewDeviceWindow_DropOverlayResetsOnLeaveAndDrop()
    {
        WpfTestDispatcher.Run(() =>
        {
            EnsureApplicationResources();
            ViewDeviceViewModel viewModel = CreateInitializedOnlineViewModel();
            ViewDeviceWindow? window = null;
            try
            {
                window = new ViewDeviceWindow(viewModel);
                Border stream = (Border)window.FindName("StreamContainer")!;
                Border overlay = (Border)window.FindName("PackageDropOverlay")!;
                DataObject validPackage = new(
                    DataFormats.FileDrop,
                    new[] { @"C:\Temp\package.apk" });

                DragEventArgs enter = CreateDragEventArgs(validPackage, stream);
                enter.RoutedEvent = UIElement.PreviewDragEnterEvent;
                stream.RaiseEvent(enter);

                Assert.AreEqual(DragDropEffects.Copy, enter.Effects);
                Assert.AreEqual(Visibility.Visible, overlay.Visibility);

                DragEventArgs leave = CreateDragEventArgs(validPackage, stream);
                leave.RoutedEvent = UIElement.PreviewDragLeaveEvent;
                stream.RaiseEvent(leave);
                Assert.AreEqual(DragDropEffects.None, leave.Effects);
                Assert.AreEqual(Visibility.Collapsed, overlay.Visibility);

                DragEventArgs enterAgain = CreateDragEventArgs(validPackage, stream);
                enterAgain.RoutedEvent = UIElement.PreviewDragEnterEvent;
                stream.RaiseEvent(enterAgain);
                Assert.AreEqual(Visibility.Visible, overlay.Visibility);

                DragEventArgs drop = CreateDragEventArgs(
                    new DataObject(
                        DataFormats.FileDrop,
                        new[] { @"C:\Temp\unsupported.zip" }),
                    stream);
                drop.RoutedEvent = UIElement.DropEvent;
                stream.RaiseEvent(drop);
                Assert.AreEqual(DragDropEffects.None, drop.Effects);
                Assert.AreEqual(Visibility.Collapsed, overlay.Visibility);
            }
            finally
            {
                CloseWindow(window);
                viewModel.DisposeAsync().GetAwaiter().GetResult();
            }
        });
    }

    [TestMethod]
    public void RenameAction_UsesReusableDetailWindowWithoutChangingToolsSize()
    {
        WpfTestDispatcher.Run(() =>
        {
            EnsureApplicationResources();
            ViewDeviceViewModel viewModel = CreateOfflineViewModel();
            ViewDeviceWindow? window = null;
            try
            {
                viewModel.InitializeAsync("SERIAL", "Device").GetAwaiter().GetResult();
                window = ShowWindow(viewModel);
                Button settingsButton = (Button)window.FindName("SettingsButton")!;
                settingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                FlushDispatcher();
                ViewDeviceToolsWindow tools = Application.Current!.Windows
                    .OfType<ViewDeviceToolsWindow>()
                    .Single(candidate => ReferenceEquals(candidate.Owner, window));
                double toolsWidth = tools.Width;

                viewModel.RenameConfigCommand.Execute(null);
                FlushDispatcher();

                ViewDeviceToolDetailWindow detail = Application.Current!.Windows
                    .OfType<ViewDeviceToolDetailWindow>()
                    .Single(candidate => candidate.IsVisible);
                Assert.IsTrue(detail.IsVisible);
                Assert.AreEqual(toolsWidth, tools.Width, 0.01);
                Assert.AreEqual(Visibility.Visible, ((FrameworkElement)detail.FindName("RenameContent")!).Visibility);
                Assert.IsNull(tools.FindName("RenameEditorPanel"));

                tools.DismissDetailWindow();
                FlushDispatcher();
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

    [TestMethod]
    public void ViewDeviceWindow_ClickDismissesToolDetail()
    {
        WpfTestDispatcher.Run(() =>
        {
            EnsureApplicationResources();
            ViewDeviceViewModel viewModel = CreateOfflineViewModel();
            ViewDeviceWindow? window = null;
            try
            {
                window = ShowWindow(viewModel);
                Button settingsButton = (Button)window.FindName("SettingsButton")!;
                settingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                FlushDispatcher();

                viewModel.RenameConfigCommand.Execute(null);
                FlushDispatcher();
                ViewDeviceToolDetailWindow detail = Application.Current!.Windows
                    .OfType<ViewDeviceToolDetailWindow>()
                    .Single(candidate => candidate.IsVisible);
                Assert.IsTrue(detail.IsVisible);

                RaisePreviewMouseDown((UIElement)window.FindName("StreamContainer")!);
                FlushDispatcher();

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

    [TestMethod]
    public void FirstPortraitFrame_PreservesCompactWindowHeight()
    {
        WpfTestDispatcher.Run(() =>
        {
            EnsureApplicationResources();
            ViewDeviceViewModel viewModel = CreateViewModel();
            ViewDeviceWindow? window = null;
            try
            {
                viewModel.InitializeAsync("SERIAL", "Device").GetAwaiter().GetResult();
                window = ShowWindow(viewModel);
                viewModel.SetContentSizeForTesting(1080, 2220);
                FlushDispatcher();
                window.UpdateLayout();

                Border stream = (Border)window.FindName("StreamContainer")!;
                Assert.IsTrue(window.Height <= 656.5);
                Assert.AreEqual(
                    1080d / 2220d,
                    stream.ActualWidth / stream.ActualHeight,
                    0.12,
                    $"window={window.Width}x{window.Height}, stream={stream.ActualWidth}x{stream.ActualHeight}");
            }
            finally
            {
                CloseWindow(window);
                viewModel.DisposeAsync().GetAwaiter().GetResult();
            }
        });
    }

    [TestMethod]
    public void ViewDeviceWindow_RealContentAspectChangeUsesEffectiveViewAspect()
    {
        WpfTestDispatcher.Run(() =>
        {
            EnsureApplicationResources();
            ViewDeviceViewModel viewModel = CreateViewModel();
            ViewDeviceWindow? window = null;
            try
            {
                viewModel.InitializeAsync("SERIAL", "Device").GetAwaiter().GetResult();
                window = ShowWindow(viewModel);
                viewModel.SetContentSizeForTesting(1080, 2220);
                FlushDispatcher();
                window.UpdateLayout();
                double portraitHeight = window.Height;
                double portraitWidth = window.Width;

                viewModel.SetContentSizeForTesting(2220, 1080);
                FlushDispatcher();
                window.UpdateLayout();

                Assert.IsTrue(window.Width > portraitWidth);
                Assert.IsTrue(window.Height <= portraitHeight + 1);
            }
            finally
            {
                CloseWindow(window);
                viewModel.DisposeAsync().GetAwaiter().GetResult();
            }
        });
    }

    [TestMethod]
    public void FourRotations_ReturnToOriginalDimensionsAndLeaveHostUiUpright()
    {
        WpfTestDispatcher.Run(() =>
        {
            EnsureApplicationResources();
            ViewDeviceViewModel viewModel = CreateViewModel();
            ViewDeviceWindow? window = null;
            try
            {
                window = ShowWindow(viewModel);
                viewModel.SetContentSizeForTesting(1080, 2220);
                FlushDispatcher();
                window.UpdateLayout();

                Border stream = (Border)window.FindName("StreamContainer")!;
                ScrcpyNet.Wpf.ScrcpyDisplay display =
                    (ScrcpyNet.Wpf.ScrcpyDisplay)window.FindName("ScrcpyDisplay")!;
                Grid rotatedDisplayHost = (Grid)window.FindName("RotatedDisplayHost")!;
                RotateTransform rotationTransform = (RotateTransform)window.FindName("ViewRotationTransform")!;
                double initialStreamWidth = stream.ActualWidth;
                double initialStreamHeight = stream.ActualHeight;

                void AssertDisplayInputCoordinatesRoundTrip()
                {
                    Assert.IsTrue(display.ActualWidth > 0);
                    Assert.IsTrue(display.ActualHeight > 0);
                    GeneralTransform toStream = display.TransformToAncestor(stream);
                    GeneralTransform toDisplay = stream.TransformToDescendant(display);
                    Point[] displayPoints =
                    [
                        new(display.ActualWidth / 2, display.ActualHeight / 2),
                        new(display.ActualWidth * 0.08, display.ActualHeight * 0.08),
                        new(display.ActualWidth * 0.92, display.ActualHeight * 0.08),
                        new(display.ActualWidth * 0.08, display.ActualHeight * 0.92),
                        new(display.ActualWidth * 0.92, display.ActualHeight * 0.92)
                    ];

                    foreach (Point expected in displayPoints)
                    {
                        Point streamPoint = toStream.Transform(expected);
                        Point actual = toDisplay.Transform(streamPoint);
                        Assert.IsTrue(double.IsFinite(streamPoint.X) && double.IsFinite(streamPoint.Y));
                        Assert.AreEqual(expected.X, actual.X, 0.5);
                        Assert.AreEqual(expected.Y, actual.Y, 0.5);
                    }
                }

                Assert.AreEqual(0, viewModel.ViewRotationQuarterTurns);
                Assert.AreEqual(0d, viewModel.ViewRotationAngle);
                Assert.IsTrue(display.IsHitTestVisible);
                Assert.IsTrue(display.Focusable);
                Assert.IsFalse(((FrameworkElement)window.FindName("HeaderPanel")!).LayoutTransform is RotateTransform);
                Assert.IsFalse(((FrameworkElement)window.FindName("NavigationPanel")!).LayoutTransform is RotateTransform);
                AssertDisplayInputCoordinatesRoundTrip();

                viewModel.RotateViewCommand.Execute(null);
                FlushDispatcher();
                window.UpdateLayout();

                Assert.AreEqual(1, viewModel.ViewRotationQuarterTurns);
                Assert.AreEqual(90d, viewModel.ViewRotationAngle);
                Assert.AreEqual(90d, rotationTransform.Angle);
                Assert.AreEqual(initialStreamHeight, stream.ActualWidth, 3d);
                Assert.AreEqual(initialStreamWidth, stream.ActualHeight, 3d);
                AssertDisplayInputCoordinatesRoundTrip();

                viewModel.RotateViewCommand.Execute(null);
                FlushDispatcher();
                window.UpdateLayout();
                Assert.AreEqual(2, viewModel.ViewRotationQuarterTurns);
                Assert.AreEqual(180d, rotationTransform.Angle);
                Assert.AreEqual(initialStreamWidth, stream.ActualWidth, 3d);
                Assert.AreEqual(initialStreamHeight, stream.ActualHeight, 3d);
                AssertDisplayInputCoordinatesRoundTrip();

                viewModel.RotateViewCommand.Execute(null);
                FlushDispatcher();
                window.UpdateLayout();
                Assert.AreEqual(3, viewModel.ViewRotationQuarterTurns);
                Assert.AreEqual(270d, rotationTransform.Angle);
                AssertDisplayInputCoordinatesRoundTrip();

                viewModel.RotateViewCommand.Execute(null);
                FlushDispatcher();
                window.UpdateLayout();
                Assert.AreEqual(0, viewModel.ViewRotationQuarterTurns);
                Assert.AreEqual(0d, rotationTransform.Angle);
                Assert.AreEqual(initialStreamWidth, stream.ActualWidth, 3d);
                Assert.AreEqual(initialStreamHeight, stream.ActualHeight, 3d);
                AssertDisplayInputCoordinatesRoundTrip();
            }
            finally
            {
                CloseWindow(window);
                viewModel.DisposeAsync().GetAwaiter().GetResult();
            }
        });
    }

    [TestMethod]
    public void Rotate_AfterManualResize_RemainsStable()
    {
        WpfTestDispatcher.Run(() =>
        {
            EnsureApplicationResources();
            ViewDeviceViewModel viewModel = CreateViewModel();
            ViewDeviceWindow? window = null;
            try
            {
                window = ShowWindow(viewModel);
                viewModel.SetContentSizeForTesting(1080, 2220);
                FlushDispatcher();
                window.Width = 520;
                window.Height = 600;
                window.UpdateLayout();
                double originalWidth = window.ActualWidth;
                double originalHeight = window.ActualHeight;

                for (int rotation = 0; rotation < 4; rotation++)
                {
                    viewModel.RotateViewCommand.Execute(null);
                    FlushDispatcher();
                    window.UpdateLayout();

                    Assert.IsTrue(double.IsFinite(window.ActualWidth));
                    Assert.IsTrue(double.IsFinite(window.ActualHeight));
                    Assert.IsTrue(window.ActualWidth >= window.MinWidth);
                    Assert.IsTrue(window.ActualHeight >= window.MinHeight);
                }

                Assert.AreEqual(originalWidth, window.ActualWidth, 4);
                Assert.AreEqual(originalHeight, window.ActualHeight, 4);
            }
            finally
            {
                CloseWindow(window);
                viewModel.DisposeAsync().GetAwaiter().GetResult();
            }
        });
    }

    [TestMethod]
    public void Rotate_ClampedByMonitorWorkArea_DoesNotGrowOutsideMonitor()
    {
        WpfTestDispatcher.Run(() =>
        {
            EnsureApplicationResources();
            ViewDeviceViewModel viewModel = CreateViewModel();
            ViewDeviceWindow? window = null;
            try
            {
                window = ShowWindow(viewModel);
                viewModel.SetContentSizeForTesting(1080, 2220);
                FlushDispatcher();
                Rect workArea = ViewDeviceMonitorWorkArea.GetFor(window);
                window.Left = workArea.Right - 10;
                window.Top = workArea.Bottom - 10;
                FlushDispatcher();

                for (int rotation = 0; rotation < 4; rotation++)
                {
                    viewModel.RotateViewCommand.Execute(null);
                    FlushDispatcher();
                    window.UpdateLayout();

                    Assert.IsTrue(window.Left >= workArea.Left - 1);
                    Assert.IsTrue(window.Top >= workArea.Top - 1);
                    Assert.IsTrue(window.Left + window.ActualWidth <= workArea.Right + 1);
                    Assert.IsTrue(window.Top + window.ActualHeight <= workArea.Bottom + 1);
                }
            }
            finally
            {
                CloseWindow(window);
                viewModel.DisposeAsync().GetAwaiter().GetResult();
            }
        });
    }

    [TestMethod]
    public void ToolsWindow_FollowsViewDeviceAfterEveryRotation()
    {
        WpfTestDispatcher.Run(() =>
        {
            EnsureApplicationResources();
            ViewDeviceViewModel viewModel = CreateViewModel();
            ViewDeviceWindow? window = null;
            try
            {
                window = ShowWindow(viewModel);
                viewModel.SetContentSizeForTesting(1080, 2220);
                FlushDispatcher();
                window.Left = 100;
                window.Top = 100;
                Button settingsButton = (Button)window.FindName("SettingsButton")!;
                settingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                FlushDispatcher();
                ViewDeviceToolsWindow tools = Application.Current!.Windows
                    .OfType<ViewDeviceToolsWindow>()
                    .Single(candidate => ReferenceEquals(candidate.Owner, window));

                for (int rotation = 0; rotation < 4; rotation++)
                {
                    if (rotation > 0)
                    {
                        viewModel.RotateViewCommand.Execute(null);
                        FlushDispatcher();
                    }

                    window.UpdateLayout();
                    tools.UpdateLayout();
                    Rect workArea = ViewDeviceMonitorWorkArea.GetFor(window);
                    Rect ownerBounds = new(
                        window.Left,
                        window.Top,
                        window.ActualWidth > 0 ? window.ActualWidth : window.Width,
                        window.ActualHeight > 0 ? window.ActualHeight : window.Height);
                    Size toolsSize = new(
                        tools.ActualWidth > 0 ? tools.ActualWidth : tools.Width,
                        tools.ActualHeight > 0 ? tools.ActualHeight : tools.Height);
                    Point expected = ViewDeviceToolsPlacement.Calculate(
                        ownerBounds,
                        toolsSize,
                        workArea,
                        visibleGap: 12,
                        shadowMargin: 8);

                    Assert.AreSame(window, tools.Owner);
                    Assert.AreEqual(expected.X, tools.Left, 1);
                    Assert.AreEqual(expected.Y, tools.Top, 1);
                }
            }
            finally
            {
                CloseWindow(window);
                viewModel.DisposeAsync().GetAwaiter().GetResult();
            }
        });
    }

    private static ViewDeviceWindow ShowWindow(ViewDeviceViewModel viewModel)
    {
        ViewDeviceWindow window = new(viewModel);
        window.Show();
        window.UpdateLayout();
        FlushDispatcher();
        return window;
    }

    private static void CloseWindow(ViewDeviceWindow? window)
    {
        if (window is null)
            return;

        window.DataContext = null;
        if (Application.Current?.Windows.OfType<Window>().Any(candidate => ReferenceEquals(candidate, window)) != true)
            return;

        window.Close();
        FlushDispatcher();
    }

    private static void FlushDispatcher()
    {
        DispatcherFrame frame = new();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
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

    private static void AssertCommandBinding(Button button, string commandName)
    {
        Binding? binding = BindingOperations.GetBinding(button, Button.CommandProperty);
        Assert.IsNotNull(binding);
        Assert.AreEqual(commandName, binding!.Path.Path);
    }

    private static void EnsureApplicationResources()
    {
        WpfTestDispatcher.EnsureApplicationResources();
    }

    private static ViewDeviceViewModel CreateViewModel()
    {
        ILocalizationService localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());

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

    private static ViewDeviceViewModel CreateOfflineViewModel()
    {
        ILocalizationService localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());

        return new ViewDeviceViewModel(
            Substitute.For<ISingleViewDeviceSessionFactory>(),
            new FakeTracker([new AdbDevice("SERIAL", AdbDeviceStatus.Offline)]),
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

    private static ViewDeviceViewModel CreateInitializedOnlineViewModel()
    {
        var tracker = new FakeTracker([new AdbDevice("SERIAL", AdbDeviceStatus.Online)]);
        var sessionFactory = new FakeSessionFactory(new FakeViewDeviceSession("SERIAL"));
        ILocalizationService localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        var viewModel = new ViewDeviceViewModel(
            sessionFactory,
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

    private static DragEventArgs CreateDragEventArgs(IDataObject data, DependencyObject target)
    {
        return (DragEventArgs)Activator.CreateInstance(
            typeof(DragEventArgs),
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic,
            binder: null,
            args:
            [
                data,
                DragDropKeyStates.LeftMouseButton,
                DragDropEffects.Copy,
                target,
                new Point()
            ],
            culture: null)!;
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
