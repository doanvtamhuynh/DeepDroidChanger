namespace DeepDroidChanger.Models;

public sealed record ApplicationPreferences(string Theme, string Language, bool SidebarCollapsed)
{
    public SettingsOptions Options { get; init; } = new();

    internal static ApplicationPreferences FromSettings(AppSettings settings) =>
        new(settings.Theme, settings.Language, settings.SidebarCollapsed) { Options = settings.Options };

    // Copy only preferences; device selection and layout belong to runtime state.
    internal void ApplyTo(AppSettings settings)
    {
        settings.Theme = Theme;
        settings.Language = Language;
        settings.SidebarCollapsed = SidebarCollapsed;
        settings.Options = Options;
    }
}

// Immutable snapshots: running operations keep the options they started with.
public sealed record SettingsOptions
{
    public AppearanceOptions Appearance { get; init; } = new();
    public string AccountDisplayName { get; init; } = string.Empty;
    public bool RememberAccount { get; init; } = true;
    public string SingleViewQuality { get; init; } = "Balanced";
    public string MultiViewQuality { get; init; } = "Lightweight";
    public string KeyboardMode { get; init; } = "Sdk";
    public bool SyncClipboard { get; init; } = true;
    public bool EnableAudio { get; init; }
    public bool AutoReconnect { get; init; } = true;
    public const string UnlimitedBatchConcurrency = "Unlimited";
    public string MaxConcurrentTools { get; init; } = UnlimitedBatchConcurrency;
    public string BatchDelay { get; init; } = "0";
    public bool ContinueOnError { get; init; } = true;
    public string ScreenshotDestination { get; init; } = "Ask";
    public string ScreenshotFolder { get; init; } = string.Empty;
    public string ExportDestination { get; init; } = "Ask";
    public string ExportFolder { get; init; } = string.Empty;
    public string BackupFolder { get; init; } = "Backup";
}

public sealed record AppearanceOptions
{
    public string SelectedButtonColor { get; init; } = "Theme";
    public string CustomButtonHex { get; init; } = "#3B82F6";
    public string SelectedTextColor { get; init; } = "Theme";
    public string CustomTextHex { get; init; } = "#F8FAFC";
    public string FontFamily { get; init; } = "Segoe UI Variable, Segoe UI";
    public string TypeStyle { get; init; } = "Regular";
    public double FontSize { get; init; } = 14;
    public string ButtonVariant { get; init; } = "Filled";
    public string ButtonSize { get; init; } = "Comfortable";
}
