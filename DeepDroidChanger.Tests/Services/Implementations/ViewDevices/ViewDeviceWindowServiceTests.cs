using System.Windows;
using System.Windows.Threading;
using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using DeepDroidChanger.ViewDevices.Contracts;
using DeepDroidChanger.ViewDevices.Models;
using DeepDroidChanger.ViewDevices.Runtime;
using DeepDroidChanger.ViewModels;
using DeepDroidChanger.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DeepDroidChanger.Tests.Services.Implementations.ViewDevices;

[TestClass]
[DoNotParallelize]
public sealed class ViewDeviceWindowServiceTests
{
    [TestMethod]
    [DataRow("Samsung S9", "22e20c84e40c7ece", "Samsung S9 - 22e20c84e40c7ece")]
    [DataRow(null, "22e20c84e40c7ece", "22e20c84e40c7ece")]
    [DataRow("   ", "22e20c84e40c7ece", "22e20c84e40c7ece")]
    [DataRow("22E20C84E40C7ECE", "22e20c84e40c7ece", "22e20c84e40c7ece")]
    public void FormatWindowTitle_UsesNameAndSerialWithoutDuplicateFallback(
        string? displayName,
        string serial,
        string expected)
    {
        string title = ViewDeviceWindowService.FormatWindowTitle(serial, displayName);

        Assert.AreEqual(expected, title);
    }

    [TestMethod]
    public async Task Open_NormalizesSerial()
    {
        await RunOnStaAsync(async () =>
        {
            EnsureApplicationResources();
            TestScopeFactory scopes = new();
            await using ViewDeviceWindowService service = CreateService(scopes);

            await service.OpenAsync("  SERIAL  ", null);

            Assert.AreEqual(1, service.TrackedEntryCountForTesting);
            Assert.AreEqual("SERIAL", scopes.Scopes.Single().ViewModel.Serial);
            Assert.AreEqual(
                "SERIAL",
                Application.Current!.Windows
                    .OfType<ViewDeviceWindow>()
                    .Single(window => window.Title == "SERIAL")
                    .Title);

            await service.CloseAllAsync();
            Assert.AreEqual(0, service.TrackedEntryCountForTesting);
        });
    }

    [TestMethod]
    public async Task Open_DuplicateSerial_ActivatesExisting()
    {
        await RunOnStaAsync(async () =>
        {
            EnsureApplicationResources();
            TestScopeFactory scopes = new();
            await using ViewDeviceWindowService service = CreateService(scopes);

            await service.OpenAsync("SERIAL", "Device");
            ViewDeviceWindow window = Application.Current!.Windows
                .OfType<ViewDeviceWindow>()
                .Single(candidate => candidate.Title == "Device - SERIAL");
            window.WindowState = WindowState.Minimized;

            await service.OpenAsync("SERIAL", "Other name");

            Assert.AreEqual(WindowState.Normal, window.WindowState);
            Assert.AreEqual(1, scopes.Scopes.Count);
            Assert.AreEqual(1, service.TrackedEntryCountForTesting);
            await service.CloseAllAsync();
        });
    }

    [TestMethod]
    public async Task Rename_UpdatesViewDeviceHeaderAndNativeWindowTitle()
    {
        await RunOnStaAsync(async () =>
        {
            EnsureApplicationResources();
            TestScopeFactory scopes = new();
            await using ViewDeviceWindowService service = CreateService(scopes);

            await service.OpenAsync("SERIAL", "Old name");
            ViewDeviceWindow window = Application.Current!.Windows
                .OfType<ViewDeviceWindow>()
                .Single(candidate => ReferenceEquals(candidate.DataContext, scopes.Scopes.Single().ViewModel));
            ViewDeviceViewModel viewModel = scopes.Scopes.Single().ViewModel;

            viewModel.RenameConfigCommand.Execute(null);
            viewModel.RenameName = "New name";
            await viewModel.SaveRenameConfigCommand.ExecuteAsync(null);

            Assert.AreEqual("New name", viewModel.DeviceName);
            Assert.AreEqual(
                ViewDeviceWindowService.FormatWindowTitle("SERIAL", "New name"),
                window.Title);

            await service.CloseAllAsync();
        });
    }

    [TestMethod]
    public async Task Open_CaseInsensitiveDuplicate()
    {
        await RunOnStaAsync(async () =>
        {
            EnsureApplicationResources();
            TestScopeFactory scopes = new();
            await using ViewDeviceWindowService service = CreateService(scopes);

            await service.OpenAsync("SERIAL", null);
            await service.OpenAsync("serial", null);

            Assert.AreEqual(1, scopes.Scopes.Count);
            Assert.AreEqual(1, service.TrackedEntryCountForTesting);
            await service.CloseAllAsync();
        });
    }

