using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using DeepDroidChanger.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace DeepDroidChanger.Tests.ViewModels;

[TestClass]
public sealed class ViewMultipleDevicesViewModelTests
{
    [TestMethod]
    public void PageSize_Remains8()
    {
        int pageSize = GetPageSize();
        Assert.AreEqual(8, pageSize);
    }

    [TestMethod]
    [DataRow(0, 1, 0)]
    [DataRow(1, 1, 1)]
    [DataRow(8, 1, 8)]
    [DataRow(9, 2, 8)]
    [DataRow(16, 2, 8)]
    [DataRow(19, 3, 8)]
    public async Task Initialize_PaginatesWithEightDevicesPerPage(
        int deviceCount,
        int expectedTotalPages,
        int expectedVisibleCount)
    {
        (FakeDeviceStoreService store, FakeTracker tracker, FakeSessionFactory factory) =
            CreateFixture(deviceCount);
        await using ViewMultipleDevicesViewModel viewModel = CreateViewModel(
            store,
            tracker,
            factory);

        await viewModel.InitializeAsync();
        await viewModel.WaitForPendingOperationsAsync();

        Assert.AreEqual(expectedTotalPages, viewModel.TotalPages);
        Assert.AreEqual(expectedVisibleCount, viewModel.VisibleDevices.Count);
        Assert.AreEqual(deviceCount, viewModel.CatalogDeviceCount);
        Assert.AreEqual(1, viewModel.CurrentPage);
    }

    [TestMethod]
    public async Task Page1_StartsOnlyVisibleItems()
    {
        (FakeDeviceStoreService store, FakeTracker tracker, FakeSessionFactory factory) =
            CreateFixture(19);
        await using ViewMultipleDevicesViewModel viewModel = CreateViewModel(
            store,
            tracker,
            factory);

        await viewModel.InitializeAsync();
        await viewModel.WaitForPendingOperationsAsync();

        Assert.IsTrue(factory.Sessions.Take(8).All(session => session.StartCount == 1));
        Assert.IsTrue(factory.Sessions.Skip(8).All(session => session.StartCount == 0));
    }

    [TestMethod]
    public async Task Initialize_DefaultZoom_Is100()
    {
        (FakeDeviceStoreService store, FakeTracker tracker, FakeSessionFactory factory) =
            CreateFixture(1);
        await using ViewMultipleDevicesViewModel viewModel = CreateViewModel(
            store,
            tracker,
            factory);

        await viewModel.InitializeAsync();

        Assert.AreEqual(100, viewModel.ZoomPercent);
    }

    [TestMethod]
    public async Task ZoomOut_From100_Is75()
    {
        await using ViewMultipleDevicesViewModel viewModel = await CreateInitializedViewModelAsync(1);

        viewModel.ZoomOutCommand.Execute(null);

        Assert.AreEqual(75, viewModel.ZoomPercent);
    }

    [TestMethod]
    public async Task ZoomIn_From100_Is125()
    {
        await using ViewMultipleDevicesViewModel viewModel = await CreateInitializedViewModelAsync(1);

        viewModel.ZoomInCommand.Execute(null);

        Assert.AreEqual(125, viewModel.ZoomPercent);
    }

