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
            return ShowAsync(
                initialize: viewModel => viewModel.Initialize(deviceSerial, deviceName),
                buildResult: viewModel => viewModel.BuildResult(),
                cancellationToken: cancellationToken);
        }

        public Task<InstallPackageBatchRequest?> ShowInstallPackageBatchAsync(
            int targetCount,
            CancellationToken cancellationToken)
        {
            return ShowAsync(
                initialize: viewModel => viewModel.InitializeBatch(targetCount),
                buildResult: viewModel => viewModel.BuildBatchRequest(),
                cancellationToken: cancellationToken);
        }

        private Task<TResult?> ShowAsync<TResult>(
            Action<InstallPackageViewModel> initialize,
            Func<InstallPackageViewModel, TResult?> buildResult,
            CancellationToken cancellationToken)
            where TResult : class
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogDebug("Opening Install Package dialog.");
            using var scope = _scopeFactory.CreateScope();

            var viewModel = scope.ServiceProvider.GetRequiredService<InstallPackageViewModel>();
            initialize(viewModel);

            var window = scope.ServiceProvider.GetRequiredService<InstallPackageDialog>();
            window.Owner = Application.Current?.MainWindow;
            window.DataContext = viewModel;

            viewModel.CloseRequested += (_, result) =>
            {
                window.DialogResult = result;
            };

            using CancellationTokenRegistration cancellationRegistration =
                DialogCancellation.RegisterClose(window, cancellationToken);
            bool dialogResult = window.ShowDialog() ?? false;
            cancellationToken.ThrowIfCancellationRequested();

            TResult? result = dialogResult ? buildResult(viewModel) : null;

            _logger.LogDebug("Install Package dialog closed. Result: {HasResult}.", result != null);

            return Task.FromResult(result);
        }
    }
}
