using DeepDroidChanger.Models;
using DeepDroidChanger.ViewModels;
using DeepDroidChanger.Views;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services
{
    public sealed class ChangeTimezoneDialogService : IChangeTimezoneDialogService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ChangeTimezoneDialogService> _logger;

        public ChangeTimezoneDialogService(IServiceScopeFactory scopeFactory, ILogger<ChangeTimezoneDialogService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task<ChangeTimezoneDialogResult?> ShowChangeTimezoneAsync(
            string deviceSerial,
            string deviceName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogDebug("Opening Change Timezone dialog for device {Serial}.", deviceSerial);
            using var scope = _scopeFactory.CreateScope();

            var viewModel = scope.ServiceProvider.GetRequiredService<ChangeTimezoneViewModel>();
            viewModel.DeviceSerial = deviceSerial;
            viewModel.DeviceName = deviceName;
            await viewModel.InitializeAsync(cancellationToken).ConfigureAwait(true);

            try
            {
                var window = scope.ServiceProvider.GetRequiredService<ChangeTimezoneDialog>();
                window.Owner = Application.Current?.MainWindow;
                window.DataContext = viewModel;

                viewModel.CloseRequested += (_, result) =>
                {
                    window.DialogResult = result;
                };

                var dialogResult = window.ShowDialog() ?? false;
                cancellationToken.ThrowIfCancellationRequested();

                ChangeTimezoneDialogResult? result = dialogResult
                    ? viewModel.BuildResult()
                    : null;

                _logger.LogDebug("Change Timezone dialog closed. Result: {HasResult}.", result != null);

                return result;
            }
            finally
            {
                await viewModel.FlushPendingConfigSaveAsync().ConfigureAwait(true);
            }
        }
    }
}
