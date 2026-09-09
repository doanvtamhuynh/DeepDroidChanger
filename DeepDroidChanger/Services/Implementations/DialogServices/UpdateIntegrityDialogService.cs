using DeepDroidChanger.Models;
using DeepDroidChanger.ViewModels;
using DeepDroidChanger.Views;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services
{
    public sealed class UpdateIntegrityDialogService : IUpdateIntegrityDialogService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<UpdateIntegrityDialogService> _logger;

        public UpdateIntegrityDialogService(IServiceScopeFactory scopeFactory, ILogger<UpdateIntegrityDialogService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public Task<UpdateIntegrityDialogResult?> ShowUpdateIntegrityAsync(
            string deviceSerial,
            string deviceName,
            StoredDeviceConfig currentConfig,
            Func<UpdateIntegrityDialogResult, CancellationToken, Task>? settingsChangedAsync,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(currentConfig);
            return ShowAsync(
                deviceSerial: deviceSerial,
                deviceName: deviceName,
                isBatchMode: false,
                targetCount: 0,
                initialize: viewModel => viewModel.InitializeFromConfig(currentConfig),
                settingsChangedAsync: settingsChangedAsync,
                cancellationToken: cancellationToken);
        }

        public Task<UpdateIntegrityDialogResult?> ShowUpdateIntegrityBatchAsync(
            int targetCount,
            DeviceUpdateIntegrityConfig currentConfig,
            Func<UpdateIntegrityDialogResult, CancellationToken, Task> settingsChangedAsync,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(currentConfig);
            ArgumentNullException.ThrowIfNull(settingsChangedAsync);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetCount);
            return ShowAsync(
                deviceSerial: string.Empty,
                deviceName: string.Empty,
                isBatchMode: true,
                targetCount: targetCount,
                initialize: viewModel => viewModel.InitializeFromConfig(currentConfig),
                settingsChangedAsync: settingsChangedAsync,
                cancellationToken: cancellationToken);
        }

        private async Task<UpdateIntegrityDialogResult?> ShowAsync(
            string deviceSerial,
            string deviceName,
            bool isBatchMode,
            int targetCount,
            Action<UpdateIntegrityViewModel> initialize,
            Func<UpdateIntegrityDialogResult, CancellationToken, Task>? settingsChangedAsync,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogDebug(
                "Opening Update Integrity dialog. Batch mode: {IsBatchMode}, Serial: {Serial}, Target count: {TargetCount}.",
                isBatchMode,
                deviceSerial,
                targetCount);
            using var scope = _scopeFactory.CreateScope();

            var viewModel = scope.ServiceProvider.GetRequiredService<UpdateIntegrityViewModel>();
            viewModel.IsBatchMode = isBatchMode;
            viewModel.BatchTargetCount = targetCount;
            viewModel.DeviceSerial = deviceSerial;
            viewModel.DeviceName = deviceName;

            var pendingSettingsSave = Task.CompletedTask;

            void OnSettingsChanged(object? sender, UpdateIntegrityDialogResult result)
            {
                if (settingsChangedAsync == null)
                    return;

                pendingSettingsSave = SaveSettingsChangeAsync(pendingSettingsSave, result);
            }

            async Task SaveSettingsChangeAsync(
                Task previousSave,
                UpdateIntegrityDialogResult result)
            {
                await previousSave.ConfigureAwait(true);
                try
                {
                    await settingsChangedAsync!(result, CancellationToken.None).ConfigureAwait(true);
                }
                catch (Exception exception)
                {
                    _logger.LogError(
                        exception,
                        "Failed to persist Update Integrity settings for {Scope}.",
                        isBatchMode ? "Multiple Devices" : deviceSerial);
                }
            }

            if (settingsChangedAsync != null)
                viewModel.SettingsChanged += OnSettingsChanged;

            try
            {
                initialize(viewModel);

                var window = scope.ServiceProvider.GetRequiredService<UpdateIntegrityDialog>();
                window.Owner = Application.Current?.MainWindow;
                window.DataContext = viewModel;

                viewModel.CloseRequested += (_, result) =>
                {
                    window.DialogResult = result;
                };

                using CancellationTokenRegistration cancellationRegistration =
                    DialogCancellation.RegisterClose(window, cancellationToken);
                var dialogResult = window.ShowDialog() ?? false;
                cancellationToken.ThrowIfCancellationRequested();

                UpdateIntegrityDialogResult? result = dialogResult
                    ? viewModel.BuildResult()
                    : null;

                _logger.LogDebug("Update Integrity dialog closed. Result: {HasResult}.", result != null);

                return result;
            }
            finally
            {
                if (settingsChangedAsync != null)
                {
                    viewModel.SettingsChanged -= OnSettingsChanged;
                    await pendingSettingsSave.ConfigureAwait(true);
                }
            }
        }
    }
}