    [TestMethod]
    public async Task Open_SerialWithWhitespace_DoesNotCreateDuplicate()
    {
        await RunOnStaAsync(async () =>
        {
            EnsureApplicationResources();
            TestScopeFactory scopes = new();
            await using ViewDeviceWindowService service = CreateService(scopes);

            await service.OpenAsync("SERIAL", null);
            await service.OpenAsync("  SERIAL  ", null);

            Assert.AreEqual(1, scopes.Scopes.Count);
            Assert.AreEqual(1, service.TrackedEntryCountForTesting);
            await service.CloseAllAsync();
        });
    }

    [TestMethod]
    public async Task Open_WithPresentationCoordinator_SuspendsBeforeInitializationAndResumesOnClose()
    {
        await RunOnStaAsync(async () =>
        {
            EnsureApplicationResources();
            TestScopeFactory scopes = new();
            ViewDevicePresentationCoordinator coordinator = new();
            List<string> transitions = [];
            TaskCompletionSource suspendEntered = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource releaseSuspend = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using IDisposable registration = coordinator.RegisterMultiView(
                async (serial, cancellationToken) =>
                {
                    transitions.Add($"suspend:{serial}");
                    suspendEntered.TrySetResult();
                    await releaseSuspend.Task.WaitAsync(cancellationToken);
                },
                (serial, cancellationToken) =>
                {
                    transitions.Add($"resume:{serial}");
                    return Task.CompletedTask;
                });
            await using ViewDeviceWindowService service = CreateService(scopes, coordinator);

            Task open = service.OpenAsync("SERIAL", "Device");
            await suspendEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.AreEqual(1, scopes.Scopes.Count);
            Assert.AreEqual(string.Empty, scopes.Scopes[0].ViewModel.Serial);
            Assert.AreEqual(0, service.TrackedEntryCountForTesting);
            CollectionAssert.AreEqual(new[] { "suspend:SERIAL" }, transitions);

            releaseSuspend.TrySetResult();
            await open;
            Assert.AreEqual("SERIAL", scopes.Scopes[0].ViewModel.Serial);

            await service.OpenAsync("serial", "Other name");
            CollectionAssert.AreEqual(new[] { "suspend:SERIAL" }, transitions);

            await service.CloseAllAsync();
            CollectionAssert.AreEqual(
                new[] { "suspend:SERIAL", "resume:SERIAL" },
                transitions);
        });
    }

    [TestMethod]
    public async Task ConcurrentOpen_SameSerial_CreatesOneScopeAndOneWindow()
    {
        await RunOnStaAsync(async () =>
        {
            EnsureApplicationResources();
            TestScopeFactory scopes = new();
            ViewDevicePresentationCoordinator coordinator = new();
            TaskCompletionSource publicationEntered = NewSignal();
            TaskCompletionSource releasePublication = NewSignal();
            await using ViewDeviceWindowService service = new(
                scopes,
                new ImmediateUiDispatcher(),
                NullLogger<ViewDeviceWindowService>.Instance,
                coordinator,
                async () =>
                {
                    publicationEntered.TrySetResult();
                    await releasePublication.Task;
                });

            Task first = service.OpenAsync("SERIAL", "First");
            await publicationEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Task second = service.OpenAsync("serial", "Second");
            releasePublication.TrySetResult();

            await Task.WhenAll(first, second);

            Assert.AreEqual(1, scopes.Scopes.Count);
            Assert.AreEqual(1, service.TrackedEntryCountForTesting);
            Assert.AreEqual(0, service.OpeningEntryCountForTesting);
            await service.CloseAllAsync();
        });
    }

