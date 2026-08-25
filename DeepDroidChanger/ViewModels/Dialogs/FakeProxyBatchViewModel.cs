using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.ViewModels;

public sealed partial class FakeProxyBatchViewModel : ObservableObject
{
    private readonly IMultipleDeviceConfigService _multipleDeviceConfigService;
    private readonly ILocalizationService _localizationService;
    private readonly ILogger<FakeProxyBatchViewModel> _logger;
    private IReadOnlyList<ProxyEndpoint> _parsedProxies = [];
    private int? _invalidProxyLine;
    private bool _isInitializing;
    private FakeProxyBatchDialogResult? _confirmedBatchResult;

    [ObservableProperty]
    private int _batchTargetCount;

    [ObservableProperty]
    private string _deviceInfoText = string.Empty;

    [ObservableProperty]
    private string _proxyListText = string.Empty;

    [ObservableProperty]
    private string _selectedProxyType = ProxyEndpoint.DefaultProxyType;

    [ObservableProperty]
    private bool _proxyChangeLocationByIp = true;

    [ObservableProperty]
    private bool _proxyChangeTimezoneByIp = true;

    [ObservableProperty]
    private ProxyAssignmentMode _assignmentMode = ProxyAssignmentMode.OneToOne;

    [ObservableProperty]
    private string _repeatCountText = "1";

    [ObservableProperty]
    private ProxyRepeatPattern _repeatPattern = ProxyRepeatPattern.Interleaved;

    [ObservableProperty]
    private bool _isRepeatByCount;

    [ObservableProperty]
    private string _validationText = string.Empty;

    [ObservableProperty]
    private bool _hasValidationError;

    [ObservableProperty]
    private string _validProxySummaryText = string.Empty;

    [ObservableProperty]
    private string _assignedDeviceSummaryText = string.Empty;

    public FakeProxyBatchViewModel(
        IMultipleDeviceConfigService multipleDeviceConfigService,
        ILocalizationService localizationService,
        ILogger<FakeProxyBatchViewModel> logger)
    {
        _multipleDeviceConfigService = multipleDeviceConfigService;
        _localizationService = localizationService;
        _logger = logger;
        RefreshValidationAndSummary();
    }

    public IReadOnlyList<string> ProxyTypes { get; } = [ProxyEndpoint.DefaultProxyType];

    public bool IsOneToOne
    {
        get => AssignmentMode == ProxyAssignmentMode.OneToOne;
        set
        {
            if (value)
                AssignmentMode = ProxyAssignmentMode.OneToOne;
        }
    }

    public bool IsRepeatByCountMode
    {
        get => AssignmentMode == ProxyAssignmentMode.RepeatByCount;
        set
        {
            if (value)
                AssignmentMode = ProxyAssignmentMode.RepeatByCount;
        }
    }

    public bool IsRepeatAll
    {
        get => AssignmentMode == ProxyAssignmentMode.RepeatAll;
        set
        {
            if (value)
                AssignmentMode = ProxyAssignmentMode.RepeatAll;
        }
    }

    public bool IsInterleaved
    {
        get => RepeatPattern == ProxyRepeatPattern.Interleaved;
        set
        {
            if (value)
                RepeatPattern = ProxyRepeatPattern.Interleaved;
        }
    }

    public bool IsConsecutive
    {
        get => RepeatPattern == ProxyRepeatPattern.Consecutive;
        set
        {
            if (value)
                RepeatPattern = ProxyRepeatPattern.Consecutive;
        }
    }

    public event EventHandler<bool>? CloseRequested;

