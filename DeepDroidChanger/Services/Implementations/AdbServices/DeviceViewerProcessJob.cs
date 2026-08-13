using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services;

/// <summary>
/// Application-owned process job used as a last-resort cleanup boundary for scrcpy.
/// </summary>
internal sealed class DeviceViewerProcessJob : IDisposable
{
    private const uint JobObjectExtendedLimitInformation = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x2000;
    private const uint JobObjectLimitSilentBreakawayOk = 0x1000;

    private readonly SafeFileHandle? _handle;
    private readonly ILogger _logger;
    private int _disposed;

    private DeviceViewerProcessJob(SafeFileHandle? handle, ILogger logger)
    {
        _handle = handle;
        _logger = logger;
    }

    internal static DeviceViewerProcessJob Create(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        if (!OperatingSystem.IsWindows())
            return new DeviceViewerProcessJob(null, logger);

        var handle = CreateJobObject(IntPtr.Zero, null);
        if (handle == IntPtr.Zero)
        {
            logger.LogWarning(
                new Win32Exception(Marshal.GetLastWin32Error()),
                "Failed to create the scrcpy process job; continuing without the safety net.");
            return new DeviceViewerProcessJob(null, logger);
        }

        var safeHandle = new SafeFileHandle(handle, ownsHandle: true);
        var limits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = JobObjectLimitKillOnJobClose | JobObjectLimitSilentBreakawayOk
            }
        };

        if (!SetInformationJobObject(
                safeHandle,
                JobObjectExtendedLimitInformation,
                ref limits,
                Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>()))
        {
            logger.LogWarning(
                new Win32Exception(Marshal.GetLastWin32Error()),
                "Failed to configure the scrcpy process job; continuing without the safety net.");
            safeHandle.Dispose();
            return new DeviceViewerProcessJob(null, logger);
        }

        return new DeviceViewerProcessJob(safeHandle, logger);
    }

    internal void TryAssign(Process process, string serial)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (_handle is null || Volatile.Read(ref _disposed) != 0)
            return;

        try
        {
            if (!AssignProcessToJobObject(_handle, process.Handle))
            {
                _logger.LogWarning(
                    new Win32Exception(Marshal.GetLastWin32Error()),
                    "Failed to assign scrcpy process to the application job for {Serial}; continuing with normal cleanup.",
                    serial);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Failed to assign scrcpy process to the application job for {Serial}; continuing with normal cleanup.",
                serial);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _handle?.Dispose();
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr jobAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(
        SafeFileHandle job,
        uint informationClass,
        ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION jobObjectInformation,
        int jobObjectInformationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
}
