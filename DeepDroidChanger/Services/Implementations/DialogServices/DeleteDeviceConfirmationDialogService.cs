using DeepDroidChanger.Views;
using DeepDroidChanger.ViewModels;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services
{
    public sealed class DeleteDeviceConfirmationDialogService : IDeleteDeviceConfirmationDialogService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<DeleteDeviceConfirmationDialogService> _logger;

        public DeleteDeviceConfirmationDialogService(
            IServiceScopeFactory scopeFactory,
            ILogger<DeleteDeviceConfirmationDialogService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public Task<bool> ShowDeleteDeviceConfirmationAsync(
            string deviceName,
            string deviceSerial,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogDebug("Opening Delete Device confirmation dialog for device {Serial}.", deviceSerial);
            using var scope = _scopeFactory.CreateScope();

            var viewModel = scope.ServiceProvider.GetRequiredService<DeleteDeviceConfirmationViewModel>();
            viewModel.DeviceName = deviceName;
            viewModel.DeviceSerial = deviceSerial;

            var window = scope.ServiceProvider.GetRequiredService<DeleteDeviceConfirmationDialog>();
            window.Owner = Application.Current?.MainWindow;
            window.DataContext = viewModel;

            viewModel.CloseRequested += (_, result) =>
            {
                window.DialogResult = result;
            };

            var dialogResult = window.ShowDialog() ?? false;
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogDebug("Delete Device confirmation dialog closed. Confirmed: {Confirmed}.", dialogResult);

            return Task.FromResult(dialogResult);
        }
    }
}