    [TestMethod]
    public async Task RepeatedZoomIn_StopsAt300()
    {
        await using ViewMultipleDevicesViewModel viewModel = await CreateInitializedViewModelAsync(1);

        for (int index = 0; index < 20; index++)
            viewModel.ZoomInCommand.Execute(null);

        Assert.AreEqual(300, viewModel.ZoomPercent);
        Assert.IsFalse(viewModel.ZoomInCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task RepeatedZoomOut_StopsAt50()
    {
        await using ViewMultipleDevicesViewModel viewModel = await CreateInitializedViewModelAsync(1);

        for (int index = 0; index < 20; index++)
            viewModel.ZoomOutCommand.Execute(null);

        Assert.AreEqual(50, viewModel.ZoomPercent);
        Assert.IsFalse(viewModel.ZoomOutCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task Fit_Sets100()
    {
        await using ViewMultipleDevicesViewModel viewModel = await CreateInitializedViewModelAsync(1);

        viewModel.ZoomInCommand.Execute(null);
        viewModel.ZoomInCommand.Execute(null);
        viewModel.ZoomInCommand.Execute(null);
        viewModel.ZoomInCommand.Execute(null);
        viewModel.FitCommand.Execute(null);

        Assert.AreEqual(100, viewModel.ZoomPercent);
    }

    [TestMethod]
    public async Task ViewportResize_RetainsCurrentZoomAndRecalculatesWidth()
    {
        await using ViewMultipleDevicesViewModel viewModel = await CreateInitializedViewModelAsync(1);

        viewModel.UpdateViewport(1920, 900, 12, 12);
        for (int index = 0; index < 4; index++)
            viewModel.ZoomInCommand.Execute(null);
        double widthAt1920 = viewModel.VisibleDevices.Single().TileWidth;

        viewModel.UpdateViewport(1280, 900, 12, 12);

        Assert.AreEqual(200, viewModel.ZoomPercent);
        Assert.AreNotEqual(widthAt1920, viewModel.VisibleDevices.Single().TileWidth);
        Assert.IsTrue(viewModel.VisibleDevices.Single().TileWidth < widthAt1920);
    }

    [TestMethod]
    public async Task ViewportResize_DoesNotRestartSessions()
    {
        (FakeDeviceStoreService store, FakeTracker tracker, FakeSessionFactory factory) =
            CreateFixture(1);
        await using ViewMultipleDevicesViewModel viewModel = CreateViewModel(
            store,
            tracker,
            factory);

        await viewModel.InitializeAsync();
        FakeViewDeviceSession session = factory.Sessions.Single();
        int createCount = factory.CreateCount;
        int startCount = session.StartCount;

        viewModel.UpdateViewport(1280, 720, 12, 12);
        viewModel.UpdateViewport(1920, 1080, 12, 12);

        Assert.AreEqual(createCount, factory.CreateCount);
        Assert.AreEqual(startCount, session.StartCount);
        Assert.AreEqual(0, session.StopCount);
    }

    [TestMethod]
    public async Task Rotation_DoesNotRestartSessions()
    {
        (FakeDeviceStoreService store, FakeTracker tracker, FakeSessionFactory factory) =
            CreateFixture(1);
        await using ViewMultipleDevicesViewModel viewModel = CreateViewModel(
            store,
            tracker,
            factory);

        await viewModel.InitializeAsync();
        FakeViewDeviceSession session = factory.Sessions.Single();
        session.RaiseContentSize(1280, 720);

        Assert.AreEqual(1, factory.CreateCount);
        Assert.AreEqual(1, session.StartCount);
        Assert.AreEqual(0, session.StopCount);
    }

    [TestMethod]
    public async Task Zoom_DoesNotRestartVisibleSessions()
    {
        (FakeDeviceStoreService store, FakeTracker tracker, FakeSessionFactory factory) =
            CreateFixture(1);
        await using ViewMultipleDevicesViewModel viewModel = CreateViewModel(
            store,
            tracker,
            factory);

        await viewModel.InitializeAsync();
        viewModel.UpdateViewport(1600, 900, 12, 12);
        FakeViewDeviceSession session = factory.Sessions.Single();
        double widthAt100 = viewModel.VisibleDevices.Single().TileWidth;
        int createCount = factory.CreateCount;
        int startCount = session.StartCount;
        int stopCount = session.StopCount;
        int disposeCount = session.DisposeCount;

        viewModel.ZoomInCommand.Execute(null);
        viewModel.ZoomInCommand.Execute(null);
        viewModel.ZoomInCommand.Execute(null);
        viewModel.ZoomInCommand.Execute(null);
        viewModel.ZoomInCommand.Execute(null);
        viewModel.ZoomInCommand.Execute(null);
        viewModel.ZoomInCommand.Execute(null);
        viewModel.ZoomInCommand.Execute(null);
        viewModel.ZoomInCommand.Execute(null);
        viewModel.ZoomInCommand.Execute(null);
        double widthAt300 = viewModel.VisibleDevices.Single().TileWidth;
        viewModel.ZoomOutCommand.Execute(null);
        viewModel.FitCommand.Execute(null);

        Assert.AreEqual(createCount, factory.CreateCount);
        Assert.AreEqual(startCount, session.StartCount);
        Assert.AreEqual(stopCount, session.StopCount);
        Assert.AreEqual(disposeCount, session.DisposeCount);
        Assert.AreNotEqual(widthAt100, widthAt300);
        Assert.AreEqual(widthAt100, viewModel.VisibleDevices.Single().TileWidth, 0.000001);
    }

    [TestMethod]
    public async Task WidthAndWrapMath_UsesFourColumnsAt100AndCapsAt300()
    {
        await using ViewMultipleDevicesViewModel viewModel = await CreateInitializedViewModelAsync(1);

        viewModel.UpdateViewport(1600, 900, 12, 12);
        double widthAt100 = viewModel.VisibleDevices.Single().TileWidth;
        viewModel.ZoomInCommand.Execute(null);
        viewModel.ZoomInCommand.Execute(null);
        viewModel.ZoomInCommand.Execute(null);
        viewModel.ZoomInCommand.Execute(null);
        viewModel.ZoomInCommand.Execute(null);
        viewModel.ZoomInCommand.Execute(null);
        viewModel.ZoomInCommand.Execute(null);
        viewModel.ZoomInCommand.Execute(null);
        viewModel.ZoomInCommand.Execute(null);
        viewModel.ZoomInCommand.Execute(null);
        double widthAt300 = viewModel.VisibleDevices.Single().TileWidth;

        Assert.AreEqual(388, widthAt100, 0.000001);
        Assert.IsTrue(widthAt300 > widthAt100);
        Assert.IsTrue(widthAt300 + 12 <= 1600.000001);

        viewModel.UpdateViewport(1280, 720, 12, 12);

        Assert.AreEqual(300, viewModel.ZoomPercent);
        Assert.IsTrue(viewModel.VisibleDevices.Single().TileWidth < widthAt300);
        Assert.IsTrue(viewModel.VisibleDevices.Single().TileWidth + 12 <= 1280.000001);
    }

    [TestMethod]
    public async Task CatalogShrink_ClampsCurrentPageToOne()
    {
        (FakeDeviceStoreService store, FakeTracker tracker, FakeSessionFactory factory) =
            CreateFixture(19);
        await using ViewMultipleDevicesViewModel viewModel = CreateViewModel(
            store,
            tracker,
            factory);

        await viewModel.InitializeAsync();
        viewModel.NextPageCommand.Execute(null);
        await viewModel.WaitForPendingOperationsAsync();
        viewModel.NextPageCommand.Execute(null);
        await viewModel.WaitForPendingOperationsAsync();
        Assert.AreEqual(3, viewModel.CurrentPage);

        tracker.SetDevices(CreateAdbDevices(5));
        tracker.SetHealth(AdbDeviceTrackerHealth.Reconnecting);
        await viewModel.WaitForPendingOperationsAsync();
        tracker.SetHealth(AdbDeviceTrackerHealth.Connected);
        await viewModel.WaitForPendingOperationsAsync();

        Assert.AreEqual(5, viewModel.CatalogDeviceCount);
        Assert.AreEqual(1, viewModel.CurrentPage);
        Assert.AreEqual(5, viewModel.VisibleDevices.Count);
    }

    [TestMethod]
    public async Task PageNavigation_StopsOldPageBeforeStartingNewPage()
    {
        (FakeDeviceStoreService store, FakeTracker tracker, FakeSessionFactory factory) =
            CreateFixture(19);
        await using ViewMultipleDevicesViewModel viewModel = CreateViewModel(
            store,
            tracker,
            factory);

        await viewModel.InitializeAsync();
        FakeViewDeviceSession[] firstPageSessions = factory.Sessions.Take(8).ToArray();

        viewModel.NextPageCommand.Execute(null);
        await viewModel.WaitForPendingOperationsAsync();

        Assert.IsTrue(factory.MaxActiveSessionCount <= 8);
        Assert.IsTrue(firstPageSessions.All(session => !session.IsActive));
        Assert.IsTrue(factory.Sessions.Skip(8).Take(8).All(session => session.IsActive));

        int lastOldStop = firstPageSessions
            .Select(session => factory.LifecycleEvents.LastIndexOf($"stop:{session.Serial}"))
            .Max();
        int firstNewStart = factory.Sessions
            .Skip(8)
            .Take(8)
            .Select(session => factory.LifecycleEvents.IndexOf($"start:{session.Serial}"))
            .Min();
        Assert.IsTrue(lastOldStop < firstNewStart);
    }

    [TestMethod]
    public async Task NavigatePage2_StopsPage1Sessions()
    {
        (FakeDeviceStoreService store, FakeTracker tracker, FakeSessionFactory factory) =
            CreateFixture(16);
        await using ViewMultipleDevicesViewModel viewModel = CreateViewModel(
            store,
            tracker,
            factory);

        await viewModel.InitializeAsync();
        viewModel.NextPageCommand.Execute(null);
        await viewModel.WaitForPendingOperationsAsync();

        Assert.AreEqual(2, viewModel.CurrentPage);
        Assert.IsTrue(factory.Sessions.Take(8).All(session => session.StopCount == 1));
    }

    [TestMethod]
    public async Task NavigatePage2_StartsOnlyPage2Sessions()
    {
        (FakeDeviceStoreService store, FakeTracker tracker, FakeSessionFactory factory) =
            CreateFixture(16);
        await using ViewMultipleDevicesViewModel viewModel = CreateViewModel(
            store,
            tracker,
            factory);

        await viewModel.InitializeAsync();
        viewModel.NextPageCommand.Execute(null);
        await viewModel.WaitForPendingOperationsAsync();

        Assert.IsTrue(factory.Sessions.Skip(8).Take(8).All(session => session.StartCount == 1));
        Assert.IsTrue(factory.Sessions.Skip(8).Take(8).All(session => session.IsActive));
    }

    [TestMethod]
    public async Task NavigateBack_CreatesFreshSessions()
    {
        (FakeDeviceStoreService store, FakeTracker tracker, FakeSessionFactory factory) =
            CreateFixture(16);
        await using ViewMultipleDevicesViewModel viewModel = CreateViewModel(
            store,
            tracker,
            factory);

        await viewModel.InitializeAsync();
        viewModel.NextPageCommand.Execute(null);
        await viewModel.WaitForPendingOperationsAsync();
        FakeViewDeviceSession[] firstPage = factory.Sessions.Take(8).ToArray();
        viewModel.PreviousPageCommand.Execute(null);
        await viewModel.WaitForPendingOperationsAsync();

        Assert.AreEqual(24, factory.CreateCount);
        Assert.IsTrue(factory.Sessions.Skip(16).Take(8).All(session => session.StartCount == 1));
        Assert.IsTrue(firstPage.All(session => session.StopCount >= 1));
    }

    [TestMethod]
    public async Task NavigatePageAndBack_PreservesCatalogItemInstances()
    {
        (FakeDeviceStoreService store, FakeTracker tracker, FakeSessionFactory factory) =
            CreateFixture(16);
        await using ViewMultipleDevicesViewModel viewModel = CreateViewModel(
            store,
            tracker,
            factory);

        await viewModel.InitializeAsync();
        ViewMultipleDeviceItemViewModel[] firstPageItems = viewModel.VisibleDevices.ToArray();
        viewModel.NextPageCommand.Execute(null);
        await viewModel.WaitForPendingOperationsAsync();
        viewModel.PreviousPageCommand.Execute(null);
        await viewModel.WaitForPendingOperationsAsync();

        CollectionAssert.AreEqual(firstPageItems, viewModel.VisibleDevices.ToArray());
        Assert.AreEqual(16, viewModel.CatalogDeviceCount);
    }

    [TestMethod]
    public async Task PageNavigation_DoesNotReloadDeviceStore()
    {
        (FakeDeviceStoreService store, FakeTracker tracker, FakeSessionFactory factory) =
            CreateFixture(19);
        await using ViewMultipleDevicesViewModel viewModel = CreateViewModel(
            store,
            tracker,
            factory);
        await viewModel.InitializeAsync();

        int loadCountBeforeNavigation = store.LoadCount;
        viewModel.NextPageCommand.Execute(null);
        await viewModel.WaitForPendingOperationsAsync();

        Assert.AreEqual(loadCountBeforeNavigation, store.LoadCount);
        Assert.AreEqual(2, viewModel.CurrentPage);
        CollectionAssert.AreEqual(
            CreateSerials(9, 16),
            viewModel.VisibleDevices.Select(item => item.Serial).ToArray());

        viewModel.NextPageCommand.Execute(null);
        await viewModel.WaitForPendingOperationsAsync();

        Assert.AreEqual(3, viewModel.CurrentPage);
    }

    [TestMethod]
    public async Task RapidNavigation_ReconcilesToNewestRequestedPage()
    {
        (FakeDeviceStoreService store, FakeTracker tracker, FakeSessionFactory factory) =
            CreateFixture(19);
        await using ViewMultipleDevicesViewModel viewModel = CreateViewModel(
            store,
            tracker,
            factory);
        await viewModel.InitializeAsync();

        viewModel.NextPageCommand.Execute(null);
        viewModel.NextPageCommand.Execute(null);
        viewModel.PreviousPageCommand.Execute(null);
        viewModel.NextPageCommand.Execute(null);
        await viewModel.WaitForPendingOperationsAsync();

        Assert.AreEqual(3, viewModel.CurrentPage);
        CollectionAssert.AreEqual(
            CreateSerials(17, 19),
            viewModel.VisibleDevices.Select(item => item.Serial).ToArray());
        Assert.IsTrue(factory.MaxActiveSessionCount <= 8);
        Assert.IsTrue(factory.Sessions.Where(session => session.StartCount > 0)
            .All(session => session.Serial is "SERIAL-01"
                or "SERIAL-02"
                or "SERIAL-03"
                or "SERIAL-04"
                or "SERIAL-05"
                or "SERIAL-06"
                or "SERIAL-07"
                or "SERIAL-08"
                or "SERIAL-17"
                or "SERIAL-18"
                or "SERIAL-19"));
    }

    [TestMethod]
    public async Task TrackerOnlineEvent_AddsNewSavedDevice()
    {
        FakeDeviceStoreService store = new(CreateStoredDevices(1));
        FakeTracker tracker = new();
        FakeSessionFactory factory = new();
        await using ViewMultipleDevicesViewModel viewModel = CreateViewModel(
            store,
            tracker,
            factory);
        await viewModel.InitializeAsync();

        tracker.SetDevice(new AdbDevice("SERIAL-01", AdbDeviceStatus.Online));
        await viewModel.WaitForPendingOperationsAsync();

        Assert.AreEqual(1, viewModel.CatalogDeviceCount);
        Assert.AreEqual("SERIAL-01", viewModel.VisibleDevices.Single().Serial);
    }

    [TestMethod]
    public async Task Initialize_OnlyPublishesSavedOnlineDevices()
    {
        FakeDeviceStoreService store = new(CreateStoredDevices(3));
        FakeTracker tracker = new(
        [
            new AdbDevice("SERIAL-01", AdbDeviceStatus.Online),
            new AdbDevice("SERIAL-02", AdbDeviceStatus.Offline),
            new AdbDevice("SERIAL-03", AdbDeviceStatus.Unauthorized)
        ]);
        await using ViewMultipleDevicesViewModel viewModel = CreateViewModel(
            store,
            tracker,
            new FakeSessionFactory());

        await viewModel.InitializeAsync();

        CollectionAssert.AreEqual(
            new[] { "SERIAL-01" },
            viewModel.OnlineDevices.Select(item => item.Serial).ToArray());
        CollectionAssert.AreEqual(
            new[] { "SERIAL-01" },
            viewModel.VisibleDevices.Select(item => item.Serial).ToArray());
    }

    [TestMethod]
    public async Task TrackerEventDuringRefresh_RunsAnotherRefreshPass()
    {
        FakeDeviceStoreService store = new(CreateStoredDevices(2));
        FakeTracker tracker = new([new AdbDevice("SERIAL-01", AdbDeviceStatus.Online)]);
        FakeSessionFactory factory = new();
        await using ViewMultipleDevicesViewModel viewModel = CreateViewModel(
            store,
            tracker,
            factory);
        await viewModel.InitializeAsync();

        TaskCompletionSource loadGate = NewSignal();
        store.LoadGate = loadGate;
        tracker.SetDevice(new AdbDevice("SERIAL-02", AdbDeviceStatus.Online));
        await store.WaitForLoadAsync(2);
        tracker.RaiseDeviceStateChanged("SERIAL-02");
        loadGate.TrySetResult();
        await viewModel.WaitForPendingOperationsAsync();

        Assert.IsTrue(store.LoadCount >= 3);
        Assert.AreEqual(1, store.MaxConcurrentLoads);
        Assert.AreEqual(2, viewModel.CatalogDeviceCount);
    }

    [TestMethod]
    public async Task ManyTrackerEvents_AreCoalescedWithoutParallelRefreshes()
    {
        FakeDeviceStoreService store = new(CreateStoredDevices(1));
        FakeTracker tracker = new();
        FakeSessionFactory factory = new();
        await using ViewMultipleDevicesViewModel viewModel = CreateViewModel(
            store,
            tracker,
            factory);
        await viewModel.InitializeAsync();

        TaskCompletionSource loadGate = NewSignal();
        store.LoadGate = loadGate;
        tracker.SetDevice(new AdbDevice("SERIAL-01", AdbDeviceStatus.Online));
        await store.WaitForLoadAsync(2);
        for (int index = 0; index < 20; index++)
            tracker.RaiseDeviceStateChanged("SERIAL-01");
        loadGate.TrySetResult();
        await viewModel.WaitForPendingOperationsAsync();

        Assert.IsTrue(store.LoadCount <= 3);
        Assert.AreEqual(1, store.MaxConcurrentLoads);
        Assert.AreEqual(1, viewModel.CatalogDeviceCount);
    }

    [TestMethod]
    public async Task TrackerReconnecting_ClearsCatalogAndDisposesCurrentSession()
    {
        (FakeDeviceStoreService store, FakeTracker tracker, FakeSessionFactory factory) =
            CreateFixture(1);
        await using ViewMultipleDevicesViewModel viewModel = CreateViewModel(
            store,
            tracker,
            factory);
        await viewModel.InitializeAsync();
        tracker.SetHealth(AdbDeviceTrackerHealth.Reconnecting);
        await viewModel.WaitForPendingOperationsAsync();

        Assert.AreEqual(0, viewModel.CatalogDeviceCount);
        Assert.IsEmpty(viewModel.OnlineDevices);
        Assert.IsEmpty(viewModel.VisibleDevices);
        Assert.IsFalse(factory.Sessions.Single().IsActive);
        Assert.AreEqual(1, factory.Sessions.Single().StopCount);
        Assert.AreEqual(1, factory.Sessions.Single().DisposeCount);
    }

    [TestMethod]
    public async Task TrackerOffline_RemovesOnlyAffectedItemAndPreservesHealthyItems()
    {
        (FakeDeviceStoreService store, FakeTracker tracker, FakeSessionFactory factory) =
            CreateFixture(3);
        await using ViewMultipleDevicesViewModel viewModel = CreateViewModel(
            store,
            tracker,
            factory);
        await viewModel.InitializeAsync();
        ViewMultipleDeviceItemViewModel[] initialItems = viewModel.OnlineDevices.ToArray();

        tracker.SetDevice(new AdbDevice("SERIAL-02", AdbDeviceStatus.Offline));
        await viewModel.WaitForPendingOperationsAsync();

        CollectionAssert.AreEqual(
            new[] { "SERIAL-01", "SERIAL-03" },
            viewModel.OnlineDevices.Select(item => item.Serial).ToArray());
        Assert.AreSame(initialItems[0], viewModel.OnlineDevices[0]);
        Assert.AreSame(initialItems[2], viewModel.OnlineDevices[1]);
        Assert.AreEqual(0, factory.Sessions[0].StopCount);
        Assert.AreEqual(0, factory.Sessions[0].DisposeCount);
        Assert.AreEqual(1, factory.Sessions[1].StopCount);
        Assert.AreEqual(1, factory.Sessions[1].DisposeCount);
        Assert.AreEqual(0, factory.Sessions[2].StopCount);
        Assert.AreEqual(0, factory.Sessions[2].DisposeCount);
    }

    [TestMethod]
    public async Task TrackerConnectedAfterReconnect_RefreshesCatalog()
    {
        (FakeDeviceStoreService store, FakeTracker tracker, FakeSessionFactory factory) =
            CreateFixture(1);
        await using ViewMultipleDevicesViewModel viewModel = CreateViewModel(
            store,
            tracker,
            factory);
        await viewModel.InitializeAsync();
        tracker.SetHealth(AdbDeviceTrackerHealth.Reconnecting);
        await viewModel.WaitForPendingOperationsAsync();
        tracker.SetHealth(AdbDeviceTrackerHealth.Connected);
        await viewModel.WaitForPendingOperationsAsync();

        Assert.AreEqual(1, viewModel.CatalogDeviceCount);
        Assert.AreEqual(1, viewModel.VisibleDevices.Count);
        Assert.IsTrue(viewModel.VisibleDevices.Single().IsRunning);
        Assert.IsTrue(factory.CreateCount >= 2);
    }

    [TestMethod]
    public async Task TrackerOnline_CanStartFreshSession()
    {
        (FakeDeviceStoreService store, FakeTracker tracker, FakeSessionFactory factory) =
            CreateFixture(1);
        await using ViewMultipleDevicesViewModel viewModel = CreateViewModel(
            store,
            tracker,
            factory);
        await viewModel.InitializeAsync();
        tracker.SetHealth(AdbDeviceTrackerHealth.Reconnecting);
        await viewModel.WaitForPendingOperationsAsync();
        tracker.SetHealth(AdbDeviceTrackerHealth.Connected);
        await viewModel.WaitForPendingOperationsAsync();

        Assert.IsTrue(viewModel.VisibleDevices.Single().IsRunning);
        Assert.IsTrue(factory.CreateCount >= 2);
        Assert.IsTrue(factory.Sessions.Last().StartCount == 1);
    }

    [TestMethod]
    public async Task TrackerConnectedAfterReconnect_ReconcilesCurrentOnlineSnapshot()
    {
        FakeDeviceStoreService store = new(CreateStoredDevices(3));
        FakeTracker tracker = new(
        [
            new AdbDevice("SERIAL-01", AdbDeviceStatus.Online),
            new AdbDevice("SERIAL-02", AdbDeviceStatus.Online),
            new AdbDevice("SERIAL-03", AdbDeviceStatus.Online)
        ]);
        FakeSessionFactory factory = new();
        await using ViewMultipleDevicesViewModel viewModel = CreateViewModel(store, tracker, factory);
        await viewModel.InitializeAsync();

        tracker.SetDevices(
        [
            new AdbDevice("SERIAL-01", AdbDeviceStatus.Online),
            new AdbDevice("SERIAL-03", AdbDeviceStatus.Online)
        ]);
        tracker.SetHealth(AdbDeviceTrackerHealth.Reconnecting);
        await viewModel.WaitForPendingOperationsAsync();
        tracker.SetHealth(AdbDeviceTrackerHealth.Connected);
        await viewModel.WaitForPendingOperationsAsync();

        CollectionAssert.AreEqual(
            new[] { "SERIAL-01", "SERIAL-03" },
            viewModel.OnlineDevices.Select(item => item.Serial).ToArray());
    }

    [TestMethod]
    public async Task DeviceRemoval_RefillsVisiblePageWithoutRestartingHealthySessions()
    {
        (FakeDeviceStoreService store, FakeTracker tracker, FakeSessionFactory factory) =
            CreateFixture(9);
        await using ViewMultipleDevicesViewModel viewModel = CreateViewModel(store, tracker, factory);
        await viewModel.InitializeAsync();

        tracker.SetDevice(new AdbDevice("SERIAL-04", AdbDeviceStatus.Offline));
        await viewModel.WaitForPendingOperationsAsync();

        CollectionAssert.AreEqual(
            new[] { "SERIAL-01", "SERIAL-02", "SERIAL-03", "SERIAL-05", "SERIAL-06", "SERIAL-07", "SERIAL-08", "SERIAL-09" },
            viewModel.VisibleDevices.Select(item => item.Serial).ToArray());
        Assert.AreEqual(1, factory.Sessions[8].StartCount);
        Assert.AreEqual(0, factory.Sessions[0].StopCount);
        Assert.AreEqual(0, factory.Sessions[1].StopCount);
        Assert.AreEqual(0, factory.Sessions[2].StopCount);
        Assert.AreEqual(1, factory.Sessions[3].StopCount);
    }

    [TestMethod]
    public async Task OpenViewDevice_DeviceRemovedBeforeExecution_DoesNotOpenWindow()
    {
        (FakeDeviceStoreService store, FakeTracker tracker, FakeSessionFactory factory) =
            CreateFixture(1);
        NoOpViewDeviceWindowService windowService = new();
        await using ViewMultipleDevicesViewModel viewModel = CreateViewModel(
            store,
            tracker,
            factory,
            viewDeviceWindowService: windowService);

        await viewModel.InitializeAsync();
        ViewMultipleDeviceItemViewModel item = viewModel.VisibleDevices.Single();
        tracker.SetDevice(new AdbDevice("SERIAL-01", AdbDeviceStatus.Offline));

        await item.OpenViewDeviceCommand.ExecuteAsync(null);

        Assert.AreEqual(0, windowService.OpenCount);
    }

    [TestMethod]
    public async Task PresentationLease_SuspendsAndResumesVisibleMultiItem()
    {
        (FakeDeviceStoreService store, FakeTracker tracker, FakeSessionFactory factory) =
            CreateFixture(1);
        ViewDevicePresentationCoordinator coordinator = new();
        await using ViewMultipleDevicesViewModel viewModel = CreateViewModel(
            store,
            tracker,
            factory,
            presentationCoordinator: coordinator);

        await viewModel.InitializeAsync();
        ViewMultipleDeviceItemViewModel item = viewModel.VisibleDevices.Single();
        FakeViewDeviceSession firstSession = factory.Sessions.Single();

        IAsyncDisposable lease = await coordinator.AcquireDedicatedViewAsync("SERIAL-01");
        Assert.IsTrue(item.IsDedicatedView);
        Assert.AreEqual(1, firstSession.StopCount);
        Assert.AreEqual(1, firstSession.DisposeCount);

        await DisposeLeaseAsync(lease);

        Assert.IsTrue(item.IsRunning);
        Assert.AreEqual(2, factory.CreateCount);
        Assert.AreEqual(1, factory.Sessions[1].StartCount);
    }

    [TestMethod]
    public async Task Deactivate_ConcurrentDedicatedOpen_ReleasesMultiLeaseBeforeDedicatedContinues()
    {
        FakeDeviceStoreService store = new(CreateStoredDevices(1));
        FakeTracker tracker = new(CreateAdbDevices(1));
        FakeViewDeviceSession session = new("SERIAL-01");
        FakeSessionFactory factory = new(session);
        ViewDevicePresentationCoordinator coordinator = new();
        await using ViewMultipleDevicesViewModel viewModel = CreateViewModel(
            store,
            tracker,
            factory,
            presentationCoordinator: coordinator);

        await viewModel.InitializeAsync();
        TaskCompletionSource stopStarted = NewSignal();
        TaskCompletionSource stopGate = NewSignal();
        session.StopStarted = stopStarted;
        session.StopGate = stopGate;

        Task deactivate = viewModel.DeactivateAsync();
        await stopStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Task<IAsyncDisposable> acquire = coordinator.AcquireDedicatedViewAsync("SERIAL-01");
        Assert.IsFalse(acquire.IsCompleted);

        stopGate.TrySetResult();
        await deactivate;
        IAsyncDisposable lease = await acquire;
        await lease.DisposeAsync();
    }

    [TestMethod]
    public async Task DedicatedLeaseOpenedBeforeMultiActivation_DoesNotStartUntilReleased()
    {
        (FakeDeviceStoreService store, FakeTracker tracker, FakeSessionFactory factory) =
            CreateFixture(1);
        ViewDevicePresentationCoordinator coordinator = new();
        IAsyncDisposable lease = await coordinator.AcquireDedicatedViewAsync("SERIAL-01");
        await using ViewMultipleDevicesViewModel viewModel = CreateViewModel(
            store,
            tracker,
            factory,
            presentationCoordinator: coordinator);

        await viewModel.InitializeAsync();
        ViewMultipleDeviceItemViewModel item = viewModel.VisibleDevices.Single();
        Assert.IsTrue(item.IsDedicatedView);
        Assert.AreEqual(0, factory.CreateCount);

        await DisposeLeaseAsync(lease);

        Assert.IsTrue(item.IsRunning);
        Assert.AreEqual(1, factory.CreateCount);
        Assert.AreEqual(1, factory.Sessions.Single().StartCount);
    }

    [TestMethod]
    public async Task Deactivate_CleansSessionsRetriesAndOperations()
    {
        (FakeDeviceStoreService store, FakeTracker tracker, FakeSessionFactory factory) =
            CreateFixture(8);
        await using ViewMultipleDevicesViewModel viewModel = CreateViewModel(
            store,
            tracker,
            factory);
        await viewModel.InitializeAsync();
        FakeViewDeviceSession[] sessions = factory.Sessions.ToArray();

        TaskCompletionSource loadGate = NewSignal();
        store.LoadGate = loadGate;
        tracker.SetHealth(AdbDeviceTrackerHealth.Reconnecting);
        await viewModel.WaitForPendingOperationsAsync();
        tracker.SetHealth(AdbDeviceTrackerHealth.Connected);
        await store.WaitForLoadAsync(2);

        await viewModel.DeactivateAsync();

        Assert.IsEmpty(viewModel.VisibleDevices);
        Assert.IsEmpty(viewModel.OnlineDevices);
        Assert.AreEqual(0, viewModel.OnlineDeviceCount);
        Assert.IsTrue(sessions.All(session => session.StopCount >= 1));
        Assert.IsTrue(sessions.All(session => session.DisposeCount >= 1));
        Assert.AreEqual(0, factory.ActiveSessionCount);
    }

    private static ViewMultipleDevicesViewModel CreateViewModel(
        FakeDeviceStoreService store,
        FakeTracker tracker,
        FakeSessionFactory factory,
        FakeLocalizationService? localization = null,
        IViewDeviceWindowService? viewDeviceWindowService = null,
        IViewDevicePresentationCoordinator? presentationCoordinator = null)
    {
        IViewDeviceWindowService windowService =
            viewDeviceWindowService ?? new NoOpViewDeviceWindowService();
        IViewDevicePresentationCoordinator coordinator =
            presentationCoordinator ?? new ViewDevicePresentationCoordinator();
        return new ViewMultipleDevicesViewModel(
            store,
            tracker,
            factory,
            new FakeClipboardService(),
            localization ?? new FakeLocalizationService(),
            new ImmediateUiDispatcher(),
            NullLoggerFactory.Instance,
            NullLogger<ViewMultipleDevicesViewModel>.Instance,
            windowService,
            coordinator,
            metadataChangeNotifier: null);
    }

    private static async Task<ViewMultipleDevicesViewModel> CreateInitializedViewModelAsync(
        int deviceCount)
    {
        (FakeDeviceStoreService store, FakeTracker tracker, FakeSessionFactory factory) =
            CreateFixture(deviceCount);
        ViewMultipleDevicesViewModel viewModel = CreateViewModel(store, tracker, factory);
        await viewModel.InitializeAsync();
        await viewModel.WaitForPendingOperationsAsync();
        return viewModel;
    }

    private static (FakeDeviceStoreService Store, FakeTracker Tracker, FakeSessionFactory Factory)
        CreateFixture(int deviceCount)
    {
        return (
            new FakeDeviceStoreService(CreateStoredDevices(deviceCount)),
            new FakeTracker(CreateAdbDevices(deviceCount)),
            new FakeSessionFactory());
    }

    private static StoredDeviceConfig[] CreateStoredDevices(int count)
    {
        return Enumerable.Range(1, count)
            .Select(index => new StoredDeviceConfig
            {
                Serial = Serial(index),
                Name = $"Device {index}"
            })
            .ToArray();
    }

    private static AdbDevice[] CreateAdbDevices(int count)
    {
        return Enumerable.Range(1, count)
            .Select(index => new AdbDevice(Serial(index), AdbDeviceStatus.Online))
            .ToArray();
    }

    private static string[] CreateSerials(int first, int last)
    {
        return Enumerable.Range(first, last - first + 1)
            .Select(Serial)
            .ToArray();
    }

    private static string Serial(int index)
    {
        return $"SERIAL-{index:00}";
    }

    private static TaskCompletionSource NewSignal()
    {
        return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static int GetPageSize()
    {
        return ViewMultipleDevicesViewModel.PageSize;
    }

    private static Task DisposeLeaseAsync(IAsyncDisposable lease)
    {
        return lease.DisposeAsync().AsTask();
    }
}
