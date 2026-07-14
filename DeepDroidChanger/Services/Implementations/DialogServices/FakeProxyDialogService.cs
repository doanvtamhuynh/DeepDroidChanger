using DeepDroidChanger.ViewModels;
using DeepDroidChanger.Models;
using DeepDroidChanger.Views;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services
{
    public sealed class FakeProxyDialogService : IFakeProxyDialogService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<FakeProxyDialogService> _logger;

        public FakeProxyDialogService(IServiceScopeFactory scopeFactory, ILogger<FakeProxyDialogService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task<FakeProxyDialogResult?> ShowFakeProxyDialogAsync(
            string deviceSerial,
            string deviceName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogDebug("Opening Fake Proxy dialog for device {Serial}.", deviceSerial);
            using var scope = _scopeFactory.CreateScope();

            var viewModel = scope.ServiceProvider.GetRequiredService<FakeProxyViewModel>();
            viewModel.DeviceSerial = deviceSerial;
            viewModel.DeviceName = deviceName;
            await viewModel.InitializeAsync(cancellationToken).ConfigureAwait(true);

            try
            {
                var window = scope.ServiceProvider.GetRequiredService<FakeProxyDialog>();
                window.Owner = Application.Current?.MainWindow;
                window.DataContext = viewModel;

                viewModel.CloseRequested += (_, result) =>
                {
                    window.DialogResult = result;
                };

                var dialogResult = window.ShowDialog() ?? false;
                cancellationToken.ThrowIfCancellationRequested();

                FakeProxyDialogResult? result = dialogResult
                    ? viewModel.BuildResult()
                    : null;

                _logger.LogDebug("Fake Proxy dialog closed. Result: {HasResult}.", result != null);

                return result;
            }
            finally
            {
                await viewModel.FlushPendingConfigSaveAsync().ConfigureAwait(true);
            }
        }
    }
}
