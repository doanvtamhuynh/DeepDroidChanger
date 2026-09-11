using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using DeepDroidChanger.ViewDevices.Contracts;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.ViewModels;

public sealed class ViewMultipleDevicesViewModel : ObservableObject, IAsyncDisposable
{
    public const int PageSize = 8;
    private const int BaseTilesPerRow = 4;

    private enum TrackerTransitionKind
    {
        DeviceOffline,
        DeviceOnline,
        TrackerReconnecting,
        TrackerConnected
    }

    private sealed class PendingTrackerWork
    {
        public bool RequiresCatalogRefresh { get; set; }

        public bool TrackerReconnecting { get; set; }

        public HashSet<string> DeviceSerials { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool HasWork => RequiresCatalogRefresh ||
                                TrackerReconnecting ||
                                DeviceSerials.Count > 0;

        public void Clear()
        {
            RequiresCatalogRefresh = false;
            TrackerReconnecting = false;
            DeviceSerials.Clear();
        }
    }

    private static readonly int[] ZoomLevels =
    [
        50, 75, 100, 125, 150, 175, 200, 225, 250, 275, 300
    ];
    private static readonly int DefaultZoomIndex = Array.IndexOf(ZoomLevels, 100);
    private static readonly int FitZoomIndex = Array.IndexOf(ZoomLevels, 100);

    private readonly IDeviceStoreService _deviceStoreService;
    private readonly IAdbDeviceTrackerService _deviceTracker;
    private readonly IViewDeviceSessionFactory _sessionFactory;
    private readonly ILocalizationService _localization;
    private readonly IUiDispatcherService _uiDispatcher;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<ViewMultipleDevicesViewModel> _logger;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly SemaphoreSlim _transitionGate = new(1, 1);
    private readonly object _navigationSchedulingGate = new();
    private readonly object _trackerSchedulingGate = new();
    private readonly object _retrySchedulingGate = new();
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly Dictionary<
        ViewMultipleDeviceItemViewModel,
        CancellationTokenSource> _retryCancellations = [];
    private readonly HashSet<Task> _retryTasks = [];

    private CancellationTokenSource? _activationCancellation;
    private CancellationTokenSource? _navigationCancellation;
    private Task _navigationTask = Task.CompletedTask;
    private CancellationTokenSource? _trackerCancellation;
    private Task _trackerTask = Task.CompletedTask;
    private PendingTrackerWork _pendingTrackerWork = new();
    private int _generation;
    private int _currentPage = 1;
    private int _requestedPage = 1;
    private int _zoomIndex;
    private double _viewportWidth;
    private double _viewportHeight;
    private double _tileHorizontalFootprint = 12;
    private double _tileVerticalFootprint = 12;
    private double _fitTileWidth = 220;
    private bool _fitPending = true;
    private bool _isActive;
    private int _disposed;
    private bool _trackerEventsSubscribed;
    private int _initializationInProgress;
    private int _trackerRefreshInProgress;
    private int _trackerDirtyDuringRefresh;

    public ViewMultipleDevicesViewModel(
        IDeviceStoreService deviceStoreService,
        IAdbDeviceTrackerService deviceTracker,
        IViewDeviceSessionFactory sessionFactory,
        ILocalizationService localization,
        IUiDispatcherService uiDispatcher,
        ILoggerFactory loggerFactory,
        ILogger<ViewMultipleDevicesViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(deviceStoreService);
        ArgumentNullException.ThrowIfNull(deviceTracker);
        ArgumentNullException.ThrowIfNull(sessionFactory);
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentNullException.ThrowIfNull(uiDispatcher);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _deviceStoreService = deviceStoreService;
        _deviceTracker = deviceTracker;
        _sessionFactory = sessionFactory;
        _localization = localization;
        _uiDispatcher = uiDispatcher;
        _loggerFactory = loggerFactory;
        _logger = logger;
        _zoomIndex = DefaultZoomIndex;

        ZoomOutCommand = new RelayCommand(ZoomOut, CanZoomOut);
        ZoomInCommand = new RelayCommand(ZoomIn, CanZoomIn);
        FitCommand = new RelayCommand(FitToViewport, CanFit);
        PreviousPageCommand = new RelayCommand(RequestPreviousPage, CanNavigatePrevious);
        NextPageCommand = new RelayCommand(RequestNextPage, CanNavigateNext);
    }

    public ObservableCollection<ViewMultipleDeviceItemViewModel> OnlineDevices { get; } = [];

    public ObservableCollection<ViewMultipleDeviceItemViewModel> VisibleDevices { get; } = [];

    public int CatalogDeviceCount => OnlineDevices.Count;

    public int OnlineDeviceCount => _deviceTracker.Health == AdbDeviceTrackerHealth.Connected
        ? OnlineDevices.Count(item => _deviceTracker.GetDevice(item.Serial)?.Status == AdbDeviceStatus.Online)
        : 0;

    public bool HasOnlineDevices => CatalogDeviceCount > 0;

    public int CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (!SetProperty(ref _currentPage, value))
                return;

