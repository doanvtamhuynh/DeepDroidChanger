using System.IO;
using System.Runtime.InteropServices;

namespace ScrcpyNet;

public enum ScrcpyAudioState { Disabled, Starting, Playing, Unavailable }

/// <summary>Bounded, low-latency PCM output. SDL owns format conversion and the Windows device.</summary>
internal sealed class SdlPcmAudioPlayer : IDisposable
{
    private const int BytesPerSecond = 48000 * 2 * 2;
    private static readonly object InitGate = new();
    private static int users;
    private static IntPtr library;
    private IntPtr stream;
    private bool initialized;

    public SdlPcmAudioPlayer(string runtimeDirectory)
    {
        lock (InitGate)
        {
            // Load by absolute path, never from the current working directory.
            if (users == 0)
            {
                if (library == IntPtr.Zero) library = NativeLibrary.Load(Path.Combine(runtimeDirectory, "SDL3.dll"));
                if (!SDL_InitSubSystem(0x10)) throw new IOException("SDL audio initialization failed.");
            }
            users++;
            initialized = true;
        }
        try
        {
            AudioSpec spec = new() { Format = 0x8010, Channels = 2, Frequency = 48000 };
            stream = SDL_OpenAudioDeviceStream(uint.MaxValue, in spec, IntPtr.Zero, IntPtr.Zero);
            if (stream == IntPtr.Zero) throw new IOException("Could not open the PC audio output.");
        }
        catch { Dispose(); throw; }
    }

    public void Write(byte[] data, int count)
    {
        if (count % 4 != 0) throw new InvalidDataException("PCM data is not sample aligned.");
        int queued = SDL_GetAudioStreamQueued(stream);
        if (queued < 0) throw new IOException("Could not query the PC audio buffer.");
        // Drop stale samples after a stall instead of accumulating audible latency.
        if (queued + count > BytesPerSecond * 150 / 1000 && !SDL_ClearAudioStream(stream))
            throw new IOException("Could not reset the PC audio buffer.");
        if (!SDL_PutAudioStreamData(stream, data, count)) throw new IOException("PC audio playback failed.");
        if (SDL_GetAudioStreamQueued(stream) >= BytesPerSecond * 50 / 1000 && !SDL_ResumeAudioStreamDevice(stream))
            throw new IOException("Could not resume the PC audio output.");
    }

    public void Dispose()
    {
        if (stream != IntPtr.Zero) { SDL_DestroyAudioStream(stream); stream = IntPtr.Zero; }
        lock (InitGate)
        {
            if (!initialized) return;
            initialized = false;
            if (--users == 0) SDL_QuitSubSystem(0x10);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioSpec { public int Format; public int Channels; public int Frequency; }

    [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)] private static extern bool SDL_InitSubSystem(uint flags);
    [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)] private static extern void SDL_QuitSubSystem(uint flags);
    [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr SDL_OpenAudioDeviceStream(uint device, in AudioSpec spec, IntPtr callback, IntPtr userdata);
    [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)] private static extern void SDL_DestroyAudioStream(IntPtr stream);
    [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)] private static extern int SDL_GetAudioStreamQueued(IntPtr stream);
    [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)] private static extern bool SDL_ClearAudioStream(IntPtr stream);
    [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)] private static extern bool SDL_PutAudioStreamData(IntPtr stream, byte[] data, int length);
    [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)] private static extern bool SDL_ResumeAudioStreamDevice(IntPtr stream);
}
