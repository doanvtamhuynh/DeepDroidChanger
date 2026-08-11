using DeepDroidChanger.Services;
using DeepDroidChanger.Helpers;
using DeepDroidChanger.Models;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.ViewModels
{
    public sealed partial class InstallPackageViewModel : ObservableObject, IDisposable
    {
        private const int CompleteProgress = 100;
        private const int InstallingProgress = 10;
        private const int EmptyProgress = 0;

        private readonly IFilePickerDialogService _filePickerDialogService;
        private readonly IPackageInstallService _packageInstallService;
        private readonly ILocalizationService _localizationService;
        private readonly ILogger<InstallPackageViewModel> _logger;
        private CancellationTokenSource? _installCancellation;
        private int _successCount;
        private int _failedCount;
        private bool _isDisposed;
        private InstallPackageBatchRequest? _batchRequest;

        public InstallPackageViewModel(
            IFilePickerDialogService filePickerDialogService,
            IPackageInstallService packageInstallService,
            ILocalizationService localizationService,
            ILogger<InstallPackageViewModel> logger)
        {
            _filePickerDialogService = filePickerDialogService;
            _packageInstallService = packageInstallService;
            _localizationService = localizationService;
            _logger = logger;
            Packages.CollectionChanged += OnPackagesChanged;
            SummaryText = GetLogText("Log_InstallPackagePending");
        }

        public ObservableCollection<InstallPackageQueueItemViewModel> Packages { get; } = new();

        public event EventHandler<bool>? CloseRequested;

        [ObservableProperty]
        private string _deviceSerial = string.Empty;

        [ObservableProperty]
        private string _deviceName = string.Empty;

        [ObservableProperty]
        private string _deviceInfoText = string.Empty;

        [ObservableProperty]
        private bool _isBatchMode;

        [ObservableProperty]
        private int _batchTargetCount;

        [ObservableProperty]
        private InstallPackageQueueItemViewModel? _selectedPackage;

        [ObservableProperty]
        private bool _grantPermissions = true;

        [ObservableProperty]
        private bool _allowDowngrade;

        [ObservableProperty]
        private bool _isInstalling;

        [ObservableProperty]
        private bool _hasCompleted;

        [ObservableProperty]
        private bool _isCanceled;

        [ObservableProperty]
        private int _overallProgress;

        [ObservableProperty]
        private string _summaryText;

        public bool CanEditQueue => !IsInstalling;
        public bool CanClose => !IsInstalling;

        public void Initialize(string deviceSerial, string deviceName)
        {
            IsBatchMode = false;
            BatchTargetCount = 0;
            DeviceSerial = deviceSerial;
            DeviceName = deviceName;
            _batchRequest = null;
            UpdateDeviceInfoText();
        }

        public void InitializeBatch(int targetCount)
        {
            IsBatchMode = true;
            BatchTargetCount = targetCount;
            DeviceSerial = string.Empty;
            DeviceName = string.Empty;
            _batchRequest = null;
            ResetRunState();
            UpdateDeviceInfoText();
        }

        partial void OnDeviceSerialChanged(string value) => UpdateDeviceInfoText();
        partial void OnDeviceNameChanged(string value) => UpdateDeviceInfoText();
        partial void OnIsBatchModeChanged(bool value) => UpdateDeviceInfoText();
        partial void OnBatchTargetCountChanged(int value) => UpdateDeviceInfoText();

        partial void OnSelectedPackageChanged(InstallPackageQueueItemViewModel? value)
        {
            RemoveSelectedPackageCommand.NotifyCanExecuteChanged();
        }

        partial void OnIsInstallingChanged(bool value)
        {
            OnPropertyChanged(nameof(CanEditQueue));
            OnPropertyChanged(nameof(CanClose));
            AddFilesCommand.NotifyCanExecuteChanged();
            RemoveSelectedPackageCommand.NotifyCanExecuteChanged();
            StartInstallCommand.NotifyCanExecuteChanged();
            CloseCommand.NotifyCanExecuteChanged();
        }

        partial void OnHasCompletedChanged(bool value)
        {
            StartInstallCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand(CanExecute = nameof(CanAddFiles))]
        private void AddFiles()
        {
            ResetRunStateForQueueChange();

            var filter = GetLogText("Log_InstallPackageFilePickerFilter");
            var title = GetLogText("Log_InstallPackageFilePickerTitle");
            var selectedFiles = _filePickerDialogService.ShowOpenFileDialogMulti(filter, title);
            foreach (var filePath in selectedFiles)
            {
                if (Packages.Any(item => string.Equals(item.FilePath, filePath, StringComparison.OrdinalIgnoreCase)))
                    continue;

                Packages.Add(new InstallPackageQueueItemViewModel(filePath, GetLogText("Log_InstallPackagePending")));
            }
        }

        [RelayCommand(CanExecute = nameof(CanRemoveSelectedPackage))]
        private void RemoveSelectedPackage()
        {
            ResetRunStateForQueueChange();

            if (SelectedPackage == null)
                return;

            Packages.Remove(SelectedPackage);
            SelectedPackage = null;
        }

        [RelayCommand(CanExecute = nameof(CanStartInstall))]
        private async Task StartInstallAsync(CancellationToken cancellationToken)
        {
            if (IsBatchMode)
            {
                _batchRequest = BuildBatchRequestCore();
                if (_batchRequest == null)
                {
                    SummaryText = GetLogText("Log_InstallPackageNoFiles");
                    return;
                }

                CloseRequested?.Invoke(this, true);
                return;
            }

            if (Packages.Count == 0)
            {
                SummaryText = GetLogText("Log_InstallPackageNoFiles");
                return;
            }

            ResetRunState();
            IsInstalling = true;
            _installCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var installToken = _installCancellation.Token;

            try
            {
                var options = new InstallPackageOptions(GrantPermissions, AllowDowngrade);
                var completedCount = 0;

                foreach (var package in Packages)
                {
                    installToken.ThrowIfCancellationRequested();

                    package.Progress = InstallingProgress;
                    package.StatusText = GetLogText("Log_InstallPackageInstalling");

                    try
                    {
                        var result = await _packageInstallService
                            .InstallAsync(DeviceSerial, package.FilePath, options, installToken)
                            .ConfigureAwait(true);

                        ApplyItemResult(package, result);
                    }
                    catch (OperationCanceledException)
                    {
                        IsCanceled = true;
                        package.Progress = EmptyProgress;
                        package.StatusText = GetLogText("Log_InstallPackageCanceled");
                        break;
                    }
                    catch (Exception)
                    {
                        _logger.LogError("Failed to install selected package.");
                        ApplyItemResult(
                            package,
                            new InstallPackageResult(
                                package.FilePath,
                                false,
                                "Log_InstallPackageAdbFailure"));
                    }

                    completedCount++;
                    OverallProgress = CalculateOverallProgress(completedCount, Packages.Count);
                    SummaryText = CreateSummaryText();
                }
            }
            finally
            {
                _installCancellation?.Dispose();
                _installCancellation = null;
                IsInstalling = false;
                HasCompleted = true;
                SummaryText = IsCanceled
                    ? GetLogText("Log_InstallPackageCanceled")
                    : CreateSummaryText();
            }
        }

        [RelayCommand]
        private void Cancel()
        {
            if (IsInstalling)
            {
                IsCanceled = true;
                _installCancellation?.Cancel();
                return;
            }

            CloseRequested?.Invoke(this, false);
        }

        [RelayCommand(CanExecute = nameof(CanCloseDialog))]
        private void Close()
        {
            CloseRequested?.Invoke(this, HasCompleted);
        }

        public InstallPackageDialogResult BuildResult()
        {
            return new InstallPackageDialogResult(Packages.Count, _successCount, _failedCount, IsCanceled);
        }

        public InstallPackageBatchRequest? BuildBatchRequest()
        {
            if (!IsBatchMode)
                return null;

            return _batchRequest ?? BuildBatchRequestCore();
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            Packages.CollectionChanged -= OnPackagesChanged;
            _installCancellation?.Cancel();
            _installCancellation?.Dispose();
        }

        private bool CanAddFiles() => !IsInstalling;
        private bool CanRemoveSelectedPackage() => !IsInstalling && SelectedPackage != null;
        private bool CanStartInstall() => !IsInstalling && !HasCompleted && (IsBatchMode || Packages.Count > 0);
        private bool CanCloseDialog() => !IsInstalling;

        private void OnPackagesChanged(object? sender, NotifyCollectionChangedEventArgs args)
        {
            StartInstallCommand.NotifyCanExecuteChanged();
            RemoveSelectedPackageCommand.NotifyCanExecuteChanged();
        }

        private void ApplyItemResult(InstallPackageQueueItemViewModel package, InstallPackageResult result)
        {
            package.Progress = CompleteProgress;
            string messageTemplate = GetLogText(result.MessageResourceKey);
            package.StatusText = result.MessageArguments.Count == 0
                ? messageTemplate
                : string.Format(messageTemplate, result.MessageArguments.ToArray());
            package.IsSuccessful = result.Success;
            package.IsFailed = !result.Success;

            if (result.Success)
            {
                _successCount++;
            }
            else
            {
                _failedCount++;
            }
        }

        private void ResetRunStateForQueueChange()
        {
            if (!HasCompleted && !IsCanceled)
                return;

            ResetRunState();
            foreach (var package in Packages)
            {
                package.StatusText = GetLogText("Log_InstallPackagePending");
                package.Progress = EmptyProgress;
                package.IsSuccessful = false;
                package.IsFailed = false;
            }
        }

        private void ResetRunState()
        {
            _successCount = 0;
            _failedCount = 0;
            IsCanceled = false;
            HasCompleted = false;
            OverallProgress = EmptyProgress;
            SummaryText = GetLogText("Log_InstallPackagePending");
        }

        private InstallPackageBatchRequest? BuildBatchRequestCore()
        {
            if (!IsBatchMode || Packages.Count == 0)
                return null;

            string[] filePaths = Packages.Select(package => package.FilePath).ToArray();
            return new InstallPackageBatchRequest(
                Array.AsReadOnly(filePaths),
                new InstallPackageOptions(GrantPermissions, AllowDowngrade));
        }

        private string CreateSummaryText()
        {
            var totalCount = Packages.Count;
            if (totalCount == 0)
                return GetLogText("Log_InstallPackageNoFiles");

            if (_failedCount == 0 && _successCount == totalCount)
            {
                return string.Format(
                    GetLogText("Log_InstallPackageCompleteFormat"),
                    _successCount,
                    totalCount);
            }

            if (_successCount > 0)
            {
                return string.Format(
                    GetLogText("Log_InstallPackagePartialFormat"),
                    _successCount,
                    totalCount);
            }

            return string.Format(
                GetLogText("Log_InstallPackageFailedFormat"),
                _successCount,
                totalCount);
        }

        private string GetLogText(string resourceKey)
        {
            return _localizationService.GetString(resourceKey);
        }

        private static int CalculateOverallProgress(int completedCount, int totalCount)
        {
            if (totalCount <= 0)
                return EmptyProgress;

            return completedCount * CompleteProgress / totalCount;
        }

        private void UpdateDeviceInfoText()
        {
            DeviceInfoText = IsBatchMode
                ? string.Format(
                    GetLogText("InstallPackage_BatchDeviceInfo"),
                    BatchTargetCount)
                : DeviceInfoTextHelper.Create(_localizationService, DeviceName, DeviceSerial);
        }
    }
}
