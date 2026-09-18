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
    private readonly IViewDevicePresentationCoordinator _presentationCoordinator;
    private readonly Func<Task>? _beforeEntryPublication;
    private readonly object _gate = new();
    private readonly Dictionary<string, ViewDeviceWindowEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TaskCompletionSource<object?>> _openingEntries =
        new(StringComparer.OrdinalIgnoreCase);
    private int _disposed;

    public ViewDeviceWindowService(
        IServiceScopeFactory scopeFactory,
        IUiDispatcherService uiDispatcher,
        ILogger<ViewDeviceWindowService> logger,
        IViewDevicePresentationCoordinator presentationCoordinator)
        : this(scopeFactory, uiDispatcher, logger, presentationCoordinator, null)
    {
    }

    internal ViewDeviceWindowService(
        IServiceScopeFactory scopeFactory,
        IUiDispatcherService uiDispatcher,
        ILogger<ViewDeviceWindowService> logger,
        IViewDevicePresentationCoordinator presentationCoordinator,
        Func<Task>? beforeEntryPublication)
    {
        _scopeFactory = scopeFactory;
        _uiDispatcher = uiDispatcher;
        _logger = logger;
        _presentationCoordinator = presentationCoordinator ?? throw new ArgumentNullException(nameof(presentationCoordinator));
        _beforeEntryPublication = beforeEntryPublication;
    }

    internal int TrackedEntryCountForTesting
    {
        get
        {
            lock (_gate)
                return _entries.Count;
        }
    }

    internal int OpeningEntryCountForTesting
    {
        get
        {
            lock (_gate)
                return _openingEntries.Count;
        }
    }

    public async Task OpenAsync(
        string serial,
        string? displayName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        serial = serial.Trim();
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
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

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

        TaskCompletionSource<object?>? openingWaiter = null;
        TaskCompletionSource<object?>? openingOwner = null;
        lock (_gate)
        {
            if (_openingEntries.TryGetValue(serial, out TaskCompletionSource<object?>? existingOpening))
                openingWaiter = existingOpening;
            else
            {
                openingOwner = new TaskCompletionSource<object?>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _openingEntries.Add(serial, openingOwner);
            }
        }

        if (openingWaiter is not null)
        {
            await openingWaiter.Task.WaitAsync(cancellationToken).ConfigureAwait(true);
            ActivateExistingWindow(serial);
            return;
        }

        Exception? openFailure = null;
        try
        {
            AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            ViewDeviceViewModel viewModel;
            ViewDeviceWindow window;
            try
            {
                viewModel = scope.ServiceProvider.GetRequiredService<ViewDeviceViewModel>();
                window = new(viewModel)
                {
                    Title = FormatWindowTitle(serial, displayName)
                };
            }
            catch
            {
                await scope.DisposeAsync().ConfigureAwait(true);
                throw;
            }

            ViewDeviceWindowEntry entry = new(serial, scope, viewModel, window);
            window.Closing += OnWindowClosing;
            window.Closed += OnWindowClosed;
            bool published = false;
            try
            {
                entry.PresentationLease = await _presentationCoordinator
                    .AcquireDedicatedViewAsync(serial, cancellationToken)
                    .ConfigureAwait(true);

                if (_beforeEntryPublication is not null)
                    await _beforeEntryPublication().ConfigureAwait(true);

                lock (_gate)
                {
                    ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
                    _entries.Add(serial, entry);
                    published = true;
                }

                window.Show();
                await viewModel.InitializeAsync(serial, displayName, cancellationToken).ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                openFailure = exception;
                if (published)
                {
                    if (window.IsVisible)
                        window.Close();

                    if (window.IsVisible)
                        await entry.Closed.Task.ConfigureAwait(true);
                    else
                    {
                        window.Closing -= OnWindowClosing;
                        window.Closed -= OnWindowClosed;
                        await DisposeEntryAsync(entry).ConfigureAwait(true);
                    }
                }
                else
                {
                    window.Closing -= OnWindowClosing;
                    window.Closed -= OnWindowClosed;
                    await DisposeUnpublishedEntryAsync(entry).ConfigureAwait(true);
                }

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
                    {
                        // The first Close call is still inside WPF's Closing
                        // event. Defer the uncancelled close until that event has
                        // returned; calling Close reentrantly makes WPF throw
                        // VerifyNotClosing.
                        _ = window.Dispatcher.BeginInvoke(
                            DispatcherPriority.Normal,
                            new Action(() =>
                            {
                                if (!window.Dispatcher.HasShutdownStarted && window.IsVisible)
                                    window.Close();
                            }));
                    }
                }
            }

            void OnWindowClosed(object? sender, EventArgs eventArgs)
            {
                window.Closing -= OnWindowClosing;
                window.Closed -= OnWindowClosed;
                _ = DisposeEntryAsync(entry);
            }
        }
        catch (Exception exception)
        {
            openFailure ??= exception;
            throw;
        }
        finally
        {
            lock (_gate)
            {
                if (openingOwner is not null &&
                    _openingEntries.TryGetValue(serial, out TaskCompletionSource<object?>? current) &&
                    ReferenceEquals(current, openingOwner))
                {
                    _openingEntries.Remove(serial);
                    if (openFailure is null)
                        openingOwner.TrySetResult(null);
                    else if (openFailure is OperationCanceledException canceled)
                    {
                        CancellationToken operationCancellationToken = canceled.CancellationToken;
                        openingOwner.TrySetCanceled(
                            operationCancellationToken.CanBeCanceled
                                ? operationCancellationToken
                                : new CancellationToken(canceled: true));
                    }
                    else
                        openingOwner.TrySetException(openFailure);
                }
            }
        }
    }

    private void ActivateExistingWindow(string serial)
    {
        ViewDeviceWindowEntry? entry;
        lock (_gate)
            _entries.TryGetValue(serial, out entry);

        if (entry is null ||
            Volatile.Read(ref entry.ClosePreparationStarted) != 0 ||
            !entry.Window.IsVisible)
        {
            return;
        }

        if (entry.Window.WindowState == WindowState.Minimized)
            entry.Window.WindowState = WindowState.Normal;
        entry.Window.Activate();
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
            await ReleasePresentationLeaseAsync(entry).ConfigureAwait(true);
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

    private async Task DisposeUnpublishedEntryAsync(ViewDeviceWindowEntry entry)
    {
        try
        {
            if (entry.Window.IsVisible)
                entry.Window.Close();
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Failed to close an unpublished View Device window for {Serial}.", entry.Serial);
        }

        try
        {
            await entry.ViewModel.DisposeAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Failed to dispose an unpublished View Device ViewModel for {Serial}.",
                entry.Serial);
        }
        finally
        {
            await ReleasePresentationLeaseAsync(entry).ConfigureAwait(true);
            try
            {
                await entry.Scope.DisposeAsync().ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Failed to dispose an unpublished View Device scope for {Serial}.",
                    entry.Serial);
            }

            entry.Closed.TrySetResult(null);
        }
    }

    private async Task ReleasePresentationLeaseAsync(ViewDeviceWindowEntry entry)
    {
        IAsyncDisposable? lease = entry.PresentationLease;
        entry.PresentationLease = null;
        if (lease is null)
            return;

        try
        {
            await lease.DisposeAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Failed to release View Device presentation lease for {Serial}.",
                entry.Serial);
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
        public IAsyncDisposable? PresentationLease { get; set; }
        public TaskCompletionSource<object?> Closed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ClosePreparationStarted;
        public int CloseReady;
        public int DisposeStarted;
    }
}
