using DeepDroidChanger.Models;
using DeepDroidChanger.ViewModels;
using DeepDroidChanger.Views;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services
{
    public sealed class ChangeLocationDialogService : IChangeLocationDialogService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ChangeLocationDialogService> _logger;

        public ChangeLocationDialogService(IServiceScopeFactory scopeFactory, ILogger<ChangeLocationDialogService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task<ChangeLocationDialogResult?> ShowChangeLocationAsync(
            string deviceSerial,
            string deviceName,
            CancellationToken cancellationToken)
        {
            return await ShowChangeLocationAsync(
                    deviceSerial,
                    deviceName,
                    configurationSnapshot: null,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(true);
        }

        public async Task<ChangeLocationDialogResult?> ShowChangeLocationAsync(
            string deviceSerial,
            string deviceName,
            StoredDeviceConfig? configurationSnapshot,
            CancellationToken cancellationToken)
        {
            return await ShowAsync(
                    deviceSerial: deviceSerial,
                    deviceName: deviceName,
                    isBatchMode: false,
                    targetCount: 0,
                    configurationSnapshot: configurationSnapshot,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(true);
        }

        public async Task<ChangeLocationDialogResult?> ShowChangeLocationBatchAsync(
            int targetCount,
            CancellationToken cancellationToken)
        {
            return await ShowAsync(
                    deviceSerial: string.Empty,
                    deviceName: string.Empty,
                    isBatchMode: true,
                    targetCount: targetCount,
                    configurationSnapshot: null,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(true);
        }

        private async Task<ChangeLocationDialogResult?> ShowAsync(
            string deviceSerial,
            string deviceName,
            bool isBatchMode,
            int targetCount,
            StoredDeviceConfig? configurationSnapshot,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogDebug(
                "Opening Change Location dialog. Batch mode: {IsBatchMode}, Serial: {Serial}, Target count: {TargetCount}.",
                isBatchMode,
                deviceSerial,
                targetCount);
            using var scope = _scopeFactory.CreateScope();

            var viewModel = scope.ServiceProvider.GetRequiredService<ChangeLocationViewModel>();
            viewModel.IsBatchMode = isBatchMode;
            viewModel.BatchTargetCount = targetCount;
            viewModel.DeviceSerial = deviceSerial;
            viewModel.DeviceName = deviceName;
            await viewModel.InitializeAsync(configurationSnapshot, cancellationToken).ConfigureAwait(true);

            try
            {
                var window = scope.ServiceProvider.GetRequiredService<ChangeLocationDialog>();
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

                ChangeLocationDialogResult? result = dialogResult
                    ? viewModel.BuildResult()
                    : null;

                _logger.LogDebug("Change Location dialog closed. Result: {HasResult}.", result != null);

                return result;
            }
            finally
            {
                if (!isBatchMode)
                    await viewModel.FlushPendingConfigSaveAsync().ConfigureAwait(true);
            }
        }
    }
}
