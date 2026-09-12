using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using DeepDroidChanger.Services;
using DeepDroidChanger.ViewDevices.Contracts;
using DeepDroidChanger.ViewModels;
using DeepDroidChanger.Views;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DeepDroidChanger.Tests.Views.ViewDevice;

[TestClass]
[DoNotParallelize]
public sealed class ViewDeviceWindowSmokeTests
{
    [TestMethod]
    public void ViewDeviceWindow_InstantiatesWithApplicationResourcesAndScrcpyDisplay()
    {
        RunOnSta(() =>
        {
            Application application = Application.Current ?? new DeepDroidChanger.App();
            if (application is not DeepDroidChanger.App app)
            {
                throw new InvalidOperationException(
                    "The WPF smoke test requires the DeepDroidChanger application instance.");
            }

            app.InitializeComponent();
            ViewDeviceViewModel viewModel = CreateViewModel();
            try
            {
                ViewDeviceWindow window = new(viewModel);

                object? display = window.FindName("ScrcpyDisplay");
                Assert.IsNotNull(display);
                Assert.IsInstanceOfType(display, typeof(ScrcpyNet.Wpf.ScrcpyDisplay));
                ScrcpyNet.Wpf.ScrcpyDisplay scrcpyDisplay = (ScrcpyNet.Wpf.ScrcpyDisplay)display;
                Assert.IsNull(scrcpyDisplay.Scrcpy);
                Assert.AreEqual("DeepDroidChanger.ViewDevices", scrcpyDisplay.GetType().Assembly.GetName().Name);
                scrcpyDisplay.ApplyTemplate();
                Assert.IsNotNull(scrcpyDisplay.Template);
                Assert.IsNull(window.FindName("NativeHost"));
                Assert.IsNull(window.FindName("ActionsPanel"));

                ScrollViewer toolbarActions =
                    (ScrollViewer)window.FindName("ToolbarActionsScrollViewer");
                Assert.AreEqual(ScrollBarVisibility.Hidden, toolbarActions.VerticalScrollBarVisibility);
                Assert.AreEqual(ScrollBarVisibility.Disabled, toolbarActions.HorizontalScrollBarVisibility);
                Assert.IsFalse(toolbarActions.Focusable);
                Assert.IsFalse(toolbarActions.IsTabStop);

                Style toolbarStyle = (Style)window.Resources["ViewDeviceToolbarButtonStyle"];
                Assert.AreEqual(
                    false,
                    toolbarStyle.Setters
                        .OfType<Setter>()
                        .Single(setter => setter.Property == Button.FocusableProperty)
                        .Value);
                Assert.AreEqual(
                    false,
                    toolbarStyle.Setters
                        .OfType<Setter>()
                        .Single(setter => setter.Property == Button.IsTabStopProperty)
                        .Value);
                Assert.IsFalse(
                    toolbarStyle.Triggers
                        .OfType<Trigger>()
                        .Any(trigger => trigger.Property == Button.IsKeyboardFocusedProperty));
            }
            finally
            {
                viewModel.DisposeAsync().GetAwaiter().GetResult();
            }
        });
    }

    [TestMethod]
    public void ViewDeviceWindow_HeaderKeepsIdentityAndStatusInSeparateColumns()
    {
        RunOnSta(() =>
        {
            Application application = Application.Current ?? new DeepDroidChanger.App();
            if (application is not DeepDroidChanger.App app)
            {
                throw new InvalidOperationException(
                    "The WPF smoke test requires the DeepDroidChanger application instance.");
            }

            app.InitializeComponent();
            ViewDeviceViewModel viewModel = CreateViewModel();
            try
            {
                ViewDeviceWindow window = new(viewModel);
                Border header = (Border)window.FindName("HeaderPanel");
                Grid headerGrid = (Grid)header.Child;

                Assert.AreEqual(2, headerGrid.ColumnDefinitions.Count);
                Assert.AreEqual(GridUnitType.Star, headerGrid.ColumnDefinitions[0].Width.GridUnitType);
                Assert.AreEqual(GridUnitType.Auto, headerGrid.ColumnDefinitions[1].Width.GridUnitType);
                Assert.AreEqual(0, Grid.GetColumn(headerGrid.Children[0]));
                Assert.AreEqual(1, Grid.GetColumn(headerGrid.Children[1]));

                Grid identity = (Grid)headerGrid.Children[0];
                Assert.AreEqual(2, identity.ColumnDefinitions.Count);
                Assert.AreEqual(0, Grid.GetColumn(identity.Children[0]));
                Assert.AreEqual(1, Grid.GetColumn(identity.Children[1]));
                Assert.AreEqual(120, ((TextBlock)identity.Children[1]).MaxWidth);

                StackPanel status = (StackPanel)headerGrid.Children[1];
                Assert.AreEqual(206, status.MaxWidth);
                Assert.AreEqual(190, ((TextBlock)status.Children[1]).MaxWidth);
            }
            finally
            {
                viewModel.DisposeAsync().GetAwaiter().GetResult();
            }
        });
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
            NullLogger<ViewDeviceViewModel>.Instance);
    }

    private static void RunOnSta(Action operation)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                operation();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
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
