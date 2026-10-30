using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeepDroidChanger.Services;
using DeepDroidChanger.ViewDevices.Contracts;
using DeepDroidChanger.ViewDevices.Models;
using Microsoft.Extensions.Logging;
using ScrcpyNet;

namespace DeepDroidChanger.ViewModels;

/// <summary>Controls shared by a dedicated viewer and an individual multi-view tile.</summary>
public sealed class ViewDeviceControlsViewModel : ObservableObject, IDisposable
{
    private readonly IViewDeviceInteractionService? interaction;
    private readonly IViewDeviceClipboardService clipboard;
    private readonly ILocalizationService localization;
    private readonly IUiDispatcherService dispatcher;
    private readonly ILogger logger;
    private readonly Func<string, CancellationToken, Task>? installPackage;
    private readonly List<IRelayCommand> commands = [];
    private ISingleViewDeviceSession? session;
    private IDisposable? registration;
    private CancellationTokenSource? sessionCancellation;
    private EventHandler? featureHandler;
    private string statusKey = "ViewDevice_FeaturesReady";

    public ViewDeviceControlsViewModel(IViewDeviceInteractionService? interaction,
        IViewDeviceClipboardService clipboard, ILocalizationService localization,
        IUiDispatcherService dispatcher, ILogger logger, Func<string, CancellationToken, Task>? installPackage = null)
    {
        this.interaction = interaction;
        this.clipboard = clipboard;
        this.localization = localization;
        this.dispatcher = dispatcher;
        this.logger = logger;
        this.installPackage = installPackage;
        PasteCommand = Command((s, t) => PasteAsync(s, t));
        BackCommand = Command((s, t) => s.SendBackOrScreenOnAsync(t));
        ScreenOffCommand = Command((s, t) => s.SetScreenPowerModeAsync(AndroidScreenPowerMode.POWER_MODE_OFF, t));
        ScreenOnCommand = Command((s, t) => s.SetScreenPowerModeAsync(AndroidScreenPowerMode.POWER_MODE_NORMAL, t));
        NotificationsCommand = Command((s, t) => s.ExpandNotificationPanelAsync(t));
        SettingsPanelCommand = Command((s, t) => s.ExpandSettingsPanelAsync(t));
        CollapsePanelsCommand = Command((s, t) => s.CollapsePanelsAsync(t));
        SendKeyCommand = new AsyncRelayCommand<int>(key => RunAsync((s, t) => s.SendKeyEventAsync(key, t)), _ => CanInteract);
        commands.Add(SendKeyCommand);
        ToggleAudioCommand = Command((s, t) => interaction?.ToggleAudioAsync(s, t) ?? Task.CompletedTask, true);
        DropFilesCommand = new AsyncRelayCommand<string[]>(files => RunAsync(async (s, t) =>
        {
            foreach (string file in files ?? [])
            {
                t.ThrowIfCancellationRequested();
                if (!ReferenceEquals(session, s)) return;
                string extension = Path.GetExtension(file);
                if (extension.Equals(".apk", StringComparison.OrdinalIgnoreCase) || extension.Equals(".xapk", StringComparison.OrdinalIgnoreCase))
                {
                    if (installPackage is null) throw new NotSupportedException();
                    Task install = Task.CompletedTask;
                    await dispatcher.InvokeAsync(() => install = installPackage(file, t), t).ConfigureAwait(false);
                    await install.ConfigureAwait(false);
                }
                else
                {
                    if (interaction is null) throw new NotSupportedException();
                    await interaction.PushFileAsync(s, file, t).ConfigureAwait(false);
                }
            }
            await dispatcher.InvokeAsync(() => ReportStatus("ViewDevice_FileTransferred"), t).ConfigureAwait(false);
        }), files => CanInteract && files is { Length: > 0 });
        commands.Add(DropFilesCommand);
        localization.LanguageChanged += OnLanguageChanged;
    }

