using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using DeepDroidChanger.ViewModels;
using DeepDroidChanger.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services;

public sealed class ViewDeviceWindowService : IViewDeviceWindowService, IAsyncDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IUiDispatcherService _uiDispatcher;
    private readonly ILogger<ViewDeviceWindowService> _logger;
    private readonly object _gate = new();
    private readonly Dictionary<string, ViewDeviceWindowEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private int _disposed;

    public ViewDeviceWindowService(
        IServiceScopeFactory scopeFactory,
        IUiDispatcherService uiDispatcher,
        ILogger<ViewDeviceWindowService> logger)
    {
        _scopeFactory = scopeFactory;
        _uiDispatcher = uiDispatcher;
        _logger = logger;
    }

    public async Task OpenAsync(
        string serial,
        string? displayName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (!_uiDispatcher.CheckAccess())
        {
            Dispatcher? dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
                throw new InvalidOperationException("The WPF dispatcher is unavailable.");

            Func<Task> open = () => OpenCoreAsync(serial, displayName, cancellationToken);
            await dispatcher.InvokeAsync(open, DispatcherPriority.Normal, cancellationToken)
                .Task
                .Unwrap()
                .ConfigureAwait(false);
            return;
        }

        await OpenCoreAsync(serial, displayName, cancellationToken).ConfigureAwait(true);
    }

    public async Task CloseAllAsync(CancellationToken cancellationToken = default)
    {
        if (!_uiDispatcher.CheckAccess())
        {
            Dispatcher? dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
                return;

            Func<Task> close = () => CloseAllCoreAsync(cancellationToken);
            await dispatcher.InvokeAsync(close, DispatcherPriority.Normal, cancellationToken)
                .Task
                .Unwrap()
                .ConfigureAwait(false);
            return;
        }

        await CloseAllCoreAsync(cancellationToken).ConfigureAwait(true);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await CloseAllAsync().ConfigureAwait(true);
    }

    private async Task OpenCoreAsync(
        string serial,
        string? displayName,
        CancellationToken cancellationToken)
    {
        ViewDeviceWindowEntry? existing;
        lock (_gate)
            _entries.TryGetValue(serial, out existing);

        if (existing != null)
        {
            if (Volatile.Read(ref existing.ClosePreparationStarted) != 0)
            {
                await existing.Closed.Task.WaitAsync(cancellationToken).ConfigureAwait(true);
                await OpenCoreAsync(serial, displayName, cancellationToken).ConfigureAwait(true);
                return;
            }

            if (existing.Window.IsVisible)
            {
                if (existing.Window.WindowState == WindowState.Minimized)
                    existing.Window.WindowState = WindowState.Normal;
                existing.Window.Activate();
                return;
            }

            await existing.Closed.Task.WaitAsync(cancellationToken).ConfigureAwait(true);
            await OpenCoreAsync(serial, displayName, cancellationToken).ConfigureAwait(true);
            return;
        }

        AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        ViewDeviceViewModel viewModel = scope.ServiceProvider.GetRequiredService<ViewDeviceViewModel>();
        ViewDeviceWindow window = new(viewModel)
        {
            Title = FormatWindowTitle(serial, displayName)
        };
        ViewDeviceWindowEntry entry = new(serial, scope, viewModel, window);

        lock (_gate)
            _entries.Add(serial, entry);

        window.Closing += OnWindowClosing;
        window.Closed += OnWindowClosed;
        try
        {
            window.Show();
            await viewModel.InitializeAsync(serial, displayName, cancellationToken).ConfigureAwait(true);
        }
        catch
        {
            window.Close();
            await entry.Closed.Task.ConfigureAwait(true);
            throw;
        }

        async void OnWindowClosing(object? sender, CancelEventArgs eventArgs)
        {
            if (Volatile.Read(ref entry.CloseReady) != 0)
                return;

            eventArgs.Cancel = true;
            if (Interlocked.Exchange(ref entry.ClosePreparationStarted, 1) != 0)
                return;

            try
            {
                await entry.ViewModel.DisposeAsync().ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Failed to stop View Device before closing its window for {Serial}.",
                    entry.Serial);
            }
            finally
            {
                Volatile.Write(ref entry.CloseReady, 1);
                if (!window.Dispatcher.HasShutdownStarted)
                    window.Close();
            }
        }

        void OnWindowClosed(object? sender, EventArgs eventArgs)
        {
            window.Closing -= OnWindowClosing;
            window.Closed -= OnWindowClosed;
            _ = DisposeEntryAsync(entry);
        }
    }

    internal static string FormatWindowTitle(string serial, string? displayName)
    {
        string normalizedSerial = serial.Trim();
        string? normalizedName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        return normalizedName is null ||
               string.Equals(normalizedName, normalizedSerial, StringComparison.OrdinalIgnoreCase)
            ? normalizedSerial
            : $"{normalizedName} - {normalizedSerial}";
    }

    private async Task CloseAllCoreAsync(CancellationToken cancellationToken)
    {
        ViewDeviceWindowEntry[] entries;
        lock (_gate)
            entries = _entries.Values.ToArray();

        foreach (ViewDeviceWindowEntry entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.Window.IsVisible)
                entry.Window.Close();
            else
                _ = DisposeEntryAsync(entry);
        }

        await Task.WhenAll(entries.Select(entry => entry.Closed.Task.WaitAsync(cancellationToken)))
            .ConfigureAwait(true);
    }

    private async Task DisposeEntryAsync(ViewDeviceWindowEntry entry)
    {
        if (Interlocked.Exchange(ref entry.DisposeStarted, 1) != 0)
        {
            await entry.Closed.Task.ConfigureAwait(true);
            return;
        }

        try
        {
            await entry.ViewModel.DisposeAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to close View Device window for {Serial}.", entry.Serial);
        }
        finally
        {
            try
            {
                await entry.Scope.DisposeAsync().ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Failed to dispose View Device scope for {Serial}.", entry.Serial);
            }

            lock (_gate)
            {
                if (_entries.TryGetValue(entry.Serial, out ViewDeviceWindowEntry? current) &&
                    ReferenceEquals(current, entry))
                {
                    _entries.Remove(entry.Serial);
                }
            }

            entry.Closed.TrySetResult(null);
        }
    }

    private sealed class ViewDeviceWindowEntry(
        string serial,
        AsyncServiceScope scope,
        ViewDeviceViewModel viewModel,
        ViewDeviceWindow window)
    {
        public string Serial { get; } = serial;
        public AsyncServiceScope Scope { get; } = scope;
        public ViewDeviceViewModel ViewModel { get; } = viewModel;
        public ViewDeviceWindow Window { get; } = window;
        public TaskCompletionSource<object?> Closed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ClosePreparationStarted;
        public int CloseReady;
        public int DisposeStarted;
    }
}
