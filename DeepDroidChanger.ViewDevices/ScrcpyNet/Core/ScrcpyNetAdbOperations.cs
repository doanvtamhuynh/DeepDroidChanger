using SharpAdbClient;
using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace ScrcpyNet
{
    internal interface IScrcpyNetAdbOperations
    {
        void CreateReverseForward(DeviceData device, string remote, string local, bool rebind);
        void RemoveReverseForward(DeviceData device, string remote);
        Task ExecuteRemoteCommandAsync(
            string command,
            DeviceData device,
            IShellOutputReceiver receiver,
            CancellationToken cancellationToken);
        void UploadMobileServer(
            DeviceData device,
            string serverFile,
            string remotePath,
            CancellationToken cancellationToken);
    }

    internal sealed class SharpAdbClientOperations : IScrcpyNetAdbOperations
    {
        private readonly AdbClient _adb = new();

        public void CreateReverseForward(DeviceData device, string remote, string local, bool rebind)
        {
            _adb.CreateReverseForward(device, remote, local, rebind);
        }

        public void RemoveReverseForward(DeviceData device, string remote)
        {
            _adb.RemoveReverseForward(device, remote);
        }

        public Task ExecuteRemoteCommandAsync(
            string command,
            DeviceData device,
            IShellOutputReceiver receiver,
            CancellationToken cancellationToken)
        {
            return _adb.ExecuteRemoteCommandAsync(command, device, receiver, cancellationToken);
        }

        public void UploadMobileServer(
            DeviceData device,
            string serverFile,
            string remotePath,
            CancellationToken cancellationToken)
        {
            using SyncService service = new(
                new AdbSocket(new IPEndPoint(IPAddress.Loopback, AdbClient.AdbServerPort)),
                device);
            using Stream stream = File.OpenRead(serverFile);
            service.Push(stream, remotePath, 444, DateTime.Now, null, cancellationToken);
        }
    }

    internal sealed class ScrcpyNetServerLifecycle
    {
        public int Scid { get; private set; }
        public string SessionEndpoint => $"localabstract:scrcpy_{Scid:x8}";
        public string RemoteServerPath { get; private set; } = string.Empty;

        private readonly DeviceData _device;
        private readonly IScrcpyNetAdbOperations _adb;
        private bool _reverseCreated;

        public ScrcpyNetServerLifecycle(
            DeviceData device,
            IScrcpyNetAdbOperations adb)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
            _adb = adb ?? throw new ArgumentNullException(nameof(adb));
        }

        // SharpAdbClient 2.3.23 exposes reverse-forward operations only as
        // synchronous methods without a CancellationToken or operation timeout.
        // Keep this call in the awaited startup boundary; never abandon it from
        // a timeout race because it could later mutate the reverse mapping.
        public void Setup(int hostPort, string serverFile, CancellationToken cancellationToken)
        {
            if (hostPort <= 0)
                throw new ArgumentOutOfRangeException(nameof(hostPort));
            if (string.IsNullOrWhiteSpace(serverFile))
                throw new ArgumentException("A scrcpy server file is required.", nameof(serverFile));

            Cleanup();
            Scid = System.Security.Cryptography.RandomNumberGenerator.GetInt32(1, int.MaxValue);
            RemoteServerPath = $"/data/local/tmp/deepdroid-scrcpy-{Scid:x8}-{Guid.NewGuid():N}.jar";
            _adb.UploadMobileServer(_device, serverFile, RemoteServerPath, cancellationToken);
            _adb.CreateReverseForward(_device, SessionEndpoint, $"tcp:{hostPort}", rebind: false);
            _reverseCreated = true;
        }

        public void Cleanup()
        {
            if (!_reverseCreated)
                return;

            // SharpAdbClient exposes reverse removal only synchronously. Keep
            // ownership until this call returns; never abandon it behind a
            // timeout because it mutates the shared reverse-forward mapping.
            _adb.RemoveReverseForward(_device, SessionEndpoint);
            _reverseCreated = false;
        }

        public Task StartServerAsync(
            string command,
            IShellOutputReceiver receiver,
            CancellationToken cancellationToken)
        {
            return _adb.ExecuteRemoteCommandAsync(command, _device, receiver, cancellationToken);
        }

        public async Task CleanupServerFileAsync(CancellationToken cancellationToken)
        {
            // This path is generated here, contains only hex and separators, and belongs to this lifecycle.
            string ownedPath = RemoteServerPath;
            if (string.IsNullOrEmpty(ownedPath)) return;
            await _adb.ExecuteRemoteCommandAsync($"rm -f {ownedPath}", _device, new SerilogOutputReceiver(), cancellationToken).ConfigureAwait(false);
            if (RemoteServerPath == ownedPath) RemoteServerPath = string.Empty;
        }
    }
}