    public string StatusText => statusKey == "ViewDevice_FeaturesReady" ? string.Empty : localization.GetString(statusKey);
    public bool IsPaused => session?.Client?.IsVideoPaused == true;
    public bool IsAudioEnabled => session?.Client?.AudioEnabled == true;
    public bool CanInteract => CanUseFeatures && session?.Client?.ControlEnabled == true && !IsPaused;
    private bool CanUseFeatures => session?.State == SingleViewDeviceSessionState.Running;
    public IAsyncRelayCommand PasteCommand { get; }
    public IAsyncRelayCommand BackCommand { get; }
    public IAsyncRelayCommand ScreenOffCommand { get; }
    public IAsyncRelayCommand ScreenOnCommand { get; }
    public IAsyncRelayCommand NotificationsCommand { get; }
    public IAsyncRelayCommand SettingsPanelCommand { get; }
    public IAsyncRelayCommand CollapsePanelsCommand { get; }
    public IAsyncRelayCommand<int> SendKeyCommand { get; }
    public IAsyncRelayCommand ToggleAudioCommand { get; }
    public IAsyncRelayCommand<string[]> DropFilesCommand { get; }

    private IAsyncRelayCommand Command(Func<ISingleViewDeviceSession, CancellationToken, Task> action, bool allowPaused = false)
    {
        AsyncRelayCommand command = new(() => RunAsync(action), () => allowPaused ? CanUseFeatures : CanInteract);
        commands.Add(command);
        return command;
    }

    public void Attach(ISingleViewDeviceSession value, Func<Task> reconnect)
    {
        Detach();
        session = value;
        sessionCancellation = new();
        registration = interaction?.Register(value, reconnect, ReportStatus);
        if (value.Client is { } client)
        {
            featureHandler = (_, _) => _ = dispatcher.InvokeAsync(() =>
            {
                if (ReferenceEquals(session, value)) Refresh();
            });
            client.FeaturesChanged += featureHandler;
        }
        Refresh();
    }

    public void Detach()
    {
        if (session?.Client is { } client && featureHandler is not null) client.FeaturesChanged -= featureHandler;
        registration?.Dispose();
        registration = null;
        sessionCancellation?.Cancel();
        // In-flight commands retain their own token until they finish.
        sessionCancellation = null;
        session = null;
        featureHandler = null;
        statusKey = "ViewDevice_FeaturesReady";
        OnPropertyChanged(nameof(StatusText));
        Refresh();
    }

    public void Activate()
    {
        if (session is { } current) interaction?.Activate(current);
    }

    private async Task RunAsync(Func<ISingleViewDeviceSession, CancellationToken, Task> action)
    {
        ISingleViewDeviceSession? current = session;
        CancellationToken token = sessionCancellation?.Token ?? new CancellationToken(true);
        if (current is null || token.IsCancellationRequested) return;
        Activate();
        try { await action(current, token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Device control failed for {Serial}.", current.Serial);
            string key = exception.Message.Contains("ViewDevice_ClipboardTooLarge", StringComparison.Ordinal) ? "ViewDevice_ClipboardTooLarge"
                : "ViewDevice_ControlFailed";
            await dispatcher.InvokeAsync(() =>
            {
                if (ReferenceEquals(session, current)) ReportStatus(key);
            }).ConfigureAwait(false);
        }
    }

    private async Task PasteAsync(ISingleViewDeviceSession current, CancellationToken token)
    {
        string? text = await clipboard.GetTextAsync(token).ConfigureAwait(false);
        if (text is null || !ReferenceEquals(session, current)) return;
        await current.PasteHostClipboardAsync(text, token).ConfigureAwait(false);
    }



    private void ReportStatus(string key) { statusKey = key; OnPropertyChanged(nameof(StatusText)); Refresh(); }
    private void OnLanguageChanged(object? sender, EventArgs args) => _ = dispatcher.InvokeAsync(() => OnPropertyChanged(nameof(StatusText)));
    private void Refresh()
    {
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(IsAudioEnabled)); OnPropertyChanged(nameof(CanInteract));
        foreach (IRelayCommand command in commands) command.NotifyCanExecuteChanged();
    }

    public void Dispose() { localization.LanguageChanged -= OnLanguageChanged; Detach(); }
}
