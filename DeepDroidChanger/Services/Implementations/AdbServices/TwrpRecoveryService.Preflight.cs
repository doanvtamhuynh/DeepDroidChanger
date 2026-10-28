using DeepDroidChanger.Models;
using DeepDroidChanger.Constants;

namespace DeepDroidChanger.Services;

public sealed partial class TwrpRecoveryService
{
    // Configuration only, before the user submits an operation. No receipt or fallback.
    public async Task<string> GetDeviceMarkerAsync(string serial, CancellationToken token) =>
        (await adb.GetPropertyAsync(serial, PropertyConstants.DeepDroidDevice, token).ConfigureAwait(false)).Trim();

    private IDeviceActionOperation Start(string serial, CancellationToken token) =>
        coordinator.TryStart(serial, DeviceActionKind.TwrpRecovery, canCancel: true, token, source: DeviceActionSource.SingleDevice)
        ?? throw new TwrpWorkflowException("Twrp_DeviceBusy");
}
