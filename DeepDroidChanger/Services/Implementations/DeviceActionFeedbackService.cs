using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services;

public sealed class DeviceActionFeedbackService : IDeviceActionFeedbackService
{
    private static readonly TimeSpan BusyMessageDuration = TimeSpan.FromSeconds(1);

    private readonly ILocalizationService _localizationService;
    private readonly IDeviceProcessStateService _deviceProcessStateService;
    private readonly IDeviceActionCoordinatorService _deviceActionCoordinatorService;
    private readonly IDeviceActionEligibilityService _deviceActionEligibilityService;
    private readonly ILogger<DeviceActionFeedbackService> _logger;

    public DeviceActionFeedbackService(
        ILocalizationService localizationService,
        IDeviceProcessStateService deviceProcessStateService,
        IDeviceActionCoordinatorService deviceActionCoordinatorService,
        IDeviceActionEligibilityService deviceActionEligibilityService,
        ILogger<DeviceActionFeedbackService> logger)
    {
        _localizationService = localizationService;
        _deviceProcessStateService = deviceProcessStateService;
        _deviceActionCoordinatorService = deviceActionCoordinatorService;
        _deviceActionEligibilityService = deviceActionEligibilityService;
        _logger = logger;
    }

    public void SetProcess(string serial, string resourceKey, params object[] arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);

        string template = _localizationService.GetString(resourceKey);
        string message = arguments.Length == 0
            ? template
            : string.Format(template, arguments);
        _deviceProcessStateService.SetProcess(serial, message, resourceKey);
        _logger.LogInformation("Device {Serial} action: {Message}", serial, message);
    }

    public void ReportEligibilityFailure(
        string serial,
        DeviceActionEligibilityFailure failure)
    {
        if (failure == DeviceActionEligibilityFailure.Offline)
        {
            SetProcess(serial, "Log_DeviceMustBeOnline");
            return;
        }

        if (failure != DeviceActionEligibilityFailure.Busy)
            return;

        DeviceActionOperationSnapshot? operation =
            _deviceActionCoordinatorService.GetOperation(serial);
        if (operation == null)
            return;

        string message = string.Format(
            _localizationService.GetString("Log_DeviceActionAlreadyRunningFormat"),
            _localizationService.GetString(operation.Kind.GetDisplayResourceKey()));
        if (_deviceProcessStateService.Get(serial) == null)
        {
            _deviceProcessStateService.SetProcess(
                serial,
                _localizationService.GetString("Log_Ready"),
                "Log_Ready");
        }

        _deviceProcessStateService.ShowTemporaryProcess(
            serial,
            message,
            "Log_DeviceActionAlreadyRunningFormat",
            BusyMessageDuration);
        _logger.LogInformation(
            "Device {Serial} action attempt while {Action} is running.",
            serial,
            operation.Kind);
    }

    public void ReportDialogDismissed(string serial)
    {
        SetProcess(serial, "Log_ActionCanceled");
    }

    public void SetNonOwningProcess(string serial, string resourceKey, params object[] arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);

        if (!_deviceActionCoordinatorService.IsBusy(serial))
        {
            SetProcess(serial, resourceKey, arguments);
            return;
        }

        string template = _localizationService.GetString(resourceKey);
        string message = arguments.Length == 0
            ? template
            : string.Format(template, arguments);
        _deviceProcessStateService.ShowTemporaryProcess(
            serial,
            message,
            resourceKey,
            BusyMessageDuration);
        _logger.LogInformation("Non-owning device feedback for {Serial}: {Message}", serial, message);
    }

    public void ReportNonOwningDialogDismissed(string serial)
    {
        SetNonOwningProcess(serial, "Log_ActionCanceled");
    }

    public async Task ReportOperationCanceledAsync(
        string serial,
        DeviceActionCancellationReason reason,
        bool requiresOnline,
        CancellationToken statusCheckToken)
    {
        if (reason == DeviceActionCancellationReason.UserStop)
        {
            SetProcess(serial, "Log_ActionCanceledByUser");
            return;
        }

        if (requiresOnline)
        {
            DeviceActionEligibilityFailure failure = await _deviceActionEligibilityService
                .CheckAsync(serial, DeviceActionRequirement.Online, statusCheckToken)
                .ConfigureAwait(false);
            if (failure == DeviceActionEligibilityFailure.Offline)
            {
                SetProcess(serial, "Log_DeviceMustBeOnline");
                return;
            }
        }

        // External lifecycle cancellation is intentionally silent. It must not be
        // presented as either a user Stop request or a dismissed dialog.
    }
}
