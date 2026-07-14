using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services
{
    public interface IAdbCommandService
    {
        Task<CommandResult> RunAdbAsync(string arguments, CancellationToken cancellationToken);
        Task<CommandResult> RunAdbAsync(string serial, string arguments, CancellationToken cancellationToken);
        Task<CommandResult> RunAdbShellAsync(string serial, string shellCommand, CancellationToken cancellationToken);
        Task<CommandResult> RunFastbootAsync(string arguments, CancellationToken cancellationToken);

        Task<string> GetPropertyAsync(string serial, string propertyName, CancellationToken cancellationToken);
        Task SetPropertyAsync(string serial, string propertyName, string value, CancellationToken cancellationToken);
        Task<string> GetSettingAsync(string serial, string namespaceName, string key, CancellationToken cancellationToken);
        Task PutSettingAsync(string serial, string namespaceName, string key, string value, CancellationToken cancellationToken);
        Task DeleteSettingAsync(string serial, string namespaceName, string key, CancellationToken cancellationToken);
        Task BroadcastAsync(string serial, string action, CancellationToken cancellationToken);
        Task<string> CurlAsync(string serial, string url, CancellationToken cancellationToken);
        Task SendKeyEventAsync(string serial, int keyCode, CancellationToken cancellationToken);
        Task SendTextAsync(string serial, string text, CancellationToken cancellationToken);
        Task RebootAsync(string serial, CancellationToken cancellationToken);

        Task SetWifiAsync(string serial, bool enabled, CancellationToken cancellationToken);
        Task OpenPackageAsync(string serial, string packageName, CancellationToken cancellationToken);
        Task ForceStopPackageAsync(string serial, string packageName, CancellationToken cancellationToken);
        Task ClearPackageAsync(string serial, string packageName, CancellationToken cancellationToken);
        Task OpenWifiSettingsAsync(string serial, CancellationToken cancellationToken);
        Task OpenLinkAsync(string serial, string url, CancellationToken cancellationToken);
        Task ClearGlobalHttpProxyAsync(string serial, CancellationToken cancellationToken);
    }
}
