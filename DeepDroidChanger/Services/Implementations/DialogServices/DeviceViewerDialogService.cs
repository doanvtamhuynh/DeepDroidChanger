using System.Windows;
using DeepDroidChanger.Services;
using DeepDroidChanger.ViewModels;
using DeepDroidChanger.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services;

public sealed class DeviceViewerDialogService : IDeviceViewerDialogService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DeviceViewerDialogService> _logger;
    private readonly DeviceViewerRegistry<DeviceViewerRuntime> _activeViewers = new();

    public DeviceViewerDialogService(
        IServiceScopeFactory scopeFactory,
        ILogger<DeviceViewerDialogService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public Task ShowDeviceViewerAsync(string serial, string name, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ArgumentNullException.ThrowIfNull(name);
        cancellationToken.ThrowIfCancellationRequested();

        return _activeViewers.GetOrCreateAsync(
            serial,
            runtime => runtime.IsLive,
            () => CreateViewerAsync(serial, name, cancellationToken),
            ActivateViewerAsync,
            cancellationToken);
    }

    private async Task<DeviceViewerRuntime> CreateViewerAsync(
        string serial,
        string name,
        CancellationToken cancellationToken)
    {
        var scope = _scopeFactory.CreateScope();
        try
        {
            var runtime = await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var viewModel = scope.ServiceProvider.GetRequiredService<DeviceViewerViewModel>();
                viewModel.Initialize(serial, name);

                var window = scope.ServiceProvider.GetRequiredService<DeviceViewerDialog>();
                window.DataContext = viewModel;
                if (Application.Current.MainWindow is { IsVisible: true } owner)
                    window.Owner = owner;

                return new DeviceViewerRuntime(
                    window,
                    viewModel,
                    serial,
                    scope,
                    scope.ServiceProvider.GetRequiredService<IDeviceViewerStreamService>(),
                    scope.ServiceProvider.GetRequiredService<IDeviceViewerCoordinatorService>(),
                    scope.ServiceProvider.GetRequiredService<IAdbDeviceTrackerService>(),
                    scope.ServiceProvider.GetRequiredService<ILogger<DeviceViewerRuntime>>(),
                    runtime => _activeViewers.Remove(serial, runtime));
            }).Task.ConfigureAwait(false);

            await Application.Current.Dispatcher.InvokeAsync(runtime.Window.Show).Task.ConfigureAwait(false);
            return runtime;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to create device viewer for {Serial}.", serial);
            scope.Dispose();
            throw;
        }
    }

    private static Task ActivateViewerAsync(DeviceViewerRuntime runtime)
    {
        return Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (!runtime.IsLive)
                return;

            if (runtime.Window.WindowState == WindowState.Minimized)
                runtime.Window.WindowState = WindowState.Normal;

            if (!runtime.Window.IsVisible)
                runtime.Window.Show();

            runtime.Window.Activate();
        }).Task;
    }
}