    [TestMethod]
    public async Task ConcurrentOpen_FirstFails_AllConcurrentCallersObserveFailure()
    {
        await RunOnStaAsync(async () =>
        {
            EnsureApplicationResources();
            TestScopeFactory scopes = new();
            ViewDevicePresentationCoordinator coordinator = new();
            TaskCompletionSource publicationEntered = NewSignal();
            TaskCompletionSource releasePublication = NewSignal();
            await using ViewDeviceWindowService service = new(
                scopes,
                new ImmediateUiDispatcher(),
                NullLogger<ViewDeviceWindowService>.Instance,
                coordinator,
                async () =>
                {
                    publicationEntered.TrySetResult();
                    await releasePublication.Task;
                    throw new InvalidOperationException("configured open failure");
                });

            Task first = service.OpenAsync("SERIAL", "First");
            await publicationEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Task second = service.OpenAsync("serial", "Second");
            releasePublication.TrySetResult();

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => first);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => second);
            Assert.AreEqual(1, scopes.Scopes.Count);
            Assert.AreEqual(0, service.TrackedEntryCountForTesting);
            Assert.AreEqual(0, service.OpeningEntryCountForTesting);
        });
    }

    [TestMethod]
    public async Task ConcurrentOpen_OwnerCancellation_IsSharedWithoutRetry()
    {
        await RunOnStaAsync(async () =>
        {
            EnsureApplicationResources();
            TestScopeFactory scopes = new();
            ViewDevicePresentationCoordinator coordinator = new();
            TaskCompletionSource publicationEntered = NewSignal();
            TaskCompletionSource releasePublication = NewSignal();
            await using ViewDeviceWindowService service = new(
                scopes,
                new ImmediateUiDispatcher(),
                NullLogger<ViewDeviceWindowService>.Instance,
                coordinator,
                async () =>
                {
                    publicationEntered.TrySetResult();
                    await releasePublication.Task;
                });
            using CancellationTokenSource ownerCancellation = new();
            scopes.InitializationFailure = new OperationCanceledException(ownerCancellation.Token);

            Task first = service.OpenAsync("SERIAL", "First", ownerCancellation.Token);
            await publicationEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Task second = service.OpenAsync("serial", "Second");
            ownerCancellation.Cancel();
            releasePublication.TrySetResult();

            await Assert.ThrowsAsync<OperationCanceledException>(() => first);
            await Assert.ThrowsAsync<OperationCanceledException>(() => second);
            Assert.AreEqual(1, scopes.Scopes.Count);
            Assert.AreEqual(0, service.TrackedEntryCountForTesting);
            Assert.AreEqual(0, service.OpeningEntryCountForTesting);
        });
    }

    [TestMethod]
    public async Task OpenFailure_ReleasesPresentationLeaseAndResumesMultiView()
    {
        await RunOnStaAsync(async () =>
        {
            EnsureApplicationResources();
            TestScopeFactory scopes = new();
            ViewDevicePresentationCoordinator coordinator = new();
            int suspendCount = 0;
            int resumeCount = 0;
            using IDisposable registration = coordinator.RegisterMultiView(
                (_, _) =>
                {
                    Interlocked.Increment(ref suspendCount);
                    return Task.CompletedTask;
                },
                (_, _) =>
                {
                    Interlocked.Increment(ref resumeCount);
                    return Task.CompletedTask;
                });
            TaskCompletionSource publicationEntered = NewSignal();
            TaskCompletionSource releasePublication = NewSignal();
            await using ViewDeviceWindowService service = new(
                scopes,
                new ImmediateUiDispatcher(),
                NullLogger<ViewDeviceWindowService>.Instance,
                coordinator,
                async () =>
                {
                    publicationEntered.TrySetResult();
                    await releasePublication.Task;
                });
            using CancellationTokenSource cancellation = new();
            scopes.InitializationFailure = new OperationCanceledException(cancellation.Token);

            Task open = service.OpenAsync("SERIAL", "Device", cancellation.Token);
            await publicationEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            cancellation.Cancel();
            releasePublication.TrySetResult();

            await Assert.ThrowsAsync<OperationCanceledException>(() => open);
            Assert.AreEqual(1, suspendCount);
            Assert.AreEqual(1, resumeCount);
            Assert.AreEqual(0, service.TrackedEntryCountForTesting);
            Assert.AreEqual(0, service.OpeningEntryCountForTesting);
            Assert.AreEqual(1, scopes.Scopes.Single().DisposeCount);
        });
    }

    [TestMethod]
    public async Task Dispose_PreventsPublication()
    {
        RaceResult result = await RunOpenDisposeRaceAsync();

        Assert.AreEqual(0, result.EntryCount);
        Assert.AreEqual(1, result.ScopeDisposeCount);
    }

    [TestMethod]
    public async Task OpenDisposeRace_NoEntryLeak()
    {
        RaceResult result = await RunOpenDisposeRaceAsync();

        Assert.AreEqual(0, result.EntryCount);
    }

    [TestMethod]
    public async Task OpenDisposeRace_NoScopeLeak()
    {
        RaceResult result = await RunOpenDisposeRaceAsync();

        Assert.AreEqual(1, result.ScopeDisposeCount);
    }

    [TestMethod]
    public async Task CloseAll_LeavesNoTrackedWindow()
    {
        await RunOnStaAsync(async () =>
        {
            EnsureApplicationResources();
            TestScopeFactory scopes = new();
            await using ViewDeviceWindowService service = CreateService(scopes);

            await service.OpenAsync("SERIAL-A", null);
            await service.OpenAsync("SERIAL-B", null);
            Assert.AreEqual(2, service.TrackedEntryCountForTesting);

            await service.CloseAllAsync();

            Assert.AreEqual(0, service.TrackedEntryCountForTesting);
            Assert.IsTrue(scopes.Scopes.All(scope => scope.DisposeCount == 1));
        });
    }

    private static ViewDeviceWindowService CreateService(
        TestScopeFactory scopes,
        IViewDevicePresentationCoordinator? presentationCoordinator = null)
    {
        IViewDevicePresentationCoordinator coordinator =
            presentationCoordinator ?? new ViewDevicePresentationCoordinator();
        return new ViewDeviceWindowService(
            scopes,
            new ImmediateUiDispatcher(),
            NullLogger<ViewDeviceWindowService>.Instance,
            coordinator);
    }

    private static async Task<RaceResult> RunOpenDisposeRaceAsync()
    {
        RaceResult? result = null;
        await RunOnStaAsync(async () =>
        {
            EnsureApplicationResources();
            TestScopeFactory scopes = new();
            TaskCompletionSource publicationEntered =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource releasePublication =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            ViewDevicePresentationCoordinator coordinator = new();
            await using ViewDeviceWindowService service = new(
                scopes,
                new ImmediateUiDispatcher(),
                NullLogger<ViewDeviceWindowService>.Instance,
                coordinator,
                async () =>
                {
                    publicationEntered.TrySetResult();
                    await releasePublication.Task;
                });

            Task open = service.OpenAsync("  SERIAL  ", null);
            await publicationEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

            // This runs on the same dispatcher while OpenCoreAsync is paused
            // before publication, so disposal wins the publication lock
            // deterministically rather than by timing luck.
            Task dispose = service.DisposeAsync().AsTask();
            releasePublication.TrySetResult();

            await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => open);
            await dispose;
            result = new RaceResult(
                service.TrackedEntryCountForTesting,
                scopes.Scopes.Single().DisposeCount);
        });

        return result ?? throw new InvalidOperationException("The WPF race did not produce a result.");
    }

    private static void EnsureApplicationResources()
    {
        WpfTestDispatcher.EnsureApplicationResources();
    }

    private static Task RunOnStaAsync(Func<Task> operation) => WpfTestDispatcher.RunAsync(operation);

    private static TaskCompletionSource NewSignal()
    {
        return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed record RaceResult(int EntryCount, int ScopeDisposeCount);

    private sealed class ImmediateUiDispatcher : IUiDispatcherService
    {
        public bool CheckAccess() => true;

        public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            action();
            return Task.CompletedTask;
        }
    }

    private sealed class TestScopeFactory : IServiceScopeFactory
    {
        public List<TestScope> Scopes { get; } = [];

        public Exception? InitializationFailure { get; set; }

        public IServiceScope CreateScope()
        {
            IDeviceConfigService config = Substitute.For<IDeviceConfigService>();
            config.RenameDeviceAsync(
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>())
                .Returns(true);
            TestScope scope = new(CreateWindowViewModel(config, InitializationFailure));
            Scopes.Add(scope);
            return scope;
        }

        private static ViewDeviceViewModel CreateWindowViewModel(
            IDeviceConfigService config,
            Exception? initializationFailure)
        {
            IAdbDeviceTrackerService tracker = Substitute.For<IAdbDeviceTrackerService>();
            tracker.Health.Returns(AdbDeviceTrackerHealth.Connected);
            tracker.CurrentSnapshot.Returns([]);
            tracker.GetDevice(Arg.Any<string>()).Returns(call =>
                new AdbDevice(call.Arg<string>(), AdbDeviceStatus.Offline));
            tracker.StartAsync(Arg.Any<CancellationToken>()).Returns(_ =>
                initializationFailure is null
                    ? Task.CompletedTask
                    : Task.FromException(initializationFailure));

            ILocalizationService localization = Substitute.For<ILocalizationService>();
            localization.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());

            return new ViewDeviceViewModel(
                Substitute.For<ISingleViewDeviceSessionFactory>(),
                tracker,
                Substitute.For<IAdbCommandService>(),
                Substitute.For<IFilePickerDialogService>(),
                Substitute.For<IViewDeviceScreenshotService>(),
                localization,
                new ImmediateUiDispatcher(),
                Substitute.For<IViewDeviceClipboardService>(),
                NullLogger<ViewDeviceViewModel>.Instance,
                config,
                Substitute.For<IDeviceActionService>(),
                Substitute.For<IPackageInstallService>(),
                Substitute.For<IPollingService>());
        }
    }

    private sealed class TestScope(ViewDeviceViewModel viewModel) : IServiceScope, IAsyncDisposable
    {
        private int disposed;

        public IServiceProvider ServiceProvider { get; } = new TestServiceProvider(viewModel);
        public ViewDeviceViewModel ViewModel { get; } = viewModel;
        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
                return;

            DisposeCount++;
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestServiceProvider(ViewDeviceViewModel viewModel) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(ViewDeviceViewModel) ? viewModel : null;
    }
}