    public async Task InitializeAsync(
        int targetCount,
        CancellationToken cancellationToken)
    {
        BatchTargetCount = targetCount;
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            _isInitializing = true;
            await LoadBatchConfigAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to load Multiple Device Fake Proxy configuration.");
            ResetConfiguration();
        }
        finally
        {
            _isInitializing = false;
            UpdateDeviceInfoText();
            RefreshValidationAndSummary();
        }
    }

    private async Task LoadBatchConfigAsync(CancellationToken cancellationToken)
    {
        MultipleDeviceProxyConfig config = await _multipleDeviceConfigService
            .LoadProxyConfigAsync(cancellationToken)
            .ConfigureAwait(true);
        ProxyListText = string.Join(Environment.NewLine, config.Proxies);
        SelectedProxyType = ProxyEndpoint.IsSupportedProxyType(config.ProxyType)
            ? config.ProxyType
            : ProxyEndpoint.DefaultProxyType;
        ProxyChangeLocationByIp = config.ChangeLocationByIp;
        ProxyChangeTimezoneByIp = config.ChangeTimezoneByIp;
        AssignmentMode = config.AssignmentMode;
        RepeatCountText = Math.Max(1, config.RepeatCount).ToString();
        RepeatPattern = config.RepeatPattern;
    }

    private void ResetConfiguration()
    {
        ProxyListText = string.Empty;
        SelectedProxyType = ProxyEndpoint.DefaultProxyType;
        ProxyChangeLocationByIp = true;
        ProxyChangeTimezoneByIp = true;
        AssignmentMode = ProxyAssignmentMode.OneToOne;
        RepeatCountText = "1";
        RepeatPattern = ProxyRepeatPattern.Interleaved;
    }

    partial void OnBatchTargetCountChanged(int value)
    {
        UpdateDeviceInfoText();
        RefreshValidationAndSummary();
    }

    partial void OnProxyListTextChanged(string value) => RefreshValidationAndSummary();

    partial void OnProxyChangeLocationByIpChanged(bool value) => SaveCommand.NotifyCanExecuteChanged();

    partial void OnProxyChangeTimezoneByIpChanged(bool value) => SaveCommand.NotifyCanExecuteChanged();

    partial void OnSelectedProxyTypeChanged(string value) => SaveCommand.NotifyCanExecuteChanged();

    partial void OnAssignmentModeChanged(ProxyAssignmentMode value)
    {
        OnPropertyChanged(nameof(IsOneToOne));
        OnPropertyChanged(nameof(IsRepeatByCountMode));
        OnPropertyChanged(nameof(IsRepeatAll));
        IsRepeatByCount = value == ProxyAssignmentMode.RepeatByCount;
        RefreshValidationAndSummary();
    }

    partial void OnRepeatCountTextChanged(string value) => RefreshValidationAndSummary();

    partial void OnRepeatPatternChanged(ProxyRepeatPattern value)
    {
        OnPropertyChanged(nameof(IsInterleaved));
        OnPropertyChanged(nameof(IsConsecutive));
        RefreshValidationAndSummary();
    }

    private void UpdateDeviceInfoText()
    {
        DeviceInfoText = FormatLocalized("FakeProxyBatch_DeviceInfo", BatchTargetCount);
    }

    private void RefreshValidationAndSummary()
    {
        ParseProxyList();
        int assignedCount = CalculateAssignedDeviceCount();
        ValidProxySummaryText = FormatLocalized("FakeProxyBatch_ValidProxySummary", _parsedProxies.Count);
        AssignedDeviceSummaryText = FormatLocalized(
            "FakeProxyBatch_AssignedDeviceSummary",
            assignedCount,
            BatchTargetCount);

        ValidationText = _invalidProxyLine.HasValue
            ? FormatLocalized("FakeProxyBatch_InvalidProxyLine", _invalidProxyLine.Value)
            : AssignmentMode == ProxyAssignmentMode.RepeatByCount
              && !TryGetRepeatCount(out _)
                ? _localizationService.GetString("FakeProxyBatch_InvalidRepeatCount")
                : string.Empty;
        HasValidationError = ValidationText.Length > 0;
        SaveCommand.NotifyCanExecuteChanged();
    }

    private void ParseProxyList()
    {
        var parsed = new List<ProxyEndpoint>();
        _invalidProxyLine = null;
        string[] lines = ProxyListText.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
        for (int index = 0; index < lines.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(lines[index]))
                continue;

            if (!ProxyEndpoint.TryParse(lines[index], out ProxyEndpoint? endpoint))
            {
                _invalidProxyLine ??= index + 1;
                continue;
            }

            parsed.Add(endpoint!);
        }

        _parsedProxies = parsed;
    }

    private int CalculateAssignedDeviceCount()
    {
        if (_parsedProxies.Count == 0 || BatchTargetCount <= 0)
            return 0;

        return AssignmentMode switch
        {
            ProxyAssignmentMode.OneToOne => Math.Min(BatchTargetCount, _parsedProxies.Count),
            ProxyAssignmentMode.RepeatAll => BatchTargetCount,
            ProxyAssignmentMode.RepeatByCount when TryGetRepeatCount(out int repeatCount) =>
                (int)Math.Min(BatchTargetCount, (long)_parsedProxies.Count * repeatCount),
            _ => 0
        };
    }

    private bool CanSave()
    {
        if (_isInitializing
            || _invalidProxyLine.HasValue
            || !ProxyEndpoint.IsSupportedProxyType(SelectedProxyType))
        {
            return false;
        }

        return _parsedProxies.Count > 0
            && Enum.IsDefined(AssignmentMode)
            && (AssignmentMode != ProxyAssignmentMode.RepeatByCount
                || (TryGetRepeatCount(out _) && Enum.IsDefined(RepeatPattern)))
            && CalculateAssignedDeviceCount() > 0;
    }

    private bool TryGetRepeatCount(out int repeatCount) =>
        int.TryParse(RepeatCountText?.Trim(), out repeatCount) && repeatCount >= 1;

    public FakeProxyBatchDialogResult? BuildBatchResult() => _confirmedBatchResult;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            ParseProxyList();
            if (!CanSave())
                return;

            int repeatCount = TryGetRepeatCount(out int parsedRepeatCount) ? parsedRepeatCount : 1;
            var result = new FakeProxyBatchDialogResult(
                _parsedProxies.ToArray(),
                AssignmentMode,
                repeatCount,
                RepeatPattern,
                SelectedProxyType,
                ProxyChangeLocationByIp,
                ProxyChangeTimezoneByIp);
            await _multipleDeviceConfigService.SaveProxyConfigAsync(
                    new MultipleDeviceProxyConfig
                    {
                        Proxies = result.Proxies.Select(proxy => proxy.NormalizedText).ToList(),
                        ProxyType = result.ProxyType,
                        ChangeLocationByIp = result.ChangeLocationByIp,
                        ChangeTimezoneByIp = result.ChangeTimezoneByIp,
                        AssignmentMode = result.AssignmentMode,
                        RepeatCount = result.RepeatCount,
                        RepeatPattern = result.RepeatPattern
                    },
                    cancellationToken)
                .ConfigureAwait(true);
            _confirmedBatchResult = result;
            CloseRequested?.Invoke(this, true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to save Multiple Device Fake Proxy preset.");
            ValidationText = _localizationService.GetString("FakeProxyBatch_PresetSaveFailed");
            HasValidationError = true;
        }
    }

    private string FormatLocalized(string resourceKey, params object[] arguments)
    {
        string format = _localizationService.GetString(resourceKey);
        try
        {
            return string.Format(format, arguments);
        }
        catch (FormatException)
        {
            return format;
        }
    }
}
