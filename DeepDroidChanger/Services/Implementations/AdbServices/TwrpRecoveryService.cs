using DeepDroidChanger.Models;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services;

public sealed partial class TwrpRecoveryService(
    IAdbCommandService adb, IAdbRootAccessService root, IDeviceActionCoordinatorService coordinator,
    TwrpSessionStore store, TwrpImageService images, ILogger<TwrpRecoveryService> logger) : ITwrpRecoveryService
{
    public string DefaultRescueDirectory => store.CacheRoot;
    public string? GetDefaultImagePath(string deviceMarker) => images.GetDefaultImagePath(deviceMarker);
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);
    internal TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);
    internal TimeSpan RecoveryTimeout { get; set; } = TimeSpan.FromHours(3);

    public async Task<TwrpSession> RunAsync(TwrpRequest request, IProgress<TwrpProgress>? progress, CancellationToken token)
    {
        // New operations use Data ZIPs. Legacy boot-writing code is retained only
        // for reconciliation/recovery of already persisted sessions.
        if (request.SourceBackupId is not null) throw new TwrpWorkflowException("Twrp_RestoreFormatRequired");
        return await RunDataAsync(request, progress, token).ConfigureAwait(false);
    }

}
