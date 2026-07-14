using DeepDroidChanger.ViewModels;
using DeepDroidChanger.Views;
using DeepDroidChanger.Models;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services
{
    public sealed class AddDevicesDialogService : IAddDevicesDialogService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<AddDevicesDialogService> _logger;

        public AddDevicesDialogService(IServiceScopeFactory scopeFactory, ILogger<AddDevicesDialogService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task<IReadOnlyList<StoredDeviceConfig>> ShowAddDevicesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogDebug("Opening Add Devices dialog.");
            using var scope = _scopeFactory.CreateScope();

            var viewModel = scope.ServiceProvider.GetRequiredService<AddDevicesViewModel>();
            var window = scope.ServiceProvider.GetRequiredService<AddDevicesDialog>();
            try
            {
                window.Owner = Application.Current?.MainWindow;
                window.DataContext = viewModel;
                await viewModel.InitializeAsync(cancellationToken).ConfigureAwait(true);

                viewModel.CloseRequested += (_, result) =>
                {
                    window.DialogResult = result;
                };

                var dialogResult = window.ShowDialog() ?? false;
                cancellationToken.ThrowIfCancellationRequested();

                IReadOnlyList<StoredDeviceConfig> selectedDevices = dialogResult
                    ? viewModel.SelectedDevices
                    : Array.Empty<StoredDeviceConfig>();

                _logger.LogDebug("Add Devices dialog closed. Selected {Count} device(s).", selectedDevices.Count);

                return selectedDevices;
            }
            finally
            {
                await viewModel.DeactivateAsync().ConfigureAwait(true);
            }
        }
    }
}
