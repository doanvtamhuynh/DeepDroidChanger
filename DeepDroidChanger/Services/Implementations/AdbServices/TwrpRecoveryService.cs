using DeepDroidChanger.Models;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services;

public sealed partial class TwrpRecoveryService(
    IAdbCommandService adb, IDeviceActionCoordinatorService coordinator,
    TwrpOperationCache cache, TwrpImageService images, ILogger<TwrpRecoveryService> logger) : ITwrpRecoveryService
{
    public string? GetDefaultImagePath(string deviceMarker) => images.GetDefaultImagePath(deviceMarker);
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);
    internal TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);
    internal TimeSpan RecoveryTimeout { get; set; } = TimeSpan.FromHours(3);

    public async Task<TwrpOperation> RunAsync(TwrpRequest request, IProgress<TwrpProgress>? progress, CancellationToken token)
    {
        return await RunDataAsync(request, progress, token).ConfigureAwait(false);
    }

}
