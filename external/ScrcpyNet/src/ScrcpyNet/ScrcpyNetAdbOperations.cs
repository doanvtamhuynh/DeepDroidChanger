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
        public const string RemoteEndpoint = "localabstract:scrcpy";
        private const string RemoteServerPath = "/data/local/tmp/scrcpy-server.jar";

        private readonly DeviceData _device;
        private readonly IScrcpyNetAdbOperations _adb;
        private readonly Action<Exception>? _logStaleReverseCleanupFailure;
        private bool _reverseCreated;

        public ScrcpyNetServerLifecycle(
            DeviceData device,
            IScrcpyNetAdbOperations adb,
            Action<Exception>? logStaleReverseCleanupFailure = null)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
            _adb = adb ?? throw new ArgumentNullException(nameof(adb));
            _logStaleReverseCleanupFailure = logStaleReverseCleanupFailure;
        }

        public void Setup(int hostPort, string serverFile, CancellationToken cancellationToken)
        {
            if (hostPort <= 0)
                throw new ArgumentOutOfRangeException(nameof(hostPort));
            if (string.IsNullOrWhiteSpace(serverFile))
                throw new ArgumentException("A scrcpy server file is required.", nameof(serverFile));

            _reverseCreated = false;
            TryRemoveStaleReverseForward();
            _adb.UploadMobileServer(_device, serverFile, RemoteServerPath, cancellationToken);
            _adb.CreateReverseForward(_device, RemoteEndpoint, $"tcp:{hostPort}", rebind: true);
            _reverseCreated = true;
        }

        public void Cleanup()
        {
            if (!_reverseCreated)
                return;

            _adb.RemoveReverseForward(_device, RemoteEndpoint);
            _reverseCreated = false;
        }

        public Task StartServerAsync(
            string command,
            IShellOutputReceiver receiver,
            CancellationToken cancellationToken)
        {
            return _adb.ExecuteRemoteCommandAsync(command, _device, receiver, cancellationToken);
        }

        private void TryRemoveStaleReverseForward()
        {
            try
            {
                _adb.RemoveReverseForward(_device, RemoteEndpoint);
            }
            catch (Exception exception)
            {
                _logStaleReverseCleanupFailure?.Invoke(exception);
            }
        }
    }
}
