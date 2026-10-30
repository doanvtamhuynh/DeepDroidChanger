using System.IO;
using System.Text;
using DeepDroidChanger.ViewDevices.Contracts;
using DeepDroidChanger.ViewDevices.Models;
using Microsoft.Extensions.Logging;
using ScrcpyNet;

namespace DeepDroidChanger.Services;

/// <summary>Application-wide ownership of device clipboard, audio and per-device input preferences.</summary>
public sealed class ViewDeviceInteractionService(
    IViewDeviceClipboardService clipboard,
    IUiDispatcherService dispatcher,
    IAdbCommandService adb,
    ILogger<ViewDeviceInteractionService> logger) : IViewDeviceInteractionService
{
    private sealed class Preferences { public ViewDeviceKeyboardMode Keyboard; public bool Audio; }
    private sealed class Registration(
        ViewDeviceInteractionService owner, ISingleViewDeviceSession session,
        Func<Task> reconnect, Action<string> report) : IDisposable
    {
        public ISingleViewDeviceSession Session { get; } = session;
        public Func<Task> Reconnect { get; } = reconnect;
        public Action<string> Report { get; } = report;
        public Scrcpy? Client { get; } = session.Client;
        public CancellationTokenSource Cancellation { get; } = new();
        public void Dispose() => owner.Unregister(this);
    }

    private readonly object gate = new();
    private readonly Dictionary<string, Preferences> preferences = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Registration> registrations = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim clipboardGate = new(1, 1);
    private readonly SemaphoreSlim configurationGate = new(1, 1);
    private string? activeSerial;
    private Registration? active;
    private long activationGeneration;
    private string? audioSerial;

    private Preferences GetPreferences(string serial)
    {
        if (!preferences.TryGetValue(serial, out Preferences? value)) preferences[serial] = value = new();
        return value;
    }

    public ViewDeviceLaunchOptions PrepareOptions(ViewDeviceLaunchOptions options)
    {
        lock (gate)
        {
            Preferences value = GetPreferences(options.Serial);
            return options with { KeyboardMode = value.Keyboard, NoAudio = !value.Audio };
        }
    }

    public IDisposable Register(ISingleViewDeviceSession session, Func<Task> reconnect, Action<string> reportStatus)
    {
        Registration registration = new(this, session, reconnect, reportStatus);
        lock (gate)
        {
            if (registrations.TryGetValue(session.Serial, out Registration? old)) Unregister(old);
            registrations[session.Serial] = registration;
            if (session.UhidFallback) GetPreferences(session.Serial).Keyboard = ViewDeviceKeyboardMode.Sdk;
            if (string.Equals(activeSerial, session.Serial, StringComparison.OrdinalIgnoreCase))
            { active = registration; activationGeneration++; }
            session.ClipboardChanged += OnClipboardChanged;
            session.FeaturesChanged += OnFeaturesChanged;
        }
        ReportFeatures(registration);
        return registration;
    }

    private void Unregister(Registration registration)
    {
        lock (gate)
        {
            if (!registrations.TryGetValue(registration.Session.Serial, out Registration? current) || !ReferenceEquals(current, registration)) return;
            registrations.Remove(registration.Session.Serial);
            registration.Session.ClipboardChanged -= OnClipboardChanged;
            registration.Session.FeaturesChanged -= OnFeaturesChanged;
            registration.Cancellation.Cancel();
            if (ReferenceEquals(active, registration)) { active = null; activationGeneration++; }
        }
        // CancellationTokenSource remains alive for an in-flight clipboard callback.
    }

    public void Activate(ISingleViewDeviceSession session)
    {
        lock (gate)
        {
            if (!registrations.TryGetValue(session.Serial, out Registration? current) || !ReferenceEquals(current.Session, session)) return;
            if (ReferenceEquals(active, current)) return;
            activeSerial = session.Serial;
            active = current;
            activationGeneration++;
        }
    }

    private void OnClipboardChanged(object? sender, ScrcpyClipboardChangedEventArgs args)
    {
        Registration registration;
        long generation;
        lock (gate)
        {
            if (active is null || !ReferenceEquals(active.Session, sender)) return;
            registration = active;
            generation = activationGeneration;
        }
        _ = PublishClipboardAsync(registration, generation, args.Text);
    }

    private async Task PublishClipboardAsync(Registration registration, long generation, string text)
    {
        CancellationToken token = registration.Cancellation.Token;
        try
        {
            await clipboardGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                string? current = await clipboard.GetTextAsync(token).ConfigureAwait(false);
                if (string.Equals(current, text, StringComparison.Ordinal)) return;
                await clipboard.TrySetTextAsync(text, write =>
                {
                    lock (gate)
                    {
                        if (token.IsCancellationRequested || !ReferenceEquals(active, registration) || generation != activationGeneration ||
                            registration.Session.State != SingleViewDeviceSessionState.Running) return false;
                        write();
                        return true;
                    }
                }, token).ConfigureAwait(false);
            }
            finally { clipboardGate.Release(); }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Device clipboard synchronization failed for {Serial}.", registration.Session.Serial);
            await ReportAsync(registration, "ViewDevice_ClipboardFailed").ConfigureAwait(false);
        }
    }

    private Registration? Find(ISingleViewDeviceSession session)
    {
        lock (gate) return registrations.TryGetValue(session.Serial, out Registration? current) && ReferenceEquals(current.Session, session) ? current : null;
    }

    public async Task ToggleAudioAsync(ISingleViewDeviceSession session, CancellationToken cancellationToken)
    {
        await configurationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Registration? target = Find(session);
            if (target is null) return;
            Registration? previous = null;
            bool enable;
            lock (gate)
            {
                Preferences value = GetPreferences(session.Serial);
                enable = !value.Audio;
                if (enable && audioSerial is not null && !string.Equals(audioSerial, session.Serial, StringComparison.OrdinalIgnoreCase))
                {
                    GetPreferences(audioSerial).Audio = false;
                    registrations.TryGetValue(audioSerial, out previous);
                }
                value.Audio = enable;
                audioSerial = enable ? session.Serial : null;
            }
            // The old stream must stop before a new device may start output.
            if (previous is not null)
            {
                try
                {
                    await previous.Reconnect().ConfigureAwait(false);
                    if (previous.Client is { Connected: true, AudioEnabled: true })
                        throw new InvalidOperationException("The previous audio session has not stopped.");
                }
                catch
                {
                    lock (gate)
                    {
                        GetPreferences(session.Serial).Audio = false;
                        GetPreferences(previous.Session.Serial).Audio = true;
                        audioSerial = previous.Session.Serial;
                    }
                    throw;
                }
            }
            if (Find(session) is not null) await target.Reconnect().ConfigureAwait(false);
        }
        finally { configurationGate.Release(); }
    }

    public async Task SetKeyboardModeAsync(ISingleViewDeviceSession session, ViewDeviceKeyboardMode mode, CancellationToken cancellationToken)
    {
        await configurationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Registration? target = Find(session);
            if (target is null) return;
            lock (gate) GetPreferences(session.Serial).Keyboard = mode;
            await target.Reconnect().ConfigureAwait(false);
        }
        finally { configurationGate.Release(); }
    }

    private void OnFeaturesChanged(object? sender, EventArgs args)
    {
        if (sender is not ISingleViewDeviceSession session || Find(session) is not { } registration) return;
        lock (gate) if (session.UhidFallback) GetPreferences(session.Serial).Keyboard = ViewDeviceKeyboardMode.Sdk;
        ReportFeatures(registration);
    }

    private void ReportFeatures(Registration registration)
    {
        string key = registration.Session.AudioState switch
        {
            ScrcpyAudioState.Starting => "ViewDevice_AudioStarting",
            ScrcpyAudioState.Playing => "ViewDevice_AudioPlaying",
            ScrcpyAudioState.Unavailable => "ViewDevice_AudioUnavailable",
            _ => registration.Session.UhidFallback ? "ViewDevice_UhidFallback" : "ViewDevice_FeaturesReady"
        };
        _ = ReportAsync(registration, key);
        if (registration.Session.AudioState == ScrcpyAudioState.Unavailable)
            _ = DisableUnavailableAudioAsync(registration);
    }

    private async Task DisableUnavailableAudioAsync(Registration registration)
    {
        CancellationToken token = registration.Cancellation.Token;
        try
        {
            await configurationGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                lock (gate)
                {
                    if (Find(registration.Session) is null || !GetPreferences(registration.Session.Serial).Audio) return;
                    GetPreferences(registration.Session.Serial).Audio = false;
                    if (audioSerial == registration.Session.Serial) audioSerial = null;
                }
                // Release capture on Android as well as the PC output, without dropping video permanently.
                await registration.Reconnect().ConfigureAwait(false);
                Registration? current;
                lock (gate) registrations.TryGetValue(registration.Session.Serial, out current);
                if (current is not null) await ReportAsync(current, "ViewDevice_AudioUnavailable").ConfigureAwait(false);
            }
            finally { configurationGate.Release(); }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) { logger.LogWarning(exception, "Could not disable unavailable audio for {Serial}.", registration.Session.Serial); }
    }

    private async Task ReportAsync(Registration registration, string key)
    {
        try
        {
            await dispatcher.InvokeAsync(() =>
            {
                if (Find(registration.Session) is not null) registration.Report(key);
            }).ConfigureAwait(false);
        }
        catch (Exception exception) { logger.LogDebug(exception, "Could not report the device feature state."); }
    }

    public async Task PushFileAsync(ISingleViewDeviceSession session, string localPath, CancellationToken cancellationToken)
    {
        if (Find(session) is null || session.State != SingleViewDeviceSessionState.Running) throw new InvalidOperationException("Device session is unavailable.");
        if (!File.Exists(localPath)) throw new FileNotFoundException("The dropped file no longer exists.");
        string extension = Path.GetExtension(localPath);
        if (extension.Equals(".apk", StringComparison.OrdinalIgnoreCase) || extension.Equals(".xapk", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Package files must use the package installer.");
        string stem = Path.GetFileNameWithoutExtension(localPath);
        StringBuilder safeStem = new();
        int bytes = 0;
        foreach (Rune rune in stem.EnumerateRunes())
        {
            if (bytes + rune.Utf8SequenceLength > 100) break;
            safeStem.Append(rune.ToString()); bytes += rune.Utf8SequenceLength;
        }
        if (Encoding.UTF8.GetByteCount(extension) > 40) extension = string.Empty;
        string destination = $"/sdcard/Download/{safeStem}-{Guid.NewGuid():N}{extension}";
        var result = await adb.PushFileAsync(session.Serial, localPath, destination, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0) throw new IOException("The file transfer failed.");
        if (Find(session) is not null) await session.ScanFileAsync(destination, cancellationToken).ConfigureAwait(false);
    }
}
