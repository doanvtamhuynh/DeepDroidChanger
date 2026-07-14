using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DeepDroidChanger.Tests.Services.Implementations
{
    [TestClass]
    public sealed class DeviceViewerCoordinatorServiceTests
    {
        [TestMethod]
        public async Task QueryDeviceAspectRatioAsync_ParsesPhysicalSize()
        {
            var streamService = Substitute.For<IDeviceViewerStreamService>();
            var commandService = Substitute.For<IAdbCommandService>();
            commandService
                .RunAdbAsync("SERIAL", "shell wm size", CancellationToken.None)
                .Returns(new CommandResult(0, "Physical size: 1080x2400", string.Empty));
            var coordinator = new DeviceViewerCoordinatorService(
                streamService,
                commandService,
                NullLogger<DeviceViewerCoordinatorService>.Instance);

            var aspectRatio = await coordinator.QueryDeviceAspectRatioAsync("SERIAL", CancellationToken.None);

            Assert.AreEqual(1080d / 2400d, aspectRatio, 0.000001d);
        }

        [TestMethod]
        public async Task MonitorStreamAsync_DisconnectedDeviceStopsCurrentSessionAndMarksWaiting()
        {
            var streamService = Substitute.For<IDeviceViewerStreamService>();
            var commandService = Substitute.For<IAdbCommandService>();
            commandService
                .RunAdbAsync("SERIAL", "get-state", Arg.Any<CancellationToken>())
                .Returns(new CommandResult(1, string.Empty, "offline"));
            var coordinator = new DeviceViewerCoordinatorService(
                streamService,
                commandService,
                NullLogger<DeviceViewerCoordinatorService>.Instance,
                TimeSpan.FromMilliseconds(10));
            var session = Substitute.For<IDeviceViewerStreamSession>();
            session.HasExited.Returns(false);
            var currentSession = session;
            var waitingMarked = false;
            var ipUnavailableMarked = false;
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));

            var context = new DeviceViewerMonitorContext(
                "SERIAL",
                new SemaphoreSlim(1, 1),
                cancellation.Token,
                () => currentSession,
                value => currentSession = value,
                async (stoppingSession, _) =>
                {
                    await stoppingSession.StopAsync(CancellationToken.None);
                    await cancellation.CancelAsync();
                },
                () => 1,
                _ => true,
                () => { },
                () => { },
                () =>
                {
                    waitingMarked = true;
                    return Task.CompletedTask;
                },
                () =>
                {
                    ipUnavailableMarked = true;
                    return Task.CompletedTask;
                },
                () => false,
                _ => Task.FromResult(new DeviceViewerStreamBounds(0, 0, 10, 10)),
                () => Task.FromResult(new IntPtr(1)),
                () => Task.CompletedTask,
                () => Task.CompletedTask,
                () => Task.CompletedTask);

            await coordinator.MonitorStreamAsync(context);

            await session.Received(1).StopAsync(CancellationToken.None);
            Assert.IsNull(currentSession);
            Assert.IsTrue(waitingMarked);
            Assert.IsTrue(ipUnavailableMarked);
        }

        [TestMethod]
        public async Task EnsureStreamAsync_StaleGeneration_StopsNewSessionWithoutPublishingIt()
        {
            IDeviceViewerStreamService streamService = Substitute.For<IDeviceViewerStreamService>();
            IAdbCommandService commandService = Substitute.For<IAdbCommandService>();
            commandService.RunAdbAsync("SERIAL", "get-state", Arg.Any<CancellationToken>())
                .Returns(new CommandResult(0, "device", string.Empty));
            commandService.RunAdbAsync("SERIAL", "shell wm size", Arg.Any<CancellationToken>())
                .Returns(new CommandResult(0, "Physical size: 1080x2400", string.Empty));
            IDeviceViewerStreamSession newSession = Substitute.For<IDeviceViewerStreamSession>();
            streamService.StartAsync(
                    "SERIAL",
                    new IntPtr(1),
                    Arg.Any<DeviceViewerStreamBounds>(),
                    Arg.Any<CancellationToken>())
                .Returns(newSession);
            var coordinator = new DeviceViewerCoordinatorService(
                streamService,
                commandService,
                NullLogger<DeviceViewerCoordinatorService>.Instance);
            IDeviceViewerStreamSession? publishedSession = null;
            const long staleGeneration = 1;

            var context = new DeviceViewerStartContext(
                "SERIAL",
                new SemaphoreSlim(1, 1),
                CancellationToken.None,
                () => publishedSession,
                value => publishedSession = value,
                (_, _) => Task.CompletedTask,
                () => { },
                staleGeneration,
                _ => false,
                () => Task.CompletedTask,
                () => Task.CompletedTask,
                _ => Task.FromResult(new DeviceViewerStreamBounds(0, 0, 100, 200)),
                () => Task.FromResult(new IntPtr(1)),
                () => Task.CompletedTask,
                () => Task.CompletedTask);

            await coordinator.EnsureStreamAsync(context);

            await newSession.Received(1).StopAsync(CancellationToken.None);
            newSession.Received(1).Dispose();
            Assert.IsNull(publishedSession);
        }

        [TestMethod]
        public async Task QueryDeviceAspectRatioAsync_InvalidOutput_ReturnsDeterministicFallback()
        {
            IAdbCommandService commandService = Substitute.For<IAdbCommandService>();
            commandService.RunAdbAsync("SERIAL", "shell wm size", Arg.Any<CancellationToken>())
                .Returns(new CommandResult(0, "Physical size: invalid", string.Empty));
            var coordinator = new DeviceViewerCoordinatorService(
                Substitute.For<IDeviceViewerStreamService>(),
                commandService,
                NullLogger<DeviceViewerCoordinatorService>.Instance);

            double ratio = await coordinator.QueryDeviceAspectRatioAsync("SERIAL", CancellationToken.None);

            Assert.AreEqual(9d / 20d, ratio, 0.000001d);
        }
    }
}
