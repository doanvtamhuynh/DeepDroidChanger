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
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogDebug("Opening Change Location dialog for device {Serial}.", deviceSerial);
            using var scope = _scopeFactory.CreateScope();

            var viewModel = scope.ServiceProvider.GetRequiredService<ChangeLocationViewModel>();
            viewModel.DeviceSerial = deviceSerial;
            viewModel.DeviceName = deviceName;
            await viewModel.InitializeAsync(cancellationToken).ConfigureAwait(true);

            try
            {
                var window = scope.ServiceProvider.GetRequiredService<ChangeLocationDialog>();
                window.Owner = Application.Current?.MainWindow;
                window.DataContext = viewModel;

                viewModel.CloseRequested += (_, result) =>
                {
                    window.DialogResult = result;
                };

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
                await viewModel.FlushPendingConfigSaveAsync().ConfigureAwait(true);
            }
        }
    }
}
