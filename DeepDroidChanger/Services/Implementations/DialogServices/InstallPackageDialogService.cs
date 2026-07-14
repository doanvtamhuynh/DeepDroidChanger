using DeepDroidChanger.ViewModels;
using DeepDroidChanger.Models;
using DeepDroidChanger.Views;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services
{
    public sealed class InstallPackageDialogService : IInstallPackageDialogService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<InstallPackageDialogService> _logger;

        public InstallPackageDialogService(
            IServiceScopeFactory scopeFactory,
            ILogger<InstallPackageDialogService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public Task<InstallPackageDialogResult?> ShowInstallPackageAsync(
            string deviceSerial,
            string deviceName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogDebug("Opening Install Package dialog.");
            using var scope = _scopeFactory.CreateScope();

            var viewModel = scope.ServiceProvider.GetRequiredService<InstallPackageViewModel>();
            viewModel.Initialize(deviceSerial, deviceName);

            var window = scope.ServiceProvider.GetRequiredService<InstallPackageDialog>();
            window.Owner = Application.Current?.MainWindow;
            window.DataContext = viewModel;

            viewModel.CloseRequested += (_, result) =>
            {
                window.DialogResult = result;
            };

            var dialogResult = window.ShowDialog() ?? false;
            cancellationToken.ThrowIfCancellationRequested();

            InstallPackageDialogResult? result = dialogResult
                ? viewModel.BuildResult()
                : null;

            _logger.LogDebug("Install Package dialog closed. Result: {HasResult}.", result != null);

            return Task.FromResult(result);
        }
    }
}
