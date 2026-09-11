using System.Runtime.ExceptionServices;
using System.Windows;
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
                Assert.IsNull(((ScrcpyNet.Wpf.ScrcpyDisplay)display).Scrcpy);
                Assert.IsNull(window.FindName("NativeHost"));
                Assert.IsNull(window.FindName("ActionsPanel"));
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
