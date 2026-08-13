using DeepDroidChanger.Services;
using DeepDroidChanger.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DeepDroidChanger.Tests.Services.Implementations.DialogServices;

[TestClass]
public sealed class DeviceViewerRuntimeTests
{
    [TestMethod]
    public void StartingNewIpRefresh_CancelsPreviousAndRequiresCurrentGeneration()
    {
        using var viewerLifetime = new CancellationTokenSource();
        using var refreshLifetime = new DeviceViewerIpRefreshLifetime(viewerLifetime.Token);
        var first = refreshLifetime.Start(1)!;
        var firstToken = first.Token;

        var second = refreshLifetime.Start(2)!;

        Assert.IsTrue(firstToken.IsCancellationRequested);
        Assert.IsFalse(first.IsCurrent(1));
        Assert.IsFalse(second.IsCurrent(1));
        Assert.IsTrue(second.IsCurrent(2));

        second.Dispose();
        Assert.IsFalse(second.IsCurrent(2));
    }

    [TestMethod]
    public void CancelCurrentIpRefresh_InvalidatesTheOperationAndCancelsItsToken()
    {
        using var viewerLifetime = new CancellationTokenSource();
        using var refreshLifetime = new DeviceViewerIpRefreshLifetime(viewerLifetime.Token);
        var operation = refreshLifetime.Start(7)!;
        var operationToken = operation.Token;

        refreshLifetime.CancelCurrent();

        Assert.IsTrue(operationToken.IsCancellationRequested);
        Assert.IsFalse(operation.IsCurrent(7));
    }

    [TestMethod]
    public async Task ActiveTaskTracker_RemovesCompletedOperationsFromItsSnapshot()
    {
        var tracker = new DeviceViewerActiveTaskTracker();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task tracked = tracker.Track(completion.Task);

        Assert.AreEqual(1, tracker.Count);
        Assert.HasCount(1, tracker.Snapshot());

        completion.SetResult();
        await tracked;

        Assert.IsTrue(SpinWait.SpinUntil(() => tracker.Count == 0, TimeSpan.FromSeconds(1)));
        Assert.HasCount(0, tracker.Snapshot());
    }

    [TestMethod]
    public async Task StaleIpResult_IsNotAppliedAfterGenerationBecomesInvalid()
    {
        var ipLookup = Substitute.For<IIpGeolocationService>();
        var lookupCompletion = new TaskCompletionSource<DeepDroidChanger.Models.IpGeolocationInfo>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        ipLookup.GetDeviceIpGeolocationAsync("SERIAL", Arg.Any<CancellationToken>())
            .Returns(lookupCompletion.Task);

        var localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>())
            .Returns(callInfo => callInfo.Arg<string>());
        var viewModel = new DeviceViewerViewModel(
            Substitute.For<IAdbCommandService>(),
            ipLookup,
            localization,
            new DeviceActionCoordinatorService(),
            NullLogger<DeviceViewerViewModel>.Instance);
        viewModel.Initialize("SERIAL", "Device");

        var canApply = true;
        Task refresh = viewModel.RefreshDeviceIpAsync(
            CancellationToken.None,
            showCheckingState: false,
            canApplyResult: () => canApply);
        await Task.Yield();

        canApply = false;
        lookupCompletion.SetResult(new DeepDroidChanger.Models.IpGeolocationInfo
        {
            PublicIp = "203.0.113.10"
        });
        await refresh;

        Assert.AreNotEqual("203.0.113.10", viewModel.DeviceIpText);
    }
}
