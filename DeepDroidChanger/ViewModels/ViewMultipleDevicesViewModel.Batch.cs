using System.Globalization;
using DeepDroidChanger.Models;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.ViewModels;

public sealed partial class ViewMultipleDevicesViewModel
{
    private const int MaxConcurrentTools = 4;

    private sealed record ToolRequest(ToolAction Action, string Text, string Shell, string Source, string AndroidPath, string OutputRoot, Guid SessionId);
    private sealed record ToolTarget(ViewMultipleDeviceItemViewModel Item, int Index, ViewMultipleDeviceToolResultViewModel Result);

    private CancellationTokenSource? _batchCancellation;
    private Task _batchTask = Task.CompletedTask;
    private bool _isBatchBusy;
    private string _activeToolKey = string.Empty;
    private int _batchTotal;
    private int _batchCompleted;

    public bool IsBatchBusy => Volatile.Read(ref _isBatchBusy);
    public bool CanChangeLayout => !IsBatchBusy;
    public bool HasToolResults => ToolResults.Count != 0;
    public int BackgroundSessionCount => OnlineDevices.Count(item => !VisibleDevices.Contains(item) && item.KeepSessionInBackground && item.IsRunning);
    public string ActiveToolText => string.IsNullOrEmpty(_activeToolKey) ? string.Empty : _localization.GetString(_activeToolKey);
    public string BatchSummary => string.Format(CultureInfo.CurrentCulture,
        _localization.GetString("ViewMultipleDevices_BatchProgress"), _batchCompleted, _batchTotal,
        ToolResults.Count(result => result.StatusKey is "ViewMultipleDevices_ToolSent" or "ViewMultipleDevices_ToolSucceeded"),
        ToolResults.Count(result => result.StatusKey == "ViewMultipleDevices_ToolFailed"),
        ToolResults.Count(result => result.StatusKey == "ViewMultipleDevices_ToolSkipped"),
        ToolResults.Count(result => result.StatusKey == "ViewMultipleDevices_ToolCanceled"),
        ToolResults.Count(result => result.StatusKey == "ViewMultipleDevices_ToolUnverified"));

    private void SetBatchBusy(bool value)
    {
        SetProperty(ref _isBatchBusy, value, nameof(IsBatchBusy));
        OnPropertyChanged(nameof(CanChangeLayout));
        NotifyToolCommands();
        NotifyPaginationCommands();
        ResetOrderCommand.NotifyCanExecuteChanged();
        EditOrderCommand.NotifyCanExecuteChanged();
    }

    private async Task StopBatchAsync()
    {
        CancelCancellation(_batchCancellation);
        await _batchTask.ConfigureAwait(false);
    }