            OnPropertyChanged(nameof(ShowingStart));
            OnPropertyChanged(nameof(ShowingEnd));
        }
    }

    public int TotalPages => CalculateTotalPages(CatalogDeviceCount);

    public int ShowingStart => CatalogDeviceCount == 0
        ? 0
        : ((CurrentPage - 1) * PageSize) + 1;

    public int ShowingEnd => CatalogDeviceCount == 0
        ? 0
        : Math.Min(CurrentPage * PageSize, CatalogDeviceCount);

    public int ZoomPercent => ZoomLevels[_zoomIndex];

    public IRelayCommand ZoomOutCommand { get; }

    public IRelayCommand ZoomInCommand { get; }

    public IRelayCommand FitCommand { get; }

    public IRelayCommand PreviousPageCommand { get; }

    public IRelayCommand NextPageCommand { get; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<ViewMultipleDeviceItemViewModel>? catalog = null;
        bool catalogPublished = false;
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (Volatile.Read(ref _isActive))
                return;

            Volatile.Write(ref _isActive, true);
            await _uiDispatcher
                .InvokeAsync(ResetPagingForActivation, cancellationToken)
                .ConfigureAwait(false);

            CancellationTokenSource activation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetimeCancellation.Token);
            _activationCancellation = activation;

            // Mark initialization before subscribing so every tracker event raised
            // during StartAsync or catalog construction is coalesced for later.
            Volatile.Write(ref _initializationInProgress, 1);
            SubscribeToTracker();

            try
            {
                await _deviceTracker.StartAsync(activation.Token).ConfigureAwait(false);
                IReadOnlyList<ViewMultipleDeviceItemViewModel> initialCatalog =
                    await BuildOnlineCatalogAsync(activation.Token).ConfigureAwait(false) ?? [];
                catalog = initialCatalog;

                int generation = Interlocked.Increment(ref _generation);
                await _transitionGate.WaitAsync(activation.Token).ConfigureAwait(false);
                try
                {
                    bool applied = await ReplaceCatalogAndPageAsync(
                            initialCatalog,
                            page: 1,
                            navigationGeneration: generation,
                            cancellationToken: activation.Token)
                        .ConfigureAwait(false);
                    if (!applied)
                    {
                        activation.Token.ThrowIfCancellationRequested();
                        throw new InvalidOperationException("The initial multi-view page was superseded.");
                    }

                    catalogPublished = true;
                    await _uiDispatcher
                        .InvokeAsync(ApplyInitialFitCore, activation.Token)
                        .ConfigureAwait(false);
                    await StartVisibleSessionsAsync(activation.Token).ConfigureAwait(false);
                }
                finally
                {
                    _transitionGate.Release();
                }
            }
            catch
            {
                await DeactivateCoreAsync().ConfigureAwait(false);
                throw;
            }
            finally
            {
                Volatile.Write(ref _initializationInProgress, 0);
                if (Volatile.Read(ref _isActive))
                    EnsureTrackerWorkerIfPending();
            }
        }
        finally
        {
            if (catalog is not null && !catalogPublished)
                await DisposeItemsAsync(catalog).ConfigureAwait(false);

            _lifecycleGate.Release();
        }
    }

    public async Task DeactivateAsync()
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        CancelCancellation(Volatile.Read(ref _activationCancellation));
        await _lifecycleGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            await DeactivateCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public void UpdateViewport(
        double viewportWidth,
        double viewportHeight,
        double horizontalTileFootprint,
        double verticalTileFootprint)
    {
        if (!double.IsFinite(viewportWidth) ||
            viewportWidth <= 0 ||
            !double.IsFinite(viewportHeight) ||
            viewportHeight < 0 ||
            !double.IsFinite(horizontalTileFootprint) ||
            !double.IsFinite(verticalTileFootprint) ||
            horizontalTileFootprint < 0 ||
            verticalTileFootprint < 0)
        {
            return;
        }

        bool viewportChanged = _viewportWidth != viewportWidth ||
                               _viewportHeight != viewportHeight ||
                               _tileHorizontalFootprint != horizontalTileFootprint ||
                               _tileVerticalFootprint != verticalTileFootprint;
        _viewportWidth = viewportWidth;
        _viewportHeight = viewportHeight;
        _tileHorizontalFootprint = horizontalTileFootprint;
        _tileVerticalFootprint = verticalTileFootprint;

        if (!viewportChanged && !_fitPending)
            return;

        if (!TryCalculateFitTileSize())
            return;

        _fitPending = false;
        // A resize recalculates the 100% baseline but deliberately keeps the
        // selected zoom index.
        ApplyTileSizesCore();
    }

    public void FitToViewport()
    {
        if (Volatile.Read(ref _disposed) != 0 || !Volatile.Read(ref _isActive))
            return;

        FitToViewportCore();
        NotifyZoomCommands();
    }

    internal async Task WaitForPendingOperationsAsync()
    {
        for (int pass = 0; pass < 100; pass++)
        {
            Task[] operations;
            lock (_navigationSchedulingGate)
            lock (_trackerSchedulingGate)
            lock (_retrySchedulingGate)
            {
                operations = new[] { _navigationTask, _trackerTask }
                    .Concat(_retryTasks)
                    .Where(task => !task.IsCompleted)
                    .Distinct()
                    .ToArray();
            }

            if (operations.Length == 0)
                return;

            await Task.WhenAll(operations).ConfigureAwait(false);
        }

        throw new TimeoutException("View Multiple Devices operations did not become idle.");
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        CancelCancellation(Volatile.Read(ref _activationCancellation));
        await _lifecycleGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            await DeactivateCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            CancelCancellation(_lifetimeCancellation);
            _lifetimeCancellation.Dispose();
            _lifecycleGate.Release();
            _lifecycleGate.Dispose();
            _transitionGate.Dispose();
        }
    }

    private async Task DeactivateCoreAsync()
    {
        Volatile.Write(ref _isActive, false);
        Volatile.Write(ref _initializationInProgress, 0);
        UnsubscribeFromTracker();

        CancellationTokenSource? activation = Interlocked.Exchange(
            ref _activationCancellation,
            null);
        CancelCancellation(activation);
        await CancelItemRetriesAsync().ConfigureAwait(false);
        await CancelOperationsAndWaitAsync().ConfigureAwait(false);

        ViewMultipleDeviceItemViewModel[] items = await SnapshotOnlineItemsAsync().ConfigureAwait(false);
        try
        {
            // Remove visible tiles before stopping their sessions. Their
            // Unloaded handlers then detach native children while the host
            // controls are still available.
            await _uiDispatcher
                .InvokeAsync(() =>
                {
                    VisibleDevices.Clear();
                    OnlineDevices.Clear();
                    _requestedPage = 1;
                    CurrentPage = 1;
                    _fitPending = true;
                    SetZoomIndexCore(DefaultZoomIndex);
                    NotifyCollectionSummary();
                    NotifyZoomCommands();
                })
                .ConfigureAwait(false);
        }
        finally
        {
            await StopItemsAsync(items).ConfigureAwait(false);
            await DisposeItemsAsync(items).ConfigureAwait(false);
        }

        activation?.Dispose();
    }

    private async Task<IReadOnlyList<ViewMultipleDeviceItemViewModel>?> BuildOnlineCatalogAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<StoredDeviceConfig> savedDevices =
            await _deviceStoreService.LoadAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<AdbDevice> detectedDevices = _deviceTracker.CurrentSnapshot;
        if (_deviceTracker.Health != AdbDeviceTrackerHealth.Connected)
            return null;

        HashSet<string> onlineSerials = detectedDevices
            .Where(device => device.Status == AdbDeviceStatus.Online)
            .Select(device => device.Serial)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return savedDevices
            .Where(device => onlineSerials.Contains(device.Serial))
            .Select(device => new ViewMultipleDeviceItemViewModel(
                device.Serial,
                device.Name,
                _sessionFactory,
                _deviceTracker,
                _localization,
                _uiDispatcher,
                _loggerFactory.CreateLogger<ViewMultipleDeviceItemViewModel>(),
                RequestItemRetry))
            .ToList();
    }

    private async Task<bool> ReplaceCatalogAndPageAsync(
        IReadOnlyList<ViewMultipleDeviceItemViewModel> catalog,
        int page,
        int? navigationGeneration,
        CancellationToken cancellationToken,
        bool requireTrackerConnected = false)
    {
        if (!IsCurrentOperation(navigationGeneration, cancellationToken) ||
            (requireTrackerConnected &&
             _deviceTracker.Health != AdbDeviceTrackerHealth.Connected))
        {
            return false;
        }

        // Candidate construction happens before this method. Keep the current
        // catalog and its sessions alive until both collections are swapped in
        // one dispatcher action; this is the commit point.
        ViewMultipleDeviceItemViewModel[] oldItems = await SnapshotOnlineItemsAsync()
            .ConfigureAwait(false);
        if (!IsCurrentOperation(navigationGeneration, cancellationToken) ||
            (requireTrackerConnected &&
             _deviceTracker.Health != AdbDeviceTrackerHealth.Connected))
        {
            return false;
        }

        bool applied = await ApplyCatalogAndPageAsync(
                catalog,
                page,
                navigationGeneration,
                cancellationToken,
                requireTrackerConnected)
            .ConfigureAwait(false);
        if (!applied)
            return false;

        // Ownership changed at commit. Cleanup is deliberately unconditional:
        // cancellation after commit must never leave the old catalog alive or
        // dispose a candidate that is now published.
        await StopItemsAsync(oldItems).ConfigureAwait(false);
        await DisposeItemsAsync(oldItems).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> ApplyCatalogAndPageAsync(
        IReadOnlyList<ViewMultipleDeviceItemViewModel> catalog,
        int page,
        int? navigationGeneration,
        CancellationToken cancellationToken,
        bool requireTrackerConnected)
    {
        bool applied = false;
        await _uiDispatcher
            .InvokeAsync(() =>
            {
                if (!IsCurrentOperation(navigationGeneration, cancellationToken) ||
                    (requireTrackerConnected &&
                     _deviceTracker.Health != AdbDeviceTrackerHealth.Connected))
                {
                    return;
                }

                int targetPage = Math.Clamp(page, 1, CalculateTotalPages(catalog.Count));
                // Remove visible tiles first so their Unloaded handlers can
                // detach native children before obsolete items are disposed.
                VisibleDevices.Clear();
                OnlineDevices.Clear();
                foreach (ViewMultipleDeviceItemViewModel item in catalog)
                {
                    item.SetTileSize(CalculateScaledTileWidth());
                    OnlineDevices.Add(item);
                }

                foreach (ViewMultipleDeviceItemViewModel item in catalog
                             .Skip((targetPage - 1) * PageSize)
                             .Take(PageSize))
                {
                    VisibleDevices.Add(item);
                }

                _requestedPage = targetPage;
                CurrentPage = targetPage;
                NotifyCollectionSummary();
                applied = true;
            }, cancellationToken)
            .ConfigureAwait(false);

        return applied;
    }

    private async Task StartVisibleSessionsAsync(CancellationToken cancellationToken)
    {
        ViewMultipleDeviceItemViewModel[] visibleItems = await SnapshotVisibleItemsAsync()
            .ConfigureAwait(false);
        try
        {
            await Task.WhenAll(visibleItems.Select(item => item.StartAsync(cancellationToken)))
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await StopItemsAsync(visibleItems).ConfigureAwait(false);
            throw;
        }
        catch
        {
            await StopItemsAsync(visibleItems).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<bool> RefreshCatalogAndStartPageAsync(
        int requestedPage,
        int? navigationGeneration,
        CancellationToken cancellationToken)
    {
        if (!IsCurrentOperation(navigationGeneration, cancellationToken) ||
            _deviceTracker.Health != AdbDeviceTrackerHealth.Connected)
        {
            return false;
        }

        IReadOnlyList<ViewMultipleDeviceItemViewModel>? builtCatalog =
            await BuildOnlineCatalogAsync(cancellationToken).ConfigureAwait(false);
        if (builtCatalog is null)
            return false;

        bool catalogPublished = false;
        try
        {
            if (!IsCurrentOperation(navigationGeneration, cancellationToken))
                return false;

            int targetPage = Math.Clamp(
                requestedPage,
                1,
                CalculateTotalPages(builtCatalog.Count));
            bool applied = await ReplaceCatalogAndPageAsync(
                    builtCatalog,
                    targetPage,
                    navigationGeneration,
                    cancellationToken,
                    requireTrackerConnected: true)
                .ConfigureAwait(false);
            if (!applied)
                return false;

            catalogPublished = true;
            try
            {
                await StartVisibleSessionsAsync(cancellationToken).ConfigureAwait(false);
                if (navigationGeneration.HasValue &&
                    !IsCurrentOperation(navigationGeneration, cancellationToken))
                {
                    await RemovePublishedCatalogAsync(builtCatalog).ConfigureAwait(false);
                    await StopItemsAsync(builtCatalog).ConfigureAwait(false);
                    catalogPublished = false;
                    return false;
                }

                return true;
            }
            catch
            {
                try
                {
                    await RemovePublishedCatalogAsync(builtCatalog).ConfigureAwait(false);
                }
                finally
                {
                    await StopItemsAsync(builtCatalog).ConfigureAwait(false);
                    catalogPublished = false;
                }

                throw;
            }
        }
        finally
        {
            if (!catalogPublished)
                await DisposeItemsAsync(builtCatalog).ConfigureAwait(false);
        }
    }

    private async Task RemovePublishedCatalogAsync(
        IReadOnlyList<ViewMultipleDeviceItemViewModel> catalog)
    {
        await _uiDispatcher
            .InvokeAsync(() =>
            {
                if (OnlineDevices.Count != catalog.Count ||
                    !OnlineDevices.SequenceEqual(catalog))
                {
                    return;
                }

                VisibleDevices.Clear();
                OnlineDevices.Clear();
                _requestedPage = 1;
                CurrentPage = 1;
                NotifyCollectionSummary();
            })
            .ConfigureAwait(false);
    }

    private async Task TransitionToPageAsync(
        int requestedPage,
        int generation,
        CancellationTokenSource navigationCancellation)
    {
        bool completed = false;
        bool deferredForTracker = false;
        try
        {
            await _transitionGate.WaitAsync(navigationCancellation.Token).ConfigureAwait(false);
            try
            {
                if (!IsLatestNavigation(generation, navigationCancellation))
                    return;

                if (_deviceTracker.Health != AdbDeviceTrackerHealth.Connected)
                {
                    deferredForTracker = true;
                    return;
                }

                Volatile.Write(ref _trackerRefreshInProgress, 1);
                try
                {
                    completed = await RefreshCatalogAndStartPageAsync(
                            requestedPage,
                            generation,
                            navigationCancellation.Token)
                        .ConfigureAwait(false);
                }
                finally
                {
                    Volatile.Write(ref _trackerRefreshInProgress, 0);
                    if (Interlocked.Exchange(ref _trackerDirtyDuringRefresh, 0) != 0)
                        QueueCatalogRefreshAfterRefresh();
                }
                if (!completed &&
                    _deviceTracker.Health != AdbDeviceTrackerHealth.Connected)
                {
                    deferredForTracker = true;
                }
            }
            finally
            {
                _transitionGate.Release();
            }
        }
        catch (OperationCanceledException) when (navigationCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "View Multiple Devices page transition failed.");
        }
        finally
        {
            if (!completed &&
                !deferredForTracker &&
                IsLatestNavigation(generation, navigationCancellation))
            {
                await ReconcileFailedNavigationAsync(generation).ConfigureAwait(false);
            }

            lock (_navigationSchedulingGate)
            {
                if (ReferenceEquals(_navigationCancellation, navigationCancellation))
                {
                    _navigationCancellation = null;
                    _navigationTask = Task.CompletedTask;
                }
            }

            navigationCancellation.Dispose();
            await NotifyPaginationCommandsAsync().ConfigureAwait(false);
        }
    }

    private void RequestPreviousPage()
    {
        RequestPage(-1);
    }

    private void RequestNextPage()
    {
        RequestPage(1);
    }

    private void RequestPage(int direction)
    {
        if (Volatile.Read(ref _disposed) != 0 ||
            !Volatile.Read(ref _isActive) ||
            _deviceTracker.Health != AdbDeviceTrackerHealth.Connected)
        {
            return;
        }

        CancellationTokenSource navigationCancellation;
        lock (_navigationSchedulingGate)
        {
            if (Volatile.Read(ref _disposed) != 0 ||
                !Volatile.Read(ref _isActive) ||
                _deviceTracker.Health != AdbDeviceTrackerHealth.Connected)
            {
                return;
            }

            int targetPage = Math.Clamp(
                _requestedPage + direction,
                1,
                CalculateTotalPages(CatalogDeviceCount));
            if (targetPage == _requestedPage)
                return;

            _requestedPage = targetPage;
            int generation = Interlocked.Increment(ref _generation);
            CancellationToken activationToken = _activationCancellation?.Token
                ?? _lifetimeCancellation.Token;
            navigationCancellation = CancellationTokenSource.CreateLinkedTokenSource(activationToken);
            CancellationTokenSource? previous = _navigationCancellation;
            _navigationCancellation = navigationCancellation;
            _navigationTask = Task.Run(() => TransitionToPageAsync(
                targetPage,
                generation,
                navigationCancellation));
            CancelCancellation(previous);
        }

        foreach (ViewMultipleDeviceItemViewModel item in OnlineDevices)
            item.CancelRetry();
        CancelPendingRetryTasks();
        NotifyPaginationCommands();
    }

    private void QueueTrackerTransition(
        TrackerTransitionKind kind,
        string? serial = null)
    {
        if (Volatile.Read(ref _disposed) != 0 || !Volatile.Read(ref _isActive))
            return;

        lock (_trackerSchedulingGate)
        {
            if (Volatile.Read(ref _disposed) != 0 || !Volatile.Read(ref _isActive))
                return;

            switch (kind)
            {
                case TrackerTransitionKind.DeviceOffline:
                case TrackerTransitionKind.DeviceOnline:
                    if (!string.IsNullOrWhiteSpace(serial))
                        _pendingTrackerWork.DeviceSerials.Add(serial);
                    break;

                case TrackerTransitionKind.TrackerReconnecting:
                    _pendingTrackerWork.TrackerReconnecting = true;
                    break;

                case TrackerTransitionKind.TrackerConnected:
                    _pendingTrackerWork.RequiresCatalogRefresh = true;
                    break;
            }

            if (Volatile.Read(ref _initializationInProgress) != 0)
                _pendingTrackerWork.RequiresCatalogRefresh = true;

            if (Volatile.Read(ref _trackerRefreshInProgress) != 0)
                Interlocked.Exchange(ref _trackerDirtyDuringRefresh, 1);

            EnsureTrackerWorkerLocked();
        }

        _ = NotifyOnlineDeviceCountAsync();
    }

    private void EnsureTrackerWorkerIfPending()
    {
        lock (_trackerSchedulingGate)
        {
            EnsureTrackerWorkerLocked();
        }
    }

    private void QueueCatalogRefreshAfterRefresh()
    {
        lock (_trackerSchedulingGate)
        {
            if (!Volatile.Read(ref _isActive) || Volatile.Read(ref _disposed) != 0)
                return;

            _pendingTrackerWork.RequiresCatalogRefresh = true;
            EnsureTrackerWorkerLocked();
        }
    }

    private void EnsureTrackerWorkerLocked()
    {
        if (!_pendingTrackerWork.HasWork || !_trackerTask.IsCompleted ||
            Volatile.Read(ref _disposed) != 0 ||
            !Volatile.Read(ref _isActive) ||
            Volatile.Read(ref _initializationInProgress) != 0)
        {
            return;
        }

        CancellationToken activationToken = _activationCancellation?.Token
            ?? _lifetimeCancellation.Token;
        CancellationTokenSource workerCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(activationToken);
        _trackerCancellation = workerCancellation;
        _trackerTask = Task.Run(() => ProcessTrackerWorkAsync(workerCancellation));
    }

    private PendingTrackerWork? TakePendingTrackerWork()
    {
        lock (_trackerSchedulingGate)
        {
            if (!_pendingTrackerWork.HasWork)
                return null;

            PendingTrackerWork work = _pendingTrackerWork;
            _pendingTrackerWork = new PendingTrackerWork();
            return work;
        }
    }

    private async Task ProcessTrackerWorkAsync(CancellationTokenSource workerCancellation)
    {
        try
        {
            while (true)
            {
                PendingTrackerWork? work = TakePendingTrackerWork();
                if (work is null)
                    break;

                workerCancellation.Token.ThrowIfCancellationRequested();
                await _transitionGate.WaitAsync(workerCancellation.Token).ConfigureAwait(false);
                try
                {
                    if (!Volatile.Read(ref _isActive))
                        break;

                    if (work.TrackerReconnecting)
                    {
                        await MarkVisibleDevicesDisconnectedAsync().ConfigureAwait(false);
                        await NotifyOnlineDeviceCountAsync().ConfigureAwait(false);
                    }

                    bool requiresRefresh = work.RequiresCatalogRefresh;
                    if (!requiresRefresh &&
                        _deviceTracker.Health == AdbDeviceTrackerHealth.Connected)
                    {
                        requiresRefresh = await HasUncataloguedOnlineDeviceAsync(
                                work.DeviceSerials,
                                workerCancellation.Token)
                            .ConfigureAwait(false);
                    }

                    if (requiresRefresh &&
                        _deviceTracker.Health == AdbDeviceTrackerHealth.Connected)
                    {
                        Volatile.Write(ref _trackerRefreshInProgress, 1);
                        try
                        {
                            await RefreshCatalogAndStartPageAsync(
                                    Volatile.Read(ref _requestedPage),
                                    navigationGeneration: null,
                                    cancellationToken: workerCancellation.Token)
                                .ConfigureAwait(false);
                        }
                        finally
                        {
                            Volatile.Write(ref _trackerRefreshInProgress, 0);
                        }

                        bool dirtyDuringRefresh = Interlocked.Exchange(
                                ref _trackerDirtyDuringRefresh,
                                0) != 0;
                        if (dirtyDuringRefresh)
                        {
                            lock (_trackerSchedulingGate)
                            {
                                if (Volatile.Read(ref _isActive))
                                    _pendingTrackerWork.RequiresCatalogRefresh = true;
                            }
                        }
                    }
                    else if (_deviceTracker.Health == AdbDeviceTrackerHealth.Connected)
                    {
                        foreach (string serial in work.DeviceSerials)
                        {
                            if (_deviceTracker.GetDevice(serial)?.Status == AdbDeviceStatus.Online)
                            {
                                await RecoverDeviceAsync(
                                        serial,
                                        workerCancellation.Token)
                                    .ConfigureAwait(false);
                            }
                            else
                            {
                                await MarkVisibleDeviceDisconnectedAsync(serial)
                                    .ConfigureAwait(false);
                            }
                        }

                        await NotifyOnlineDeviceCountAsync().ConfigureAwait(false);
                    }
                    else if (work.DeviceSerials.Count > 0)
                    {
                        await MarkVisibleDevicesDisconnectedAsync().ConfigureAwait(false);
                        await NotifyOnlineDeviceCountAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    _transitionGate.Release();
                }
            }
        }
        catch (OperationCanceledException) when (workerCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "View Multiple Devices tracker transition failed.");
        }
        finally
        {
            Volatile.Write(ref _trackerRefreshInProgress, 0);
            Interlocked.Exchange(ref _trackerDirtyDuringRefresh, 0);
            lock (_trackerSchedulingGate)
            {
                if (ReferenceEquals(_trackerCancellation, workerCancellation))
                {
                    _trackerCancellation = null;
                    _trackerTask = Task.CompletedTask;
                    if (Volatile.Read(ref _isActive) && _pendingTrackerWork.HasWork)
                        EnsureTrackerWorkerLocked();
                }
            }

            workerCancellation.Dispose();
            await NotifyPaginationCommandsAsync().ConfigureAwait(false);
        }
    }

    private async Task<bool> HasUncataloguedOnlineDeviceAsync(
        IEnumerable<string> serials,
        CancellationToken cancellationToken)
    {
        string[] requestedSerials = serials
            .Where(serial => !string.IsNullOrWhiteSpace(serial))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (requestedSerials.Length == 0)
            return false;

        ViewMultipleDeviceItemViewModel[] catalogItems = await SnapshotOnlineItemsAsync()
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        HashSet<string> catalogSerials = catalogItems
            .Select(item => item.Serial)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return requestedSerials.Any(serial =>
            _deviceTracker.GetDevice(serial)?.Status == AdbDeviceStatus.Online &&
            !catalogSerials.Contains(serial));
    }

    private async Task RecoverDeviceAsync(
        string serial,
        CancellationToken cancellationToken)
    {
        if (!Volatile.Read(ref _isActive) ||
            _deviceTracker.Health != AdbDeviceTrackerHealth.Connected)
        {
            return;
        }

        ViewMultipleDeviceItemViewModel[] catalogItems = await SnapshotOnlineItemsAsync()
            .ConfigureAwait(false);
        ViewMultipleDeviceItemViewModel? catalogItem = catalogItems.FirstOrDefault(item =>
            string.Equals(item.Serial, serial, StringComparison.OrdinalIgnoreCase));
        if (catalogItem is null)
            return;

        ViewMultipleDeviceItemViewModel[] visibleItems = await SnapshotVisibleItemsAsync()
            .ConfigureAwait(false);
        ViewMultipleDeviceItemViewModel? visibleItem = visibleItems.FirstOrDefault(item =>
            string.Equals(item.Serial, serial, StringComparison.OrdinalIgnoreCase));
        if (visibleItem is not null)
            await visibleItem.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    private void RequestItemRetry(ViewMultipleDeviceItemViewModel item, int attempt)
    {
        if (Volatile.Read(ref _disposed) != 0 || !Volatile.Read(ref _isActive))
            return;

        int navigationGeneration;
        lock (_navigationSchedulingGate)
        {
            navigationGeneration = Volatile.Read(ref _generation);
        }

        CancellationToken activationToken = _activationCancellation?.Token
            ?? _lifetimeCancellation.Token;
        CancellationTokenSource retryCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(activationToken);
        TaskCompletionSource<bool> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenSource? previous = null;

        lock (_retrySchedulingGate)
        {
            if (Volatile.Read(ref _disposed) != 0 || !Volatile.Read(ref _isActive))
            {
                retryCancellation.Dispose();
                return;
            }

            if (_retryCancellations.Remove(item, out previous))
                _retryTasks.RemoveWhere(task => task.IsCompleted);

            _retryCancellations[item] = retryCancellation;
            _retryTasks.Add(completion.Task);
        }

        CancelCancellation(previous);
        _ = ExecuteItemRetryAsync(
            item,
            attempt,
            navigationGeneration,
            retryCancellation,
            completion);
    }

    private async Task ExecuteItemRetryAsync(
        ViewMultipleDeviceItemViewModel item,
        int attempt,
        int navigationGeneration,
        CancellationTokenSource retryCancellation,
        TaskCompletionSource<bool> completion)
    {
        try
        {
            await _transitionGate.WaitAsync(retryCancellation.Token).ConfigureAwait(false);
            try
            {
                if (!IsCurrentOperation(navigationGeneration, retryCancellation.Token) ||
                    _deviceTracker.Health != AdbDeviceTrackerHealth.Connected ||
                    _deviceTracker.GetDevice(item.Serial)?.Status != AdbDeviceStatus.Online ||
                    !await IsVisibleItemAsync(item, retryCancellation.Token).ConfigureAwait(false))
                {
                    return;
                }

                await item.StartAsync(retryCancellation.Token).ConfigureAwait(false);
            }
            finally
            {
                _transitionGate.Release();
            }
        }
        catch (OperationCanceledException) when (retryCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogDebug(
                exception,
                "Multi-view scrcpy retry {Attempt} failed for {Serial}.",
                attempt,
                item.Serial);
        }
        finally
        {
            lock (_retrySchedulingGate)
            {
                if (_retryCancellations.TryGetValue(item, out CancellationTokenSource? current) &&
                    ReferenceEquals(current, retryCancellation))
                {
                    _retryCancellations.Remove(item);
                }

                _retryTasks.Remove(completion.Task);
            }

            retryCancellation.Dispose();
            completion.TrySetResult(true);
        }
    }

    private async Task<bool> IsVisibleItemAsync(
        ViewMultipleDeviceItemViewModel item,
        CancellationToken cancellationToken)
    {
        bool isVisible = false;
        await _uiDispatcher
            .InvokeAsync(() => isVisible = VisibleDevices.Contains(item), cancellationToken)
            .ConfigureAwait(false);
        return isVisible;
    }

    private void CancelPendingRetryTasks()
    {
        CancellationTokenSource[] pending;
        lock (_retrySchedulingGate)
        {
            pending = _retryCancellations.Values.ToArray();
            _retryCancellations.Clear();
        }

        foreach (CancellationTokenSource cancellation in pending)
            CancelCancellation(cancellation);
    }

    private async Task CancelItemRetriesAsync()
    {
        ViewMultipleDeviceItemViewModel[] items = await SnapshotOnlineItemsAsync()
            .ConfigureAwait(false);
        foreach (ViewMultipleDeviceItemViewModel item in items)
            item.CancelRetry();

        CancelPendingRetryTasks();
    }

    private async Task CancelOperationsAndWaitAsync()
    {
        CancellationTokenSource? navigationCancellation;
        Task navigationTask;
        lock (_navigationSchedulingGate)
        {
            Interlocked.Increment(ref _generation);
            navigationCancellation = _navigationCancellation;
            _navigationCancellation = null;
            navigationTask = _navigationTask;
            _navigationTask = Task.CompletedTask;
        }

        CancellationTokenSource? trackerCancellation;
        Task trackerTask;
        lock (_trackerSchedulingGate)
        {
            _pendingTrackerWork.Clear();
            Interlocked.Exchange(ref _trackerDirtyDuringRefresh, 0);
            trackerCancellation = _trackerCancellation;
            _trackerCancellation = null;
            trackerTask = _trackerTask;
            _trackerTask = Task.CompletedTask;
        }

        CancellationTokenSource[] retryCancellations;
        Task[] retryTasks;
        lock (_retrySchedulingGate)
        {
            retryCancellations = _retryCancellations.Values.ToArray();
            _retryCancellations.Clear();
            retryTasks = _retryTasks.ToArray();
        }

        CancelCancellation(navigationCancellation);
        CancelCancellation(trackerCancellation);
        foreach (CancellationTokenSource cancellation in retryCancellations)
            CancelCancellation(cancellation);

        await AwaitOperationAsync(
                navigationTask,
                "View Multiple Devices navigation")
            .ConfigureAwait(false);
        await AwaitOperationAsync(
                trackerTask,
                "View Multiple Devices tracker refresh")
            .ConfigureAwait(false);
        if (retryTasks.Length > 0)
        {
            try
            {
                await Task.WhenAll(retryTasks).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "View Multiple Devices retry tasks did not complete during deactivation.");
            }
        }
    }

    private async Task AwaitOperationAsync(Task operation, string operationName)
    {
        try
        {
            await operation.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "{OperationName} did not complete during deactivation.", operationName);
        }
    }

    private async Task StopActiveItemsAsync()
    {
        ViewMultipleDeviceItemViewModel[] items = await SnapshotOnlineItemsAsync()
            .ConfigureAwait(false);
        await StopItemsAsync(items).ConfigureAwait(false);
    }

    private async Task StopItemsAsync(IEnumerable<ViewMultipleDeviceItemViewModel> items)
    {
        Task[] stopTasks = items
            .Distinct()
            .Select(StopItemSafelyAsync)
            .ToArray();
        await Task.WhenAll(stopTasks).ConfigureAwait(false);
    }

    private async Task StopItemSafelyAsync(ViewMultipleDeviceItemViewModel item)
    {
        try
        {
            await item.StopSessionAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to stop multi-view device session for {Serial}.", item.Serial);
        }
    }

    private async Task DisposeItemsAsync(IEnumerable<ViewMultipleDeviceItemViewModel> items)
    {
        Task[] disposeTasks = items
            .Distinct()
            .Select(DisposeItemSafelyAsync)
            .ToArray();
        await Task.WhenAll(disposeTasks).ConfigureAwait(false);
    }

    private async Task DisposeItemSafelyAsync(ViewMultipleDeviceItemViewModel item)
    {
        try
        {
            await item.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to dispose multi-view device item for {Serial}.", item.Serial);
        }
    }

    private async Task<ViewMultipleDeviceItemViewModel[]> SnapshotOnlineItemsAsync()
    {
        ViewMultipleDeviceItemViewModel[] items = [];
        await _uiDispatcher
            .InvokeAsync(() => items = OnlineDevices.ToArray())
            .ConfigureAwait(false);
        return items;
    }

    private async Task<ViewMultipleDeviceItemViewModel[]> SnapshotVisibleItemsAsync()
    {
        ViewMultipleDeviceItemViewModel[] items = [];
        await _uiDispatcher
            .InvokeAsync(() => items = VisibleDevices.ToArray())
            .ConfigureAwait(false);
        return items;
    }

    private async Task MarkVisibleDeviceDisconnectedAsync(string serial)
    {
        try
        {
            if (!Volatile.Read(ref _isActive) || Volatile.Read(ref _disposed) != 0)
                return;

            ViewMultipleDeviceItemViewModel[] visibleItems = await SnapshotVisibleItemsAsync()
                .ConfigureAwait(false);
            ViewMultipleDeviceItemViewModel? item = visibleItems.FirstOrDefault(device =>
                string.Equals(device.Serial, serial, StringComparison.OrdinalIgnoreCase));
            if (item is not null)
                await item.MarkDisconnectedAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Could not update disconnected state for multi-view device {Serial}.", serial);
        }
    }

    private async Task MarkVisibleDevicesDisconnectedAsync()
    {
        try
        {
            if (!Volatile.Read(ref _isActive) || Volatile.Read(ref _disposed) != 0)
                return;

            ViewMultipleDeviceItemViewModel[] visibleItems = await SnapshotVisibleItemsAsync()
                .ConfigureAwait(false);
            await Task.WhenAll(visibleItems.Select(item => item.MarkDisconnectedAsync()))
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Could not update disconnected state for multi-view devices.");
        }
    }

    private void OnDeviceStateChanged(object? sender, AdbDeviceStateChangedEventArgs eventArgs)
    {
        _ = NotifyOnlineDeviceCountAsync();
        QueueTrackerTransition(
            eventArgs.Current?.Status == AdbDeviceStatus.Online
                ? TrackerTransitionKind.DeviceOnline
                : TrackerTransitionKind.DeviceOffline,
            eventArgs.Serial);
    }

    private void OnTrackerHealthChanged(
        object? sender,
        AdbDeviceTrackerHealthChangedEventArgs eventArgs)
    {
        _ = NotifyOnlineDeviceCountAsync();
        QueueTrackerTransition(eventArgs.Current switch
        {
            AdbDeviceTrackerHealth.Reconnecting => TrackerTransitionKind.TrackerReconnecting,
            AdbDeviceTrackerHealth.Connected => TrackerTransitionKind.TrackerConnected,
            _ => TrackerTransitionKind.TrackerReconnecting
        });
    }

    private void SubscribeToTracker()
    {
        if (_trackerEventsSubscribed)
            return;

        _deviceTracker.DeviceStateChanged += OnDeviceStateChanged;
        _deviceTracker.HealthChanged += OnTrackerHealthChanged;
        _trackerEventsSubscribed = true;
    }

    private void UnsubscribeFromTracker()
    {
        if (!_trackerEventsSubscribed)
            return;

        _deviceTracker.DeviceStateChanged -= OnDeviceStateChanged;
        _deviceTracker.HealthChanged -= OnTrackerHealthChanged;
        _trackerEventsSubscribed = false;
    }

    private void ZoomOut()
    {
        SetZoomIndex(_zoomIndex - 1);
    }

    private void ZoomIn()
    {
        SetZoomIndex(_zoomIndex + 1);
    }

    private void SetZoomIndex(int index)
    {
        if (index < 0 || index >= ZoomLevels.Length)
            return;

        if (!TryCalculateFitTileSize())
            _fitPending = false;

        SetZoomIndexCore(index);
        ApplyTileSizesCore();
        NotifyZoomCommands();
    }

    private void SetZoomIndexCore(int index)
    {
        if (_zoomIndex == index)
            return;

        _zoomIndex = index;
        OnPropertyChanged(nameof(ZoomPercent));
    }

    private void FitToViewportCore()
    {
        _fitPending = true;
        SetZoomIndexCore(FitZoomIndex);
        if (!TryCalculateFitTileSize())
            return;

        _fitPending = false;
        ApplyTileSizesCore();
    }

    private void ApplyInitialFitCore()
    {
        _fitPending = true;
        if (!TryCalculateFitTileSize())
            return;

        _fitPending = false;
        ApplyTileSizesCore();
    }

    private bool TryCalculateFitTileSize()
    {
        if (!double.IsFinite(_viewportWidth) ||
            _viewportWidth <= 0)
        {
            return false;
        }

        double usableWidth = Math.Max(1, _viewportWidth);
        double baseTileOuterWidth = usableWidth / BaseTilesPerRow;
        double baseTileContentWidth = baseTileOuterWidth - _tileHorizontalFootprint;
        _fitTileWidth = Math.Max(1, baseTileContentWidth);
        return true;
    }

    private void ApplyTileSizesCore()
    {
        double width = CalculateScaledTileWidth();
        foreach (ViewMultipleDeviceItemViewModel item in OnlineDevices)
            item.SetTileSize(width);
    }

    private double CalculateScaledTileWidth()
    {
        double scale = ZoomPercent / 100d;
        double maxTileContentWidth = Math.Max(1, _viewportWidth - _tileHorizontalFootprint);
        double scaledWidth = _fitTileWidth * scale;
        if (!double.IsFinite(scaledWidth) || scaledWidth <= 0)
            return Math.Min(1, maxTileContentWidth);

        // The tile margin is applied to the tile itself, so cap the content
        // width after scaling to keep the complete outer footprint inside the
        // non-scrolling horizontal viewport.
        return Math.Min(maxTileContentWidth, scaledWidth);
    }

    private void ResetPagingForActivation()
    {
        _requestedPage = 1;
        CurrentPage = 1;
        _fitPending = true;
        SetZoomIndexCore(DefaultZoomIndex);
        NotifyPaginationCommands();
        NotifyZoomCommands();
    }

    private void NotifyCollectionSummary()
    {
        OnPropertyChanged(nameof(OnlineDeviceCount));
        OnPropertyChanged(nameof(CatalogDeviceCount));
        OnPropertyChanged(nameof(HasOnlineDevices));
        OnPropertyChanged(nameof(TotalPages));
        OnPropertyChanged(nameof(ShowingStart));
        OnPropertyChanged(nameof(ShowingEnd));
        NotifyPaginationCommands();
    }

    private void NotifyPaginationCommands()
    {
        PreviousPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
    }

    private async Task NotifyPaginationCommandsAsync()
    {
        try
        {
            await _uiDispatcher
                .InvokeAsync(NotifyPaginationCommands)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Could not refresh multi-view pagination commands.");
        }
    }

    private async Task NotifyOnlineDeviceCountAsync()
    {
        try
        {
            await _uiDispatcher
                .InvokeAsync(() => OnPropertyChanged(nameof(OnlineDeviceCount)))
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Could not refresh the multi-view online device count.");
        }
    }

    private void NotifyZoomCommands()
    {
        ZoomOutCommand.NotifyCanExecuteChanged();
        ZoomInCommand.NotifyCanExecuteChanged();
        FitCommand.NotifyCanExecuteChanged();
    }

    private bool CanZoomOut()
    {
        return Volatile.Read(ref _isActive) && _zoomIndex > 0;
    }

    private bool CanZoomIn()
    {
        return Volatile.Read(ref _isActive) && _zoomIndex < ZoomLevels.Length - 1;
    }

    private bool CanFit()
    {
        return Volatile.Read(ref _isActive);
    }

    private bool CanNavigatePrevious()
    {
        return Volatile.Read(ref _isActive) &&
               _deviceTracker.Health == AdbDeviceTrackerHealth.Connected &&
               Volatile.Read(ref _requestedPage) > 1;
    }

    private bool CanNavigateNext()
    {
        return Volatile.Read(ref _isActive) &&
               _deviceTracker.Health == AdbDeviceTrackerHealth.Connected &&
               Volatile.Read(ref _requestedPage) < CalculateTotalPages(CatalogDeviceCount);
    }

    private bool IsCurrentOperation(
        int? navigationGeneration,
        CancellationToken cancellationToken)
    {
        return !cancellationToken.IsCancellationRequested &&
               Volatile.Read(ref _disposed) == 0 &&
               Volatile.Read(ref _isActive) &&
               (!navigationGeneration.HasValue ||
                navigationGeneration.Value == Volatile.Read(ref _generation));
    }

    private bool IsLatestNavigation(
        int generation,
        CancellationTokenSource navigationCancellation)
    {
        return IsCurrentOperation(generation, navigationCancellation.Token);
    }

    private async Task ReconcileFailedNavigationAsync(int generation)
    {
        try
        {
            await _uiDispatcher
                .InvokeAsync(() =>
                {
                    if (Volatile.Read(ref _disposed) != 0 ||
                        !Volatile.Read(ref _isActive) ||
                        generation != Volatile.Read(ref _generation))
                    {
                        return;
                    }

                    _requestedPage = CurrentPage;
                    NotifyPaginationCommands();
                })
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Could not reconcile the failed multi-view page navigation.");
        }
    }

    private static int CalculateTotalPages(int itemCount)
    {
        return Math.Max(1, (itemCount + PageSize - 1) / PageSize);
    }

    private void CancelCancellation(CancellationTokenSource? cancellation)
    {
        try
        {
            cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Could not cancel a View Multiple Devices operation.");
        }
    }
}
