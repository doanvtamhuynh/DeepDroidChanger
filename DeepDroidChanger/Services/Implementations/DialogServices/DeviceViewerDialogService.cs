using DeepDroidChanger.Models;
using DeepDroidChanger.ViewModels;
using DeepDroidChanger.Views;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services
{
    public sealed class DeviceViewerDialogService : IDeviceViewerDialogService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IDeviceViewerCoordinatorService _streamCoordinator;
        private readonly ILogger<DeviceViewerDialogService> _logger;

        public DeviceViewerDialogService(
            IServiceScopeFactory scopeFactory,
            IDeviceViewerCoordinatorService streamCoordinator,
            ILogger<DeviceViewerDialogService> logger)
        {
            _scopeFactory = scopeFactory;
            _streamCoordinator = streamCoordinator;
            _logger = logger;
        }

        public Task ShowDeviceViewerAsync(string serial, string name, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var scope = _scopeFactory.CreateScope();
            var scopeTransferred = false;

            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var viewModel = scope.ServiceProvider.GetRequiredService<DeviceViewerViewModel>();
                    viewModel.Initialize(serial, name);

                    var window = scope.ServiceProvider.GetRequiredService<DeviceViewerDialog>();
                    window.DataContext = viewModel;
                    if (Application.Current.MainWindow is { IsVisible: true } owner)
                        window.Owner = owner;
                    AttachStreamLifecycle(window, viewModel, serial, scope);
                    window.Show();
                });
                scopeTransferred = true;
            }
            finally
            {
                if (!scopeTransferred)
                    scope.Dispose();
            }

            return Task.CompletedTask;
        }

        private void AttachStreamLifecycle(
            DeviceViewerDialog window,
            DeviceViewerViewModel viewModel,
            string serial,
            IServiceScope ownedScope)
        {
            var sessionGate = new object();
            IDeviceViewerStreamSession? session = null;
            var lifetimeCts = new CancellationTokenSource();
            var startLock = new SemaphoreSlim(1, 1);
            Task? monitorTask = null;
            Task? deviceIpTask = null;
            var isClosed = false;
            var syncPending = false;
            long sessionGeneration = 0;

            long BeginNewSession()
            {
                return Interlocked.Increment(ref sessionGeneration);
            }

            bool IsCurrentSession(long generation)
            {
                return generation == Volatile.Read(ref sessionGeneration);
            }

            IDeviceViewerStreamSession? GetSession()
            {
                lock (sessionGate)
                {
                    return session;
                }
            }

            void SessionExited(object? sender, EventArgs e)
            {
                if (sender is IDeviceViewerStreamSession exitedSession)
                    _ = ObserveBackgroundTaskAsync(
                        HandleSessionExitedAsync(exitedSession),
                        "session-exit cleanup",
                        serial);
            }

            void SetSession(IDeviceViewerStreamSession? value)
            {
                IDeviceViewerStreamSession? previous;
                lock (sessionGate)
                {
                    if (ReferenceEquals(session, value))
                        return;

                    previous = session;
                    session = value;
                }

                if (previous != null)
                    previous.Exited -= SessionExited;

                if (value != null)
                    value.Exited += SessionExited;
            }

            void ClearSessionIfCurrent(IDeviceViewerStreamSession value)
            {
                var shouldUnsubscribe = false;
                lock (sessionGate)
                {
                    if (ReferenceEquals(session, value))
                    {
                        session = null;
                        shouldUnsubscribe = true;
                    }
                }

                if (shouldUnsubscribe)
                    value.Exited -= SessionExited;
            }

            void SyncNativeWindow()
            {
                if (isClosed)
                    return;

                var currentSession = GetSession();
                if (currentSession == null)
                    return;

                if (viewModel.IsStreaming && window.TryGetViewerBounds(out var bounds))
                {
                    currentSession.UpdateBounds(bounds);
                    currentSession.SetVisible(true);
                    return;
                }

                currentSession.SetVisible(false);
            }

            void RequestNativeWindowSync()
            {
                if (isClosed || syncPending)
                    return;

                syncPending = true;
                _ = ObserveBackgroundTaskAsync(
                    window.Dispatcher.InvokeAsync(() =>
                    {
                        syncPending = false;
                        SyncNativeWindow();
                    }, DispatcherPriority.Render).Task,
                    "native-window synchronization",
                    serial);
            }

            async Task RefreshDeviceIpOnDispatcherAsync(bool showCheckingState)
            {
                try
                {
                    await window.Dispatcher
                        .InvokeAsync(() => viewModel.RefreshDeviceIpAsync(lifetimeCts.Token, showCheckingState))
                        .Task
                        .Unwrap()
                        .ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogDebug("Device viewer IP refresh was canceled for {Serial}.", serial);
                }
                catch (Exception exception)
                {
                    _logger.LogDebug(exception, "Device viewer IP refresh failed for {Serial}.", serial);
                }
            }

            void RequestDeviceIpRefresh(bool showCheckingState)
            {
                if (isClosed || lifetimeCts.IsCancellationRequested)
                    return;

                if (deviceIpTask is { IsCompleted: false })
                    return;

                deviceIpTask = RefreshDeviceIpOnDispatcherAsync(showCheckingState);
            }

            Task MarkWaitingForDeviceAsync()
            {
                return window.Dispatcher.InvokeAsync(viewModel.MarkWaitingForDevice).Task;
            }

            Task MarkDeviceIpUnavailableAsync()
            {
                return window.Dispatcher.InvokeAsync(viewModel.MarkDeviceIpUnavailable).Task;
            }

            Task MarkStartingAsync()
            {
                return window.Dispatcher.InvokeAsync(viewModel.MarkStarting).Task;
            }

            Task MarkStreamingAsync()
            {
                return window.Dispatcher.InvokeAsync(viewModel.MarkStreaming).Task;
            }

            Task MarkStreamErrorAsync()
            {
                return window.Dispatcher.InvokeAsync(viewModel.MarkStreamError).Task;
            }

            Task<DeviceViewerStreamBounds> PrepareStreamBoundsAsync(double aspectRatio)
            {
                return window.Dispatcher.InvokeAsync(() =>
                {
                    viewModel.DeviceAspectRatio = aspectRatio;
                    window.UpdateLayout();
                    window.RefreshStreamLayout();
                    window.UpdateLayout();

                    if (!window.TryGetViewerBounds(out var bounds))
                        throw new InvalidOperationException("Device viewer placeholder bounds are not available.");

                    return bounds;
                }).Task;
            }

            Task<IntPtr> GetOwnerHandleAsync()
            {
                return window.Dispatcher.InvokeAsync(() => window.NativeOwnerHandle).Task;
            }

            async Task StopSessionAsync(IDeviceViewerStreamSession stoppingSession, string reason)
            {
                try
                {
                    stoppingSession.SetVisible(false);
                    await stoppingSession.StopAsync(CancellationToken.None).ConfigureAwait(true);
                }
                catch (Exception exception)
                {
                    _logger.LogDebug(exception, "Failed to stop device viewer stream session for {Serial}. Reason: {Reason}", serial, reason);
                }
                finally
                {
                    ClearSessionIfCurrent(stoppingSession);
                    stoppingSession.Dispose();
                }
            }

            async Task HandleSessionExitedAsync(IDeviceViewerStreamSession exitedSession)
            {
                if (isClosed || !ReferenceEquals(GetSession(), exitedSession))
                    return;

                BeginNewSession();
                ClearSessionIfCurrent(exitedSession);

                await window.Dispatcher.InvokeAsync(() =>
                {
                    exitedSession.SetVisible(false);
                    viewModel.MarkWaitingForDevice();
                });

                await StopSessionAsync(exitedSession, "process exit").ConfigureAwait(true);
            }

            async Task StartStreamAsync()
            {
                try
                {
                    if (isClosed)
                        return;

                    var generation = BeginNewSession();
                    await _streamCoordinator.EnsureStreamAsync(
                            new DeviceViewerStartContext(
                                serial,
                                startLock,
                                lifetimeCts.Token,
                                GetSession,
                                SetSession,
                                StopSessionAsync,
                                RequestNativeWindowSync,
                                generation,
                                IsCurrentSession,
                                MarkWaitingForDeviceAsync,
                                MarkStartingAsync,
                                PrepareStreamBoundsAsync,
                                GetOwnerHandleAsync,
                                MarkStreamingAsync,
                                MarkStreamErrorAsync))
                        .ConfigureAwait(true);

                    if (monitorTask != null)
                        return;

                    monitorTask = _streamCoordinator.MonitorStreamAsync(
                        new DeviceViewerMonitorContext(
                            serial,
                            startLock,
                            lifetimeCts.Token,
                            GetSession,
                            SetSession,
                            StopSessionAsync,
                            BeginNewSession,
                            IsCurrentSession,
                            RequestNativeWindowSync,
                            () => RequestDeviceIpRefresh(showCheckingState: false),
                            MarkWaitingForDeviceAsync,
                            MarkDeviceIpUnavailableAsync,
                            () => viewModel.IsStreaming,
                            PrepareStreamBoundsAsync,
                            GetOwnerHandleAsync,
                            MarkStartingAsync,
                            MarkStreamingAsync,
                            MarkStreamErrorAsync));
                }
                catch (OperationCanceledException)
                {
                    _logger.LogDebug("Device viewer start was canceled for {Serial}.", serial);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Device viewer start failed for {Serial}.", serial);
                    if (!isClosed)
                    {
                        GetSession()?.SetVisible(false);
                        viewModel.MarkStreamError();
                    }
                }
            }

            void StartStreamHandler(object? sender, EventArgs e)
            {
                window.LastStartTask = StartStreamAsync();
            }

            void SyncStreamHandler(object? sender, EventArgs e)
            {
                RequestNativeWindowSync();
            }

            async Task ActivateStreamAfterCommandAsync(ButtonBase button)
            {
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);

                if (button.Command is IAsyncRelayCommand asyncCommand && asyncCommand.ExecutionTask is { } executionTask)
                {
                    try
                    {
                        await executionTask.ConfigureAwait(true);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (Exception exception)
                    {
                        _logger.LogDebug(exception, "Device viewer command finished with an error before focus restore for {Serial}.", serial);
                    }
                }

                await window.Dispatcher.InvokeAsync(() =>
                {
                    if (!viewModel.IsStreaming || !window.IsViewerVisibleForNativeWindow)
                        return;

                    SyncNativeWindow();
                    GetSession()?.Activate();
                }, DispatcherPriority.ContextIdle);
            }

            void CommandButtonClickHandler(object sender, RoutedEventArgs e)
            {
                var button = FindButtonBase(e.OriginalSource);
                if (button is not { IsEnabled: true, Command: not null })
                    return;

                if (!ShouldRestoreStreamFocus(button, viewModel))
                    return;

                _ = ObserveBackgroundTaskAsync(
                    ActivateStreamAfterCommandAsync(button),
                    "stream focus restoration",
                    serial);
            }

            var commandButtonClickRoutedHandler = new RoutedEventHandler(CommandButtonClickHandler);

            void HideStreamOnClosing(object? sender, CancelEventArgs e)
            {
                GetSession()?.SetVisible(false);
            }

            void CloseStream(object? sender, EventArgs e)
            {
                if (isClosed)
                    return;

                window.ViewerBoundsReady -= StartStreamHandler;
                window.ViewerBoundsChanged -= SyncStreamHandler;
                window.ViewerVisibilityChanged -= SyncStreamHandler;
                window.RemoveHandler(ButtonBase.ClickEvent, commandButtonClickRoutedHandler);
                window.Closing -= HideStreamOnClosing;
                window.Closed -= CloseStream;

                isClosed = true;
                BeginNewSession();
                lifetimeCts.Cancel();
                GetSession()?.SetVisible(false);

                _ = ObserveBackgroundTaskAsync(
                    CleanupStreamAsync(),
                    "window-close cleanup",
                    serial);
            }

            async Task CleanupStreamAsync()
            {
                try
                {
                    try
                    {
                        await window.LastStartTask.ConfigureAwait(true);
                    }
                    catch (Exception exception)
                    {
                        _logger.LogDebug(exception, "Device viewer start task cleanup for {Serial}.", serial);
                    }

                    if (deviceIpTask != null)
                    {
                        try
                        {
                            await deviceIpTask.ConfigureAwait(true);
                        }
                        catch (Exception exception)
                        {
                            _logger.LogDebug(exception, "Device viewer IP refresh cleanup failed for {Serial}.", serial);
                        }
                    }

                    if (monitorTask != null)
                    {
                        try
                        {
                            await monitorTask.ConfigureAwait(true);
                        }
                        catch (Exception exception)
                        {
                            _logger.LogDebug(exception, "Device viewer monitor cleanup failed for {Serial}.", serial);
                        }
                    }

                    var closingSession = GetSession();
                    if (closingSession != null)
                        await StopSessionAsync(closingSession, "window closed").ConfigureAwait(true);
                }
                finally
                {
                    lifetimeCts.Dispose();
                    startLock.Dispose();
                    ownedScope.Dispose();
                }
            }

            window.ViewerBoundsReady += StartStreamHandler;
            window.ViewerBoundsChanged += SyncStreamHandler;
            window.ViewerVisibilityChanged += SyncStreamHandler;
            window.AddHandler(ButtonBase.ClickEvent, commandButtonClickRoutedHandler, true);
            window.Closing += HideStreamOnClosing;
            window.Closed += CloseStream;
            RequestDeviceIpRefresh(showCheckingState: true);
        }

        private async Task ObserveBackgroundTaskAsync(Task task, string operation, string serial)
        {
            try
            {
                await task.ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Device viewer {Operation} was canceled for {Serial}.", operation, serial);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Device viewer {Operation} failed for {Serial}.", operation, serial);
            }
        }

        private static ButtonBase? FindButtonBase(object source)
        {
            var current = source as DependencyObject;
            while (current != null)
            {
                if (current is ButtonBase button)
                    return button;

                current = GetParent(current);
            }

            return null;
        }

        private static bool ShouldRestoreStreamFocus(ButtonBase button, DeviceViewerViewModel viewModel)
        {
            var command = button.Command;
            return ReferenceEquals(command, viewModel.BackCommand) ||
                ReferenceEquals(command, viewModel.HomeCommand) ||
                ReferenceEquals(command, viewModel.RecentCommand) ||
                ReferenceEquals(command, viewModel.EnterCommand) ||
                ReferenceEquals(command, viewModel.VolumeUpCommand) ||
                ReferenceEquals(command, viewModel.VolumeDownCommand) ||
                ReferenceEquals(command, viewModel.ScreenshotCommand) ||
                ReferenceEquals(command, viewModel.PowerCommand);
        }

        private static DependencyObject? GetParent(DependencyObject current)
        {
            if (current is FrameworkElement { Parent: not null } frameworkElement)
                return frameworkElement.Parent;

            if (current is FrameworkContentElement { Parent: not null } frameworkContentElement)
                return frameworkContentElement.Parent;

            try
            {
                return VisualTreeHelper.GetParent(current);
            }
            catch (InvalidOperationException)
            {
                return LogicalTreeHelper.GetParent(current);
            }
        }

    }
}
