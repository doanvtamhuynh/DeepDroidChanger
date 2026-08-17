using System.Windows;
using DeepDroidChanger.ViewModels;
using DeepDroidChanger.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services;

public sealed class DeviceViewerDialogService : IDeviceViewerDialogService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DeviceViewerDialogService> _logger;
    private readonly DeviceViewerRegistry<DeviceViewerEntry> _activeViewers = new();

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
            entry => entry.IsLive,
            () => CreateViewerAsync(serial, name, cancellationToken),
            ActivateViewerAsync,
            cancellationToken);
    }

    private async Task<DeviceViewerEntry> CreateViewerAsync(
        string serial,
        string name,
        CancellationToken cancellationToken)
    {
        IServiceScope scope = _scopeFactory.CreateScope();
        try
        {
            DeviceViewerEntry entry = await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                DeviceViewerViewModel viewModel = scope.ServiceProvider
                    .GetRequiredService<DeviceViewerViewModel>();
                viewModel.Initialize(serial, name);

                DeviceViewerDialog window = scope.ServiceProvider
                    .GetRequiredService<DeviceViewerDialog>();
                window.DataContext = viewModel;
                if (Application.Current.MainWindow is { IsVisible: true } owner)
                    window.Owner = owner;

                var created = new DeviceViewerEntry(window, scope);
                window.Closed += (_, _) =>
                {
                    created.MarkClosed();
                    _activeViewers.Remove(serial, created);
                };
                return created;
            }).Task.ConfigureAwait(false);

            await Application.Current.Dispatcher.InvokeAsync(entry.Window.Show)
                .Task.ConfigureAwait(false);
            return entry;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to create device viewer for {Serial}.", serial);
            scope.Dispose();
            throw;
        }
    }

    private static Task ActivateViewerAsync(DeviceViewerEntry entry)
    {
        return Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (!entry.IsLive)
                return;

            if (entry.Window.WindowState == WindowState.Minimized)
                entry.Window.WindowState = WindowState.Normal;

            if (!entry.Window.IsVisible)
                entry.Window.Show();

            entry.Window.Activate();
        }).Task;
    }

    private sealed class DeviceViewerEntry
    {
        private readonly IServiceScope _scope;
        private int _closed;

        public DeviceViewerEntry(DeviceViewerDialog window, IServiceScope scope)
        {
            Window = window;
            _scope = scope;
        }

        public DeviceViewerDialog Window { get; }

        public bool IsLive => Volatile.Read(ref _closed) == 0;

        public void MarkClosed()
        {
            if (Interlocked.Exchange(ref _closed, 1) == 0)
                _scope.Dispose();
        }
    }
}
