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

    private static readonly int[] ZoomLevels =
    [
        50, 75, 100, 125, 150, 175, 200, 225, 250, 275, 300
    ];
    private static readonly int DefaultZoomIndex = Array.IndexOf(ZoomLevels, 100);
    private static readonly int FitZoomIndex = Array.IndexOf(ZoomLevels, 100);

    private readonly IDeviceStoreService _deviceStoreService;
    private readonly IAdbDeviceTrackerService _deviceTracker;
    private readonly ISingleViewDeviceSessionFactory _sessionFactory;
    private readonly IViewDeviceClipboardService _clipboardService;
    private readonly ILocalizationService _localization;
    private readonly IUiDispatcherService _uiDispatcher;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<ViewMultipleDevicesViewModel> _logger;
    private readonly IDeviceMetadataChangeNotifier? _metadataChangeNotifier;
    private readonly IViewDeviceWindowService _viewDeviceWindowService;
    private readonly IViewDevicePresentationCoordinator _presentationCoordinator;
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
    private IDisposable? _multiViewRegistration;
    private int _trackerRefreshPending;
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

    public ViewMultipleDevicesViewModel(
        IDeviceStoreService deviceStoreService,
        IAdbDeviceTrackerService deviceTracker,
        ISingleViewDeviceSessionFactory sessionFactory,
        IViewDeviceClipboardService clipboardService,
        ILocalizationService localization,
        IUiDispatcherService uiDispatcher,
        ILoggerFactory loggerFactory,
        ILogger<ViewMultipleDevicesViewModel> logger,
        IViewDeviceWindowService viewDeviceWindowService,
        IViewDevicePresentationCoordinator presentationCoordinator,
        IDeviceMetadataChangeNotifier? metadataChangeNotifier = null)
    {
        ArgumentNullException.ThrowIfNull(deviceStoreService);
        ArgumentNullException.ThrowIfNull(deviceTracker);
        ArgumentNullException.ThrowIfNull(sessionFactory);
        ArgumentNullException.ThrowIfNull(clipboardService);
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentNullException.ThrowIfNull(uiDispatcher);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(viewDeviceWindowService);
        ArgumentNullException.ThrowIfNull(presentationCoordinator);

        _deviceStoreService = deviceStoreService;
        _deviceTracker = deviceTracker;
        _sessionFactory = sessionFactory;
        _clipboardService = clipboardService;
        _localization = localization;
        _uiDispatcher = uiDispatcher;
        _loggerFactory = loggerFactory;
        _logger = logger;
        _metadataChangeNotifier = metadataChangeNotifier;
        _viewDeviceWindowService = viewDeviceWindowService;
        _presentationCoordinator = presentationCoordinator;
        _zoomIndex = DefaultZoomIndex;

        ZoomOutCommand = new RelayCommand(ZoomOut, CanZoomOut);
        ZoomInCommand = new RelayCommand(ZoomIn, CanZoomIn);
        FitCommand = new RelayCommand(FitToViewport, CanFit);
        PreviousPageCommand = new RelayCommand(RequestPreviousPage, CanNavigatePrevious);
        NextPageCommand = new RelayCommand(RequestNextPage, CanNavigateNext);
        _metadataChangeNotifier?.DeviceNameChanged += OnDeviceNameChanged;
    }

    // Invariant:
    // every item in OnlineDevices represents a saved device
    // currently reported as AdbDeviceStatus.Online while tracker
    // health is Connected. Offline devices are removed, not retained
    // for reconnect.
    public ObservableCollection<ViewMultipleDeviceItemViewModel> OnlineDevices { get; } = [];

    public ObservableCollection<ViewMultipleDeviceItemViewModel> VisibleDevices { get; } = [];

    public int CatalogDeviceCount => OnlineDevices.Count;

    public int OnlineDeviceCount => OnlineDevices.Count;

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
            // during StartAsync or initial reconciliation is coalesced for later.
            Volatile.Write(ref _initializationInProgress, 1);
            _multiViewRegistration = _presentationCoordinator.RegisterMultiView(
                SuspendMultiViewAsync,
                ResumeMultiViewAsync);
            SubscribeToTracker();

            try
            {
                await _deviceTracker.StartAsync(activation.Token).ConfigureAwait(false);
                int generation = Interlocked.Increment(ref _generation);
                await _transitionGate.WaitAsync(activation.Token).ConfigureAwait(false);
                try
                {
                    bool applied = await ReconcileOnlineCatalogAsync(
                            requestedPage: 1,
                            navigationGeneration: generation,
                            cancellationToken: activation.Token)
                        .ConfigureAwait(false);
                    if (!applied)
                    {
                        activation.Token.ThrowIfCancellationRequested();
                        throw new InvalidOperationException("The initial multi-view page was superseded.");
                    }

                    await _uiDispatcher
                        .InvokeAsync(ApplyInitialFitCore, activation.Token)
                        .ConfigureAwait(false);
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
            _metadataChangeNotifier?.DeviceNameChanged -= OnDeviceNameChanged;
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
        try
        {
            // Keep the registration active while the final stop/dispose drain
            // runs. Suspend callbacks serialize behind this gate and therefore
            // cannot observe a cleared catalog before its sessions are stopped.
            await _transitionGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                ViewMultipleDeviceItemViewModel[] items =
                    await SnapshotOnlineItemsAsync().ConfigureAwait(false);
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
                await StopItemsAsync(items).ConfigureAwait(false);
                await DisposeItemsAsync(items).ConfigureAwait(false);
            }
            finally
            {
                _transitionGate.Release();
            }
        }
        finally
        {
            IDisposable? multiViewRegistration = Interlocked.Exchange(
                ref _multiViewRegistration,
                null);
            multiViewRegistration?.Dispose();
            activation?.Dispose();
        }
    }

    private ViewMultipleDeviceItemViewModel CreateItem(StoredDeviceConfig device)
    {
        return new ViewMultipleDeviceItemViewModel(
            device.Serial,
            device.Name,
            _sessionFactory,
            _deviceTracker,
            _clipboardService,
            _localization,
            _uiDispatcher,
            _loggerFactory.CreateLogger<ViewMultipleDeviceItemViewModel>(),
            _presentationCoordinator,
            RequestItemRetry,
            openViewDeviceRequest: OpenViewDeviceAsync);
    }

    private void OnDeviceNameChanged(object? sender, DeviceNameChangedEventArgs eventArgs)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        void ApplyNameChange()
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;

            foreach (ViewMultipleDeviceItemViewModel item in OnlineDevices.Where(item =>
                         string.Equals(item.Serial, eventArgs.Serial, StringComparison.OrdinalIgnoreCase)))
            {
                item.UpdateDeviceName(eventArgs.Name);
            }
        }

        if (_uiDispatcher.CheckAccess())
        {
            ApplyNameChange();
            return;
        }

        _ = _uiDispatcher.InvokeAsync(ApplyNameChange);
    }

    private async Task StartItemsAsync(
        IEnumerable<ViewMultipleDeviceItemViewModel> items,
        CancellationToken cancellationToken)
    {
        ViewMultipleDeviceItemViewModel[] distinctItems = items
            .Distinct()
            .ToArray();
        if (distinctItems.Length == 0)
            return;

        try
        {
            await Task.WhenAll(distinctItems.Select(item =>
                    StartVisibleItemAsync(item, cancellationToken)))
                .ConfigureAwait(false);
        }
        catch
        {
            await StopItemsAsync(distinctItems).ConfigureAwait(false);
            throw;
        }
    }

    private async Task StartVisibleItemAsync(
        ViewMultipleDeviceItemViewModel item,
        CancellationToken cancellationToken)
    {
        if (_presentationCoordinator.IsDedicatedViewActive(item.Serial))
        {
            await item.SuspendForDedicatedViewAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await item.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> ApplyVisiblePageAsync(
        int requestedPage,
        int? navigationGeneration,
        CancellationToken cancellationToken)
    {
        if (!IsCurrentOperation(navigationGeneration, cancellationToken) ||
            _deviceTracker.Health != AdbDeviceTrackerHealth.Connected)
        {
            return false;
        }

        ViewMultipleDeviceItemViewModel[] oldVisibleItems =
            await SnapshotVisibleItemsAsync().ConfigureAwait(false);
        ViewMultipleDeviceItemViewModel[] catalog =
            await SnapshotOnlineItemsAsync().ConfigureAwait(false);
        int targetPage = Math.Clamp(
            requestedPage,
            1,
            CalculateTotalPages(catalog.Length));
        ViewMultipleDeviceItemViewModel[] desiredVisibleItems = catalog
            .Skip((targetPage - 1) * PageSize)
            .Take(PageSize)
            .ToArray();
        bool applied = false;
        await _uiDispatcher
            .InvokeAsync(() =>
            {
                if (!IsCurrentOperation(navigationGeneration, cancellationToken) ||
                    _deviceTracker.Health != AdbDeviceTrackerHealth.Connected)
                {
                    return;
                }

                ApplyCollectionOrder(VisibleDevices, desiredVisibleItems);
                _requestedPage = targetPage;
                CurrentPage = targetPage;
                NotifyCollectionSummary();
                applied = true;
            }, cancellationToken)
            .ConfigureAwait(false);

        if (!applied)
            return false;

        ViewMultipleDeviceItemViewModel[] noLongerVisibleItems = oldVisibleItems
            .Except(desiredVisibleItems)
            .ToArray();
        ViewMultipleDeviceItemViewModel[] newlyVisibleItems = desiredVisibleItems
            .Except(oldVisibleItems)
            .ToArray();
        await StopItemsAsync(noLongerVisibleItems).ConfigureAwait(false);
        await StartItemsAsync(newlyVisibleItems, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> ReconcileOnlineCatalogAsync(
        int requestedPage,
        int? navigationGeneration,
        CancellationToken cancellationToken)
    {
        if (!IsCurrentOperation(navigationGeneration, cancellationToken) ||
            _deviceTracker.Health != AdbDeviceTrackerHealth.Connected)
        {
            return false;
        }

        IReadOnlyList<StoredDeviceConfig> savedDevices =
            await _deviceStoreService.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (_deviceTracker.Health != AdbDeviceTrackerHealth.Connected)
            return false;

        HashSet<string> onlineSerials = _deviceTracker.CurrentSnapshot
            .Where(device => device.Status == AdbDeviceStatus.Online)
            .Select(device => device.Serial)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        StoredDeviceConfig[] desiredConfigs = savedDevices
            .Where(device => onlineSerials.Contains(device.Serial))
            .ToArray();

        ViewMultipleDeviceItemViewModel[] existingItems = await SnapshotOnlineItemsAsync()
            .ConfigureAwait(false);
        Dictionary<string, ViewMultipleDeviceItemViewModel> existingBySerial = existingItems
            .ToDictionary(item => item.Serial, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string?> desiredNames = new(StringComparer.OrdinalIgnoreCase);
        List<ViewMultipleDeviceItemViewModel> addedItems = [];
        List<ViewMultipleDeviceItemViewModel> desiredItems = [];
        foreach (StoredDeviceConfig device in desiredConfigs)
        {
            if (existingBySerial.TryGetValue(device.Serial, out ViewMultipleDeviceItemViewModel? existing))
            {
                desiredNames[existing.Serial] = device.Name;
                desiredItems.Add(existing);
                continue;
            }

            ViewMultipleDeviceItemViewModel added = CreateItem(device);
            addedItems.Add(added);
            desiredNames[added.Serial] = device.Name;
            desiredItems.Add(added);
        }

        ViewMultipleDeviceItemViewModel[] oldVisibleItems = await SnapshotVisibleItemsAsync()
            .ConfigureAwait(false);
        int targetPage = Math.Clamp(
            requestedPage,
            1,
            CalculateTotalPages(desiredItems.Count));
        ViewMultipleDeviceItemViewModel[] desiredVisibleItems = desiredItems
            .Skip((targetPage - 1) * PageSize)
            .Take(PageSize)
            .ToArray();
        bool applied = false;
        try
        {
            await _uiDispatcher
                .InvokeAsync(() =>
                {
                    if (!IsCurrentOperation(navigationGeneration, cancellationToken) ||
                        _deviceTracker.Health != AdbDeviceTrackerHealth.Connected)
                    {
                        return;
                    }

                    ApplyCollectionOrder(OnlineDevices, desiredItems);
                    ApplyCollectionOrder(VisibleDevices, desiredVisibleItems);
                    double tileWidth = CalculateScaledTileWidth();
                    foreach (ViewMultipleDeviceItemViewModel item in desiredItems)
                    {
                        if (desiredNames.TryGetValue(item.Serial, out string? desiredName))
                            item.UpdateDeviceName(desiredName ?? item.Serial);
                        item.SetTileSize(tileWidth);
                    }

                    _requestedPage = targetPage;
                    CurrentPage = targetPage;
                    NotifyCollectionSummary();
                    applied = true;
                }, cancellationToken)
                .ConfigureAwait(false);

            if (!applied)
            {
                await DisposeItemsAsync(addedItems).ConfigureAwait(false);
                return false;
            }

            ViewMultipleDeviceItemViewModel[] removedItems = existingItems
                .Except(desiredItems)
                .ToArray();
            ViewMultipleDeviceItemViewModel[] noLongerVisibleItems = oldVisibleItems
                .Except(desiredVisibleItems)
                .Except(removedItems)
                .ToArray();
            await StopItemsAsync(removedItems.Concat(noLongerVisibleItems))
                .ConfigureAwait(false);
            await DisposeItemsAsync(removedItems).ConfigureAwait(false);
            ViewMultipleDeviceItemViewModel[] newlyVisibleItems = desiredVisibleItems
                .Except(oldVisibleItems)
                .ToArray();
            await StartItemsAsync(newlyVisibleItems, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch
        {
            if (!applied)
                await DisposeItemsAsync(addedItems).ConfigureAwait(false);
            throw;
        }
    }

    private async Task ClearCatalogAsync()
    {
        ViewMultipleDeviceItemViewModel[] items = await SnapshotOnlineItemsAsync()
            .ConfigureAwait(false);
        await _uiDispatcher
            .InvokeAsync(() =>
            {
                VisibleDevices.Clear();
                OnlineDevices.Clear();
                _requestedPage = 1;
                CurrentPage = 1;
                _fitPending = true;
                NotifyCollectionSummary();
            })
            .ConfigureAwait(false);
        await StopItemsAsync(items).ConfigureAwait(false);
        await DisposeItemsAsync(items).ConfigureAwait(false);
    }

    private async Task SuspendMultiViewAsync(
        string serial,
        CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        await _transitionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ViewMultipleDeviceItemViewModel[] items = await SnapshotOnlineItemsAsync()
                .ConfigureAwait(false);
            ViewMultipleDeviceItemViewModel? item = items.FirstOrDefault(candidate =>
                string.Equals(candidate.Serial, serial, StringComparison.OrdinalIgnoreCase));
            if (item is not null)
                await item.SuspendForDedicatedViewAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _transitionGate.Release();
        }
    }

    private async Task ResumeMultiViewAsync(
        string serial,
        CancellationToken cancellationToken)
    {
        if (!Volatile.Read(ref _isActive) ||
            Volatile.Read(ref _disposed) != 0 ||
            _deviceTracker.Health != AdbDeviceTrackerHealth.Connected ||
            _deviceTracker.GetDevice(serial)?.Status != AdbDeviceStatus.Online)
        {
            return;
        }

        await _transitionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ViewMultipleDeviceItemViewModel[] items = await SnapshotOnlineItemsAsync()
                .ConfigureAwait(false);
            ViewMultipleDeviceItemViewModel? item = items.FirstOrDefault(candidate =>
                string.Equals(candidate.Serial, serial, StringComparison.OrdinalIgnoreCase));
            if (item is null)
                return;

            ViewMultipleDeviceItemViewModel[] visibleItems = await SnapshotVisibleItemsAsync()
                .ConfigureAwait(false);
            if (visibleItems.Contains(item) &&
                !_presentationCoordinator.IsDedicatedViewActive(serial))
            {
                await item.StartAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _transitionGate.Release();
        }
    }

    private async Task OpenViewDeviceAsync(
        ViewMultipleDeviceItemViewModel item,
        CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _disposed) != 0 ||
            !Volatile.Read(ref _isActive) ||
            _deviceTracker.Health != AdbDeviceTrackerHealth.Connected ||
            _deviceTracker.GetDevice(item.Serial)?.Status != AdbDeviceStatus.Online)
        {
            return;
        }

        bool stillPublished = false;
        await _uiDispatcher
            .InvokeAsync(() => stillPublished = OnlineDevices.Contains(item), cancellationToken)
            .ConfigureAwait(false);
        if (!stillPublished ||
            Volatile.Read(ref _disposed) != 0 ||
            !Volatile.Read(ref _isActive) ||
            _deviceTracker.Health != AdbDeviceTrackerHealth.Connected ||
            _deviceTracker.GetDevice(item.Serial)?.Status != AdbDeviceStatus.Online)
        {
            return;
        }

        await _viewDeviceWindowService.OpenAsync(
            item.Serial,
            item.DeviceName,
            cancellationToken).ConfigureAwait(false);
    }

    private static void ApplyCollectionOrder<T>(
        ObservableCollection<T> target,
        IReadOnlyList<T> desired)
    {
        for (int index = target.Count - 1; index >= 0; index--)
        {
            if (!desired.Contains(target[index]))
                target.RemoveAt(index);
        }

        for (int index = 0; index < desired.Count; index++)
        {
            if (index < target.Count &&
                EqualityComparer<T>.Default.Equals(target[index], desired[index]))
            {
                continue;
            }

            int existingIndex = target.IndexOf(desired[index]);
            if (existingIndex >= 0)
                target.Move(existingIndex, index);
            else
                target.Insert(index, desired[index]);
        }

        while (target.Count > desired.Count)
            target.RemoveAt(target.Count - 1);
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

                completed = await ApplyVisiblePageAsync(
                        requestedPage,
                        generation,
                        navigationCancellation.Token)
                    .ConfigureAwait(false);
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

    private void QueueTrackerRefresh()
    {
        if (Volatile.Read(ref _disposed) != 0 || !Volatile.Read(ref _isActive))
            return;

        Interlocked.Exchange(ref _trackerRefreshPending, 1);
        lock (_trackerSchedulingGate)
        {
            if (Volatile.Read(ref _disposed) != 0 || !Volatile.Read(ref _isActive))
                return;
            EnsureTrackerWorkerLocked();
        }
    }

    private void EnsureTrackerWorkerIfPending()
    {
        lock (_trackerSchedulingGate)
        {
            EnsureTrackerWorkerLocked();
        }
    }

    private void EnsureTrackerWorkerLocked()
    {
        if (Volatile.Read(ref _trackerRefreshPending) == 0 ||
            !_trackerTask.IsCompleted ||
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

    private async Task ProcessTrackerWorkAsync(CancellationTokenSource workerCancellation)
    {
        try
        {
            while (Interlocked.Exchange(ref _trackerRefreshPending, 0) != 0)
            {
                workerCancellation.Token.ThrowIfCancellationRequested();
                await _transitionGate.WaitAsync(workerCancellation.Token).ConfigureAwait(false);
                try
                {
                    if (!Volatile.Read(ref _isActive))
                        break;

                    if (_deviceTracker.Health != AdbDeviceTrackerHealth.Connected)
                    {
                        await ClearCatalogAsync().ConfigureAwait(false);
                        continue;
                    }

                    await ReconcileOnlineCatalogAsync(
                            Volatile.Read(ref _requestedPage),
                            navigationGeneration: null,
                            cancellationToken: workerCancellation.Token)
                        .ConfigureAwait(false);
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
            lock (_trackerSchedulingGate)
            {
                if (ReferenceEquals(_trackerCancellation, workerCancellation))
                {
                    _trackerCancellation = null;
                    _trackerTask = Task.CompletedTask;
                    if (Volatile.Read(ref _isActive) &&
                        Volatile.Read(ref _trackerRefreshPending) != 0)
                        EnsureTrackerWorkerLocked();
                }
            }

            workerCancellation.Dispose();
            await NotifyPaginationCommandsAsync().ConfigureAwait(false);
        }
    }

    private void RequestItemRetry(ViewMultipleDeviceItemViewModel item, int attempt)
    {
        if (Volatile.Read(ref _disposed) != 0 ||
            !Volatile.Read(ref _isActive) ||
            _presentationCoordinator.IsDedicatedViewActive(item.Serial))
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

    private void CancelRetryForItem(ViewMultipleDeviceItemViewModel item)
    {
        CancellationTokenSource? retryCancellation;
        lock (_retrySchedulingGate)
        {
            _retryCancellations.Remove(item, out retryCancellation);
        }

        CancelCancellation(retryCancellation);
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
            Interlocked.Exchange(ref _trackerRefreshPending, 0);
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
        ViewMultipleDeviceItemViewModel[] distinctItems = items
            .Distinct()
            .ToArray();
        foreach (ViewMultipleDeviceItemViewModel item in distinctItems)
            CancelRetryForItem(item);

        Task[] stopTasks = distinctItems
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

    private void OnDeviceStateChanged(object? sender, AdbDeviceStateChangedEventArgs eventArgs)
    {
        QueueTrackerRefresh();
    }

    private void OnTrackerHealthChanged(
        object? sender,
        AdbDeviceTrackerHealthChangedEventArgs eventArgs)
    {
        QueueTrackerRefresh();
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
