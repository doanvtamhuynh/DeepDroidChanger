using System.Runtime.ExceptionServices;
using System.Windows;
using DeepDroidChanger.Tests.ViewModels;
using DeepDroidChanger.ViewModels;
using DeepDroidChanger.Views;
using Microsoft.Extensions.Logging.Abstractions;

namespace DeepDroidChanger.Tests.Views.ViewMultipleDevices;

[TestClass]
[DoNotParallelize]
public sealed class ViewMultipleDevicesViewsSmokeTests
{
    [TestMethod]
    public void ViewMultipleDevicesViews_InstantiateWithApplicationResources()
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

            FakeTracker tracker = new();
            ViewMultipleDevicesViewModel viewModel = new(
                new FakeDeviceStoreService(),
                tracker,
                new FakeSessionFactory(),
                new FakeLocalizationService(),
                new ImmediateUiDispatcher(),
                NullLoggerFactory.Instance,
                NullLogger<ViewMultipleDevicesViewModel>.Instance);

            try
            {
                _ = new ViewMultipleDevicesView(
                    viewModel,
                    NullLogger<ViewMultipleDevicesView>.Instance);
                _ = new ViewMultipleDeviceTile();
            }
            finally
            {
                viewModel.DisposeAsync().GetAwaiter().GetResult();
            }
        });
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
}
