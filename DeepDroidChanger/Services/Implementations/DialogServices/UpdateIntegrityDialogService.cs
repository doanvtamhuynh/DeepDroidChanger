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

        public async Task<UpdateIntegrityDialogResult?> ShowUpdateIntegrityAsync(
            string deviceSerial,
            string deviceName,
            StoredDeviceConfig currentConfig,
            Func<UpdateIntegrityDialogResult, CancellationToken, Task>? settingsChangedAsync,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogDebug("Opening Update Integrity dialog for device {Serial}.", deviceSerial);
            using var scope = _scopeFactory.CreateScope();

            var viewModel = scope.ServiceProvider.GetRequiredService<UpdateIntegrityViewModel>();
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
                    _logger.LogError(exception, "Failed to persist Update Integrity settings for device {Serial}.", deviceSerial);
                }
            }

            viewModel.SettingsChanged += OnSettingsChanged;
            try
            {
                viewModel.InitializeFromConfig(currentConfig);

                var window = scope.ServiceProvider.GetRequiredService<UpdateIntegrityDialog>();
                window.Owner = Application.Current?.MainWindow;
                window.DataContext = viewModel;

                viewModel.CloseRequested += (_, result) =>
                {
                    window.DialogResult = result;
                };

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
                viewModel.SettingsChanged -= OnSettingsChanged;
                await pendingSettingsSave.ConfigureAwait(true);
            }
        }
    }
}