    private ToolRequest? PrepareToolRequest(ToolAction action)
    {
        string source = ComputerFilePath;
        string outputRoot = string.Empty;
        if (action == ToolAction.Install)
        {
            source = _toolPicker!.ShowOpenFileDialog(_localization.GetString("ViewDevice_InstallFilter"), _localization.GetString("ViewDevice_InstallTitle")) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(source)) return null;
        }
        else if (action == ToolAction.Import && !_toolFiles!.FileExists(source))
        {
            source = _toolPicker?.ShowOpenFileDialog(_localization.GetString("ViewDevice_FileFilter"), _localization.GetString("ViewDevice_ImportTitle")) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(source)) return null;
            ComputerFilePath = source;
        }
        else if (action is ToolAction.Screenshot or ToolAction.Export)
        {
            outputRoot = _toolPicker!.ShowOpenFolderDialog(_localization.GetString("ViewMultipleDevices_OutputFolder")) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(outputRoot)) return null;
        }
        return new ToolRequest(action, InputText, AdbShellCommand.Trim(), source, AndroidFilePath.Trim(), outputRoot, Guid.NewGuid());
    }

    private async Task RunToolBatchAsync(ToolAction action)
    {
        if (!CanRunTool(action))
            return;

        ToolTarget[] targets = _deviceStack.Where(item => item.IsSelected)
            .Select(item => new ToolTarget(item, item.Index,
                new ViewMultipleDeviceToolResultViewModel(item.Serial, item.DeviceName, _localization))).ToArray();
        ToolRequest? request;
        try
        {
            request = PrepareToolRequest(action);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not prepare a multiple-device tool action.");
            InitializeBatchResults(action, targets);
            foreach (ToolTarget target in targets)
                target.Result.Update("ViewMultipleDevices_ToolFailed", exception.Message);

            _batchCompleted = targets.Length;
            OnPropertyChanged(nameof(BatchSummary));
            return;
        }
        if (request is null || IsBatchBusy || !Volatile.Read(ref _isActive))
            return;

        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _activationCancellation?.Token ?? _lifetimeCancellation.Token);
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _batchTask = completion.Task;
        _batchCancellation = cancellation;
        InitializeBatchResults(action, targets);
        SetBatchBusy(true);
        try
        {
            string runDirectory = request.OutputRoot.Length == 0 ? string.Empty : _toolArtifacts!.CreateRunDirectory(request.OutputRoot);
            using SemaphoreSlim throttle = new(MaxConcurrentTools, MaxConcurrentTools);
            await Task.WhenAll(targets.Select(target => Task.Run(
                () => RunToolTargetAsync(target, request, runDirectory, throttle, cancellation.Token)))).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Multiple-device tool batch failed.");
            try
            {
                await _uiDispatcher.InvokeAsync(() =>
                {
                    foreach (ToolTarget target in targets.Where(target => target.Result.StatusKey == "ViewMultipleDevices_ToolQueued"))
                        target.Result.Update("ViewMultipleDevices_ToolFailed", exception.Message);
                    _batchCompleted = targets.Length;
                    OnPropertyChanged(nameof(BatchSummary));
                }).ConfigureAwait(false);
            }
            catch (Exception dispatcherException)
            {
                _logger.LogDebug(dispatcherException, "The tool result dispatcher is shutting down.");
            }
        }
        finally
        {
            try
            {
                await _uiDispatcher.InvokeAsync(() =>
                {
                    _batchCancellation = null;
                    SetBatchBusy(false);
                    QueueTrackerRefresh();
                }).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "Could not refresh tools during shutdown.");
            }
            finally
            {
                completion.TrySetResult();
            }
        }
    }

    private void InitializeBatchResults(ToolAction action, ToolTarget[] targets)
    {
        _batchTotal = targets.Length;
        _batchCompleted = 0;
        _activeToolKey = ToolTitleKey(action);
        ToolResults.Clear();
        foreach (ToolTarget target in targets)
            ToolResults.Add(target.Result);

        OnPropertyChanged(nameof(HasToolResults));
        OnPropertyChanged(nameof(ActiveToolText));
        OnPropertyChanged(nameof(BatchSummary));
    }

    private async Task RunToolTargetAsync(
        ToolTarget target,
        ToolRequest request,
        string runDirectory,
        SemaphoreSlim throttle,
        CancellationToken token)
    {
        bool entered = false;
        try
        {
            await throttle.WaitAsync(token).ConfigureAwait(false);
            entered = true;
            token.ThrowIfCancellationRequested();
            string? unavailable = ToolUnavailableReason(target.Item);
            if (unavailable is not null)
            {
                await UpdateToolResultAsync(target.Result, "ViewMultipleDevices_ToolSkipped", _localization.GetString(unavailable)).ConfigureAwait(false);
                return;
            }

            await _uiDispatcher.InvokeAsync(() => target.Result.Update("ViewMultipleDevices_ToolRunning")).ConfigureAwait(false);
            string detail = await ExecuteToolAsync(target, request, runDirectory, token).ConfigureAwait(false);
            await UpdateToolResultAsync(target.Result, ToolSuccessStatusKey(request.Action), detail).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await UpdateToolResultAsync(target.Result, "ViewMultipleDevices_ToolCanceled").ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Multiple device {Action} failed for {Serial}.", request.Action, target.Item.Serial);
            string? unavailable = ToolUnavailableReason(target.Item);
            string statusKey = unavailable is not null ? "ViewMultipleDevices_ToolSkipped" :
                exception is ToolVerificationException ? "ViewMultipleDevices_ToolUnverified" : "ViewMultipleDevices_ToolFailed";
            string message = unavailable ?? exception.Message;
            string detail = message.StartsWith("ViewMultipleDevices_", StringComparison.Ordinal)
                ? _localization.GetString(message) : message;
            await UpdateToolResultAsync(target.Result, statusKey, detail).ConfigureAwait(false);
        }
        finally
        {
            if (entered)
                throttle.Release();
        }
    }

    private static string ToolSuccessStatusKey(ToolAction action) => action switch
    {
        ToolAction.Power or ToolAction.VolumeUp or ToolAction.VolumeDown or ToolAction.ScreenOn or
            ToolAction.ScreenOff or ToolAction.InputText or ToolAction.Reboot => "ViewMultipleDevices_ToolSent",
        _ => "ViewMultipleDevices_ToolSucceeded"
    };

    private Task UpdateToolResultAsync(ViewMultipleDeviceToolResultViewModel result, string key, string details = "") =>
        _uiDispatcher.InvokeAsync(() =>
        {
            result.Update(key, details);
            _batchCompleted++;
            OnPropertyChanged(nameof(BatchSummary));
            OnPropertyChanged(nameof(BackgroundSessionCount));
        });

    private string? ToolUnavailableReason(ViewMultipleDeviceItemViewModel item)
    {
        if (item.IsDisposed || _deviceTracker.Health != AdbDeviceTrackerHealth.Connected || _deviceTracker.GetDevice(item.Serial)?.Status != AdbDeviceStatus.Online)
            return "ViewMultipleDevices_DeviceOffline";
        if (_presentationCoordinator.IsDedicatedViewActive(item.Serial))
            return "ViewMultipleDevices_OpenedInViewDevice";
        if (_toolCoordinator?.IsBusy(item.Serial) == true)
            return "ViewMultipleDevices_ActionBusy";
        return null;
    }
}
