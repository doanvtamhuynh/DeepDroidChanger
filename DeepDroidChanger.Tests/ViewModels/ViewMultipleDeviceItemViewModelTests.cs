using System.ComponentModel;
using DeepDroidChanger.Models;
using DeepDroidChanger.Tests.ViewModels;
using DeepDroidChanger.ViewDevices.Models;
using DeepDroidChanger.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace DeepDroidChanger.Tests.ViewModels;

[TestClass]
public sealed class ViewMultipleDeviceItemViewModelTests
{
    private const string Serial = "SERIAL-1";

    [TestMethod]
    public async Task NativeHostFailure_FirstFailure_SchedulesAttempt1()
    {
        List<int> attempts = [];
        FakeTracker tracker = CreateOnlineTracker();
        FakeSessionFactory factory = new();
        await using ViewMultipleDeviceItemViewModel item = CreateItem(
            tracker,
            factory,
            retryRequest: (_, attempt) => attempts.Add(attempt),
            retryDelay: static (_, _) => Task.CompletedTask);

        await item.StartAsync(CancellationToken.None);
        await item.HandleNativeHostFailureAsync(
            new InvalidOperationException("host"),
            item.NativeWindowHandle);

        CollectionAssert.AreEqual(new[] { 1 }, attempts);
        Assert.AreEqual(1, item.RetryAttempt);
    }

