namespace DeepDroidChanger.Services;

public interface IDeviceActionFeedbackService
{
    void SetProcess(string serial, string resourceKey, params object[] arguments);

    void ReportEligibilityFailure(
        string serial,
        DeviceActionEligibilityFailure failure);

    void ReportDialogDismissed(string serial);

    /// <summary>
    /// Reports a message from work that does not own the device operation.
    /// When another action owns the device, the message is temporary and the
    /// owner presentation is restored afterward.
    /// </summary>
    void SetNonOwningProcess(string serial, string resourceKey, params object[] arguments);

    void ReportNonOwningDialogDismissed(string serial);

    Task ReportOperationCanceledAsync(
        string serial,
        DeviceActionCancellationReason reason,
        bool requiresOnline,
        CancellationToken statusCheckToken);
}
