using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DeepDroidChanger.ViewDevices.Interop;

internal sealed class ProcessJob : IDisposable
{
    private const uint JobObjectExtendedLimitInformation = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x2000;
    private const uint JobObjectLimitSilentBreakawayOk = 0x1000;
    private readonly SafeFileHandle? _handle;
    private int _disposed;

    private ProcessJob(SafeFileHandle? handle)
    {
        _handle = handle;
    }

    public static ProcessJob Create()
    {
        if (!OperatingSystem.IsWindows())
            return new ProcessJob(null);

        IntPtr rawHandle = CreateJobObject(IntPtr.Zero, null);
        if (rawHandle == IntPtr.Zero)
            return new ProcessJob(null);

        SafeFileHandle handle = new(rawHandle, ownsHandle: true);
        JOBOBJECT_EXTENDED_LIMIT_INFORMATION limits = new()
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = JobObjectLimitKillOnJobClose | JobObjectLimitSilentBreakawayOk
            }
        };

        if (SetInformationJobObject(
                handle,
                JobObjectExtendedLimitInformation,
                ref limits,
                Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>()))
        {
            return new ProcessJob(handle);
        }

        handle.Dispose();
        return new ProcessJob(null);
    }

    public bool TryAssign(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        return _handle is not null &&
               Volatile.Read(ref _disposed) == 0 &&
               AssignProcessToJobObject(_handle, process.Handle);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _handle?.Dispose();
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr jobAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        SafeFileHandle job,
        uint informationClass,
        ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION information,
        int informationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
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