    [TestMethod]
    public async Task NativeHostFailure_RepeatedFailure_AdvancesAttempts()
    {
        List<int> attempts = [];
        FakeTracker tracker = CreateOnlineTracker();
        await using ViewMultipleDeviceItemViewModel item = CreateItem(
            tracker,
            new FakeSessionFactory(),
            retryRequest: (_, attempt) => attempts.Add(attempt),
            retryDelay: static (_, _) => Task.CompletedTask);

        await item.StartAsync(CancellationToken.None);
        await item.HandleNativeHostFailureAsync(
            new InvalidOperationException("first"),
            item.NativeWindowHandle);
        await item.StartAsync(CancellationToken.None);
        await item.HandleNativeHostFailureAsync(
            new InvalidOperationException("second"),
            item.NativeWindowHandle);
        await item.StartAsync(CancellationToken.None);
        await item.HandleNativeHostFailureAsync(
            new InvalidOperationException("third"),
            item.NativeWindowHandle);

        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, attempts);
        Assert.AreEqual(3, item.RetryAttempt);
    }

    [TestMethod]
    public async Task NativeHostFailure_AfterThirdRetry_DoesNotScheduleFourth()
    {
        List<int> attempts = [];
        FakeTracker tracker = CreateOnlineTracker();
        await using ViewMultipleDeviceItemViewModel item = CreateItem(
            tracker,
            new FakeSessionFactory(),
            retryRequest: (_, attempt) => attempts.Add(attempt),
            retryDelay: static (_, _) => Task.CompletedTask);

        for (int failure = 0; failure < 4; failure++)
        {
            await item.StartAsync(CancellationToken.None);
            await item.HandleNativeHostFailureAsync(
                new InvalidOperationException("host"),
                item.NativeWindowHandle);
        }

        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, attempts);
        Assert.AreEqual(3, item.RetryAttempt);
    }

    [TestMethod]
    public async Task ScrcpyStartSuccess_DoesNotResetHostRetryBeforeAttach()
    {
        List<int> attempts = [];
        FakeTracker tracker = CreateOnlineTracker();
        FakeSessionFactory factory = new();
        await using ViewMultipleDeviceItemViewModel item = CreateItem(
            tracker,
            factory,
            retryRequest: (_, attempt) => attempts.Add(attempt),
            retryDelay: static (_, _) => Task.CompletedTask);

        await item.StartAsync(CancellationToken.None);
        await item.HandleNativeHostFailureAsync(
            new InvalidOperationException("host"),
            item.NativeWindowHandle);
        await item.StartAsync(CancellationToken.None);

        CollectionAssert.AreEqual(new[] { 1 }, attempts);
        Assert.AreEqual(1, item.RetryAttempt);
        Assert.AreEqual(2, factory.CreateCount);
        Assert.IsTrue(item.IsRunning);
    }

    [TestMethod]
    public async Task PendingAttach_DoesNotResetRetryAttempt()
    {
        List<int> attempts = [];
        FakeTracker tracker = CreateOnlineTracker();
        await using ViewMultipleDeviceItemViewModel item = CreateItem(
            tracker,
            new FakeSessionFactory(),
            retryRequest: (_, attempt) => attempts.Add(attempt),
            retryDelay: static (_, _) => Task.CompletedTask);

        await item.StartAsync(CancellationToken.None);
        await item.HandleNativeHostFailureAsync(
            new InvalidOperationException("host"),
            item.NativeWindowHandle);
        await item.StartAsync(CancellationToken.None);

        Assert.AreEqual(1, item.RetryAttempt);
        CollectionAssert.AreEqual(new[] { 1 }, attempts);
        Assert.IsTrue(item.IsRunning);
    }

    [TestMethod]
    public async Task NativeHostAttached_ResetsRetryAttempt()
    {
        List<int> attempts = [];
        FakeTracker tracker = CreateOnlineTracker();
        FakeSessionFactory factory = new();
        await using ViewMultipleDeviceItemViewModel item = CreateItem(
            tracker,
            factory,
            retryRequest: (_, attempt) => attempts.Add(attempt),
            retryDelay: static (_, _) => Task.CompletedTask);

        await item.StartAsync(CancellationToken.None);
        await item.HandleNativeHostFailureAsync(
            new InvalidOperationException("host"),
            item.NativeWindowHandle);
        await item.StartAsync(CancellationToken.None);
        IntPtr currentHandle = item.NativeWindowHandle;

        item.NotifyNativeHostAttached(currentHandle);

        CollectionAssert.AreEqual(new[] { 1 }, attempts);
        Assert.AreEqual(0, item.RetryAttempt);
        Assert.IsTrue(item.IsRunning);
    }

    [TestMethod]
    public async Task Disconnect_CancelsPendingRetry()
    {
        TaskCompletionSource cancellationObserved = NewSignal();
        List<int> attempts = [];
        FakeTracker tracker = CreateOnlineTracker();
        await using ViewMultipleDeviceItemViewModel item = CreateItem(
            tracker,
            new FakeSessionFactory(),
            retryRequest: (_, attempt) => attempts.Add(attempt),
            retryDelay: CreateCancellableDelay(cancellationObserved));

        await item.StartAsync(CancellationToken.None);
        await item.HandleNativeHostFailureAsync(
            new InvalidOperationException("host"),
            item.NativeWindowHandle);
        await item.StopSessionAsync();
        await cancellationObserved.Task;

        Assert.IsEmpty(attempts);
        Assert.AreEqual(0, item.RetryAttempt);
        Assert.IsTrue(item.IsDisconnected);
    }

    [TestMethod]
    public async Task Dispose_CancelsPendingRetry()
    {
        TaskCompletionSource cancellationObserved = NewSignal();
        List<int> attempts = [];
        FakeTracker tracker = CreateOnlineTracker();
        ViewMultipleDeviceItemViewModel item = CreateItem(
            tracker,
            new FakeSessionFactory(),
            retryRequest: (_, attempt) => attempts.Add(attempt),
            retryDelay: CreateCancellableDelay(cancellationObserved));

        await item.StartAsync(CancellationToken.None);
        await item.HandleNativeHostFailureAsync(
            new InvalidOperationException("host"),
            item.NativeWindowHandle);
        await item.DisposeAsync();
        await cancellationObserved.Task;

        CollectionAssert.AreEqual(Array.Empty<int>(), attempts);
        Assert.AreEqual(0, item.RetryAttempt);
    }

    [TestMethod]
    public async Task NoContentSize_Uses9By16Fallback()
    {
        FakeTracker tracker = CreateOnlineTracker();
        await using ViewMultipleDeviceItemViewModel item = new(
            Serial,
            "Device",
            new FakeSessionFactory(),
            tracker,
            new FakeLocalizationService(),
            new ImmediateUiDispatcher(),
            NullLogger<ViewMultipleDeviceItemViewModel>.Instance);

        Assert.AreEqual(9d / 16d, item.DeviceAspectRatio, 0.000001);
    }

    [TestMethod]
    public async Task ContentSize_1080x2220_UsesActualPortraitAspect()
    {
        FakeTracker tracker = CreateOnlineTracker();
        FakeSessionFactory factory = new(new FakeViewDeviceSession(Serial, 1080, 2220));
        await using ViewMultipleDeviceItemViewModel item = CreateItem(tracker, factory);

        await item.StartAsync(CancellationToken.None);

        Assert.AreEqual(1080d / 2220d, item.DeviceAspectRatio, 0.000001);
    }

    [TestMethod]
    public async Task ContentSize_2220x1080_UsesLandscapeAspect()
    {
        FakeTracker tracker = CreateOnlineTracker();
        FakeSessionFactory factory = new(new FakeViewDeviceSession(Serial, 2220, 1080));
        await using ViewMultipleDeviceItemViewModel item = CreateItem(tracker, factory);

        await item.StartAsync(CancellationToken.None);

        Assert.AreEqual(2220d / 1080d, item.DeviceAspectRatio, 0.000001);
    }

    [TestMethod]
    public async Task ContentSizeChanged_RotationUpdatesAspect()
    {
        FakeTracker tracker = CreateOnlineTracker();
        FakeViewDeviceSession session = new(Serial, 1080, 2220);
        FakeSessionFactory factory = new(session);
        await using ViewMultipleDeviceItemViewModel item = CreateItem(tracker, factory);

        await item.StartAsync(CancellationToken.None);
        int createCount = factory.CreateCount;
        int startCount = session.StartCount;

        session.RaiseContentSize(2220, 1080);

        Assert.AreEqual(2220d / 1080d, item.DeviceAspectRatio, 0.000001);
        Assert.AreEqual(createCount, factory.CreateCount);
        Assert.AreEqual(startCount, session.StartCount);
    }

    [TestMethod]
    public async Task ContentSizeChanged_RotationPublishesOneAtomicAspectNotification()
    {
        FakeTracker tracker = CreateOnlineTracker();
        FakeViewDeviceSession session = new(Serial, 1080, 2220);
        FakeSessionFactory factory = new(session);
        await using ViewMultipleDeviceItemViewModel item = CreateItem(tracker, factory);

        await item.StartAsync(CancellationToken.None);
        List<double> aspectValues = [];
        item.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(ViewMultipleDeviceItemViewModel.DeviceAspectRatio))
                aspectValues.Add(item.DeviceAspectRatio);
        };

        session.RaiseContentSize(2220, 1080);

        Assert.AreEqual(2220d / 1080d, item.DeviceAspectRatio, 0.000001);
        Assert.AreEqual(1, aspectValues.Count);
        Assert.AreNotEqual(1d, aspectValues[0], 0.000001);
        Assert.AreEqual(1, factory.CreateCount);
        Assert.AreEqual(1, session.StartCount);
    }

    [TestMethod]
    public async Task ConnectingToOnline_PublishesIsRunningOnce()
    {
        FakeTracker tracker = CreateOnlineTracker();
        await using ViewMultipleDeviceItemViewModel item = CreateItem(
            tracker,
            new FakeSessionFactory());
        int isRunningNotifications = 0;
        item.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(ViewMultipleDeviceItemViewModel.IsRunning))
                isRunningNotifications++;
        };

        await item.StartAsync(CancellationToken.None);

        Assert.IsTrue(item.IsRunning);
        Assert.AreEqual(1, isRunningNotifications);
    }

    [TestMethod]
    public async Task InvalidContentSize_FallsBackSafely()
    {
        FakeTracker tracker = CreateOnlineTracker();
        FakeViewDeviceSession session = new(Serial, 0, 0);
        FakeSessionFactory factory = new(session);
        await using ViewMultipleDeviceItemViewModel item = CreateItem(tracker, factory);

        await item.StartAsync(CancellationToken.None);
        Assert.AreEqual(9d / 16d, item.DeviceAspectRatio, 0.000001);

        session.RaiseContentSize(1080, 0);
        Assert.AreEqual(9d / 16d, item.DeviceAspectRatio, 0.000001);
        session.RaiseContentSize(0, 2220);
        Assert.AreEqual(9d / 16d, item.DeviceAspectRatio, 0.000001);
    }

    [TestMethod]
    [DataRow("Connecting", "Đang kết nối...")]
    [DataRow("Online", "Trực tuyến")]
    [DataRow("Disconnected", "Đã ngắt kết nối")]
    [DataRow("Failed", "Thất bại")]
    public async Task LocalizationRefresh_UpdatesStatusWithoutRestart(
        string state,
        string expectedVietnameseStatus)
    {
        FakeTracker tracker = CreateOnlineTracker();
        FakeLocalizationService localization = new();
        FakeViewDeviceSession session = new(Serial);
        FakeSessionFactory factory = new(session);
        await using ViewMultipleDeviceItemViewModel item = CreateItem(
            tracker,
            factory,
            localization: localization,
            retryDelay: static (_, _) => Task.CompletedTask);
        List<string?> propertyChanges = [];
        item.PropertyChanged += (_, eventArgs) => propertyChanges.Add(eventArgs.PropertyName);

        switch (state)
        {
            case "Online":
                await item.StartAsync(CancellationToken.None);
                break;
            case "Disconnected":
                await item.StartAsync(CancellationToken.None);
                await item.StopSessionAsync();
                break;
            case "Failed":
                await item.StartAsync(CancellationToken.None);
                await item.HandleNativeHostFailureAsync(
                    new InvalidOperationException("host"),
                    item.NativeWindowHandle);
                break;
        }

        int createCount = factory.CreateCount;
        int startCount = session.StartCount;
        int stopCount = session.StopCount;
        propertyChanges.Clear();

        localization.ApplyLanguage("vi");
        await item.RefreshLocalizedTextAsync();

        Assert.AreEqual(expectedVietnameseStatus, item.StatusText);
        Assert.IsTrue(propertyChanges.Contains(nameof(ViewMultipleDeviceItemViewModel.StatusText)));
        Assert.IsFalse(propertyChanges.Contains(nameof(ViewMultipleDeviceItemViewModel.State)));
        Assert.IsFalse(propertyChanges.Contains(nameof(ViewMultipleDeviceItemViewModel.IsRunning)));
        Assert.IsFalse(propertyChanges.Contains(nameof(ViewMultipleDeviceItemViewModel.IsDisconnected)));
        Assert.IsFalse(propertyChanges.Contains(nameof(ViewMultipleDeviceItemViewModel.IsFailed)));
        Assert.IsFalse(propertyChanges.Contains(nameof(ViewMultipleDeviceItemViewModel.NativeWindowHandle)));
        Assert.IsFalse(propertyChanges.Contains(nameof(ViewMultipleDeviceItemViewModel.DeviceAspectRatio)));
        Assert.AreEqual(createCount, factory.CreateCount);
        Assert.AreEqual(startCount, session.StartCount);
        Assert.AreEqual(stopCount, session.StopCount);
    }

    [TestMethod]
    public async Task LateEventsFromOldSession_AreIgnoredAfterReplacement()
    {
        FakeTracker tracker = CreateOnlineTracker();
        FakeViewDeviceSession first = new(Serial, 1080, 2220);
        FakeViewDeviceSession second = new(Serial, 720, 1280);
        FakeSessionFactory factory = new(first, second);
        await using ViewMultipleDeviceItemViewModel item = CreateItem(tracker, factory);

        await item.StartAsync(CancellationToken.None);
        await item.StopSessionAsync();
        await item.StartAsync(CancellationToken.None);

        IntPtr currentHandle = item.NativeWindowHandle;
        ViewDeviceSessionState currentState = item.State;
        double currentAspect = item.DeviceAspectRatio;

        first.RaiseState(ViewDeviceSessionState.Failed);
        first.RaiseNativeWindowReady();
        first.RaiseContentSize(2220, 1080);
        first.Exit();

        Assert.AreEqual(currentState, item.State);
        Assert.AreEqual(currentHandle, item.NativeWindowHandle);
        Assert.AreEqual(currentAspect, item.DeviceAspectRatio, 0.000001);
        Assert.AreEqual(2, factory.CreateCount);
        Assert.AreEqual(1, second.StartCount);
    }

    [TestMethod]
    public async Task StaleHostFailure_DoesNotStopReplacementSession()
    {
        List<int> attempts = [];
        FakeTracker tracker = CreateOnlineTracker();
        FakeSessionFactory factory = new();
        await using ViewMultipleDeviceItemViewModel item = CreateItem(
            tracker,
            factory,
            retryRequest: (_, attempt) => attempts.Add(attempt),
            retryDelay: static (_, _) => Task.CompletedTask);

        await item.StartAsync(CancellationToken.None);
        IntPtr handleA = item.NativeWindowHandle;
        await item.StopSessionAsync();

        await item.StartAsync(CancellationToken.None);
        FakeViewDeviceSession sessionB = factory.Sessions[1];
        IntPtr handleB = item.NativeWindowHandle;
        int stopCountB = sessionB.StopCount;
        int disposeCountB = sessionB.DisposeCount;

        await item.HandleNativeHostFailureAsync(
            new InvalidOperationException("stale host A"),
            handleA);

        Assert.AreEqual(stopCountB, sessionB.StopCount);
        Assert.AreEqual(disposeCountB, sessionB.DisposeCount);
        Assert.AreEqual(handleB, item.NativeWindowHandle);
        Assert.IsTrue(item.IsRunning);
        Assert.IsEmpty(attempts);
        Assert.AreEqual(0, item.RetryAttempt);

        await item.HandleNativeHostFailureAsync(
            new InvalidOperationException("current host B"),
            handleB);

        Assert.AreEqual(stopCountB + 1, sessionB.StopCount);
        Assert.AreEqual(disposeCountB + 1, sessionB.DisposeCount);
        CollectionAssert.AreEqual(new[] { 1 }, attempts);
        Assert.IsFalse(item.IsRunning);
    }

    private static ViewMultipleDeviceItemViewModel CreateItem(
        FakeTracker tracker,
        FakeSessionFactory factory,
        FakeLocalizationService? localization = null,
        Action<ViewMultipleDeviceItemViewModel, int>? retryRequest = null,
        Func<TimeSpan, CancellationToken, Task>? retryDelay = null)
    {
        return new ViewMultipleDeviceItemViewModel(
            Serial,
            "Device",
            factory,
            tracker,
            localization ?? new FakeLocalizationService(),
            new ImmediateUiDispatcher(),
            NullLogger<ViewMultipleDeviceItemViewModel>.Instance,
            retryRequest,
            retryDelay);
    }

    private static FakeTracker CreateOnlineTracker()
    {
        return new FakeTracker([new AdbDevice(Serial, AdbDeviceStatus.Online)]);
    }

    private static TaskCompletionSource NewSignal()
    {
        return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static Func<TimeSpan, CancellationToken, Task> CreateCancellableDelay(
        TaskCompletionSource cancellationObserved)
    {
        return async (_, cancellationToken) =>
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                cancellationObserved.TrySetResult();
                throw;
            }
        };
    }
}
