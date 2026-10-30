using ScrcpyNet;
using DeepDroidChanger.ViewDevices.Models;

namespace DeepDroidChanger.ViewDevices.Contracts;

public interface ISingleViewDeviceSession : IAsyncDisposable
{
    string Serial { get; }
    SingleViewDeviceSessionState State { get; }
    Scrcpy? Client { get; }
    int ContentWidth { get; }
    int ContentHeight { get; }
    IReadOnlyList<string> RecentDiagnostics { get; }
    ViewDeviceKeyboardMode KeyboardMode => ViewDeviceKeyboardMode.Sdk;
    bool UhidFallback => false;
    ScrcpyAudioState AudioState => Client?.AudioState ?? ScrcpyAudioState.Disabled;
    event EventHandler? FeaturesChanged { add { } remove { } }
    Task ResetVideoAsync(CancellationToken cancellationToken = default) => Task.FromException(new NotSupportedException());
    Task OpenKeyboardSettingsAsync(CancellationToken cancellationToken = default) => Task.FromException(new NotSupportedException());
    Task ScanFileAsync(string path, CancellationToken cancellationToken = default) => Task.FromException(new NotSupportedException());

    event EventHandler<SingleViewDeviceSessionStateChangedEventArgs>? StateChanged;
    event EventHandler<SingleViewDeviceContentSizeChangedEventArgs>? ContentSizeChanged;
    event EventHandler<ScrcpyClipboardChangedEventArgs>? ClipboardChanged;
    event EventHandler? Exited;

    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task FlushControlAsync(CancellationToken cancellationToken = default);
    Task SendKeyEventAsync(int keyCode, CancellationToken cancellationToken = default);
    Task SendBackOrScreenOnAsync(CancellationToken cancellationToken = default);
    Task SetScreenPowerModeAsync(
        AndroidScreenPowerMode mode,
        CancellationToken cancellationToken = default);
    Task RotateDeviceAsync(CancellationToken cancellationToken = default);
    Task PasteHostClipboardAsync(string text, CancellationToken cancellationToken = default);
    Task PasteHostClipboardWithPasteKeyAsync(string text, CancellationToken cancellationToken = default);
    Task InjectTextAsync(string text, CancellationToken cancellationToken = default);
    Task RequestClipboardAsync(
        ScrcpyCopyKey copyKey,
        CancellationToken cancellationToken = default);
    Task ExpandNotificationPanelAsync(CancellationToken cancellationToken = default);
    Task ExpandSettingsPanelAsync(CancellationToken cancellationToken = default);
    Task CollapsePanelsAsync(CancellationToken cancellationToken = default);
}
