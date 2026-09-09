using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using DeepDroidChanger.Helpers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DeepDroidChanger.ViewModels
{
    public sealed partial class UpdateIntegrityViewModel : ObservableObject
    {
        private readonly IFilePickerDialogService _filePickerDialogService;
        private readonly ILocalizationService _localizationService;
        private readonly IFileSystemService _fileSystemService;
        private bool _isInitializing;

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
        private bool _updateIntegrityFromServer = true;

        [ObservableProperty]
        private bool _updateIntegrityEnabled = true;

        [ObservableProperty]
        private bool _updateKeyboxEnabled = true;

        [ObservableProperty]
        private string _updateIntegrityFile = string.Empty;

        [ObservableProperty]
        private string _updateKeyboxFile = string.Empty;

        public event EventHandler<bool>? CloseRequested;
        public event EventHandler<UpdateIntegrityDialogResult>? SettingsChanged;

        public UpdateIntegrityViewModel(
            IFilePickerDialogService filePickerDialogService,
            ILocalizationService localizationService,
            IFileSystemService fileSystemService)
        {
            _filePickerDialogService = filePickerDialogService;
            _localizationService = localizationService;
            _fileSystemService = fileSystemService;
        }

        public bool IsIntegrityFilePickerEnabled => !UpdateIntegrityFromServer && UpdateIntegrityEnabled;
        public bool IsKeyboxFilePickerEnabled => !UpdateIntegrityFromServer && UpdateKeyboxEnabled;

        public void InitializeFromConfig(StoredDeviceConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);
            InitializeFromValues(
                config.UpdateIntegrityFromServer,
                config.UpdateIntegrityEnabled,
                config.UpdateKeyboxEnabled,
                config.UpdateIntegrityFile,
                config.UpdateKeyboxFile);
        }

        public void InitializeFromConfig(DeviceUpdateIntegrityConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);
            InitializeFromValues(
                config.FromServer,
                config.IntegrityEnabled,
                config.KeyboxEnabled,
                config.IntegrityFile,
                config.KeyboxFile);
        }

        private void InitializeFromValues(
            bool configuredFromServer,
            bool configuredIntegrityEnabled,
            bool configuredKeyboxEnabled,
            string? integrityFile,
            string? keyboxFile)
        {
            var updateIntegrityFile = GetAvailableFilePathOrEmpty(integrityFile);
            var updateKeyboxFile = GetAvailableFilePathOrEmpty(keyboxFile);
            bool updateIntegrityFromServer = configuredFromServer;
            bool updateIntegrityEnabled = configuredIntegrityEnabled;
            bool updateKeyboxEnabled = configuredKeyboxEnabled;

            if (!updateIntegrityFromServer)
            {
                var enabledIntegrityAvailable = updateIntegrityEnabled && !string.IsNullOrWhiteSpace(updateIntegrityFile);
                var enabledKeyboxAvailable = updateKeyboxEnabled && !string.IsNullOrWhiteSpace(updateKeyboxFile);

                if ((updateIntegrityEnabled || updateKeyboxEnabled)
                    && !enabledIntegrityAvailable
                    && !enabledKeyboxAvailable)
                {
                    updateIntegrityFromServer = true;
                }
                else
                {
                    if (updateIntegrityEnabled && string.IsNullOrWhiteSpace(updateIntegrityFile))
                        updateIntegrityEnabled = false;
                    if (updateKeyboxEnabled && string.IsNullOrWhiteSpace(updateKeyboxFile))
                        updateKeyboxEnabled = false;
                }
            }

            var shouldSaveSanitizedPaths =
                !string.Equals(integrityFile?.Trim() ?? string.Empty, updateIntegrityFile, StringComparison.Ordinal) ||
                !string.Equals(keyboxFile?.Trim() ?? string.Empty, updateKeyboxFile, StringComparison.Ordinal) ||
                configuredFromServer != updateIntegrityFromServer ||
                configuredIntegrityEnabled != updateIntegrityEnabled ||
                configuredKeyboxEnabled != updateKeyboxEnabled;

            _isInitializing = true;
            try
            {
                UpdateIntegrityFromServer = updateIntegrityFromServer;
                UpdateIntegrityFile = updateIntegrityFile;
                UpdateKeyboxFile = updateKeyboxFile;
                UpdateIntegrityEnabled = updateIntegrityEnabled;
                UpdateKeyboxEnabled = updateKeyboxEnabled;
                UpdateDeviceInfoText();
            }
            finally
            {
                _isInitializing = false;
            }

            if (shouldSaveSanitizedPaths)
            {
                NotifySettingsChanged();
            }
        }

        partial void OnDeviceSerialChanged(string value) => UpdateDeviceInfoText();
        partial void OnDeviceNameChanged(string value) => UpdateDeviceInfoText();
        partial void OnIsBatchModeChanged(bool value) => UpdateDeviceInfoText();
        partial void OnBatchTargetCountChanged(int value) => UpdateDeviceInfoText();

        partial void OnUpdateIntegrityFromServerChanged(bool value)
        {
            OnPropertyChanged(nameof(IsIntegrityFilePickerEnabled));
            OnPropertyChanged(nameof(IsKeyboxFilePickerEnabled));
            UpdateCommand.NotifyCanExecuteChanged();
            NotifySettingsChanged();
        }

        partial void OnUpdateIntegrityEnabledChanged(bool value)
        {
            OnPropertyChanged(nameof(IsIntegrityFilePickerEnabled));
            UpdateCommand.NotifyCanExecuteChanged();
            NotifySettingsChanged();
        }

        partial void OnUpdateKeyboxEnabledChanged(bool value)
        {
            OnPropertyChanged(nameof(IsKeyboxFilePickerEnabled));
            UpdateCommand.NotifyCanExecuteChanged();
            NotifySettingsChanged();
        }

        partial void OnUpdateIntegrityFileChanged(string value)
        {
            UpdateCommand.NotifyCanExecuteChanged();
            NotifySettingsChanged();
        }

        partial void OnUpdateKeyboxFileChanged(string value)
        {
            UpdateCommand.NotifyCanExecuteChanged();
            NotifySettingsChanged();
        }

        [RelayCommand]
        private void BrowseIntegrityFile()
        {
            var file = _filePickerDialogService.ShowOpenFileDialog(
                _localizationService.GetString("UpdateIntegrity_IntegrityFileFilter"),
                _localizationService.GetString("UpdateIntegrity_IntegrityFileTitle"));
            if (file != null)
            {
                UpdateIntegrityFile = file;
            }
        }

        [RelayCommand]
        private void BrowseKeyboxFile()
        {
            var file = _filePickerDialogService.ShowOpenFileDialog(
                _localizationService.GetString("UpdateIntegrity_KeyboxFileFilter"),
                _localizationService.GetString("UpdateIntegrity_KeyboxFileTitle"));
            if (file != null)
            {
                UpdateKeyboxFile = file;
            }
        }

        private void UpdateDeviceInfoText()
        {
            if (IsBatchMode)
            {
                string format = _localizationService.GetString("UpdateIntegrity_BatchDeviceInfo");
                try
                {
                    DeviceInfoText = format.Contains("{0}", StringComparison.Ordinal)
                        ? string.Format(format, BatchTargetCount)
                        : $"{format} ({BatchTargetCount})";
                }
                catch (FormatException)
                {
                    DeviceInfoText = $"{format} ({BatchTargetCount})";
                }

                return;
            }

            DeviceInfoText = DeviceInfoTextHelper.Create(_localizationService, DeviceName, DeviceSerial);
        }

        public UpdateIntegrityDialogResult BuildResult()
        {
            return new UpdateIntegrityDialogResult(
                UpdateIntegrityFromServer,
                UpdateIntegrityEnabled,
                UpdateKeyboxEnabled,
                UpdateIntegrityFile?.Trim() ?? string.Empty,
                UpdateKeyboxFile?.Trim() ?? string.Empty
            );
        }

        private bool CanUpdate()
        {
            if (!UpdateIntegrityEnabled && !UpdateKeyboxEnabled)
                return false;

            if (!UpdateIntegrityFromServer)
            {
                if (UpdateIntegrityEnabled && !_fileSystemService.FileExists(UpdateIntegrityFile.Trim()))
                    return false;
                if (UpdateKeyboxEnabled && !_fileSystemService.FileExists(UpdateKeyboxFile.Trim()))
                    return false;
            }

            return true;
        }

        [RelayCommand(CanExecute = nameof(CanUpdate))]
        private Task UpdateAsync()
        {
            CloseRequested?.Invoke(this, true);
            return Task.CompletedTask;
        }

        private void NotifySettingsChanged()
        {
            if (_isInitializing)
                return;

            SettingsChanged?.Invoke(this, BuildResult());
        }

        private string GetAvailableFilePathOrEmpty(string? filePath)
        {
            var normalizedFilePath = filePath?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalizedFilePath))
                return string.Empty;

            return _fileSystemService.FileExists(normalizedFilePath) ? normalizedFilePath : string.Empty;
        }
    }
}
