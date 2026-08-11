using System.Windows;
using DeepDroidChanger.Models;
using DeepDroidChanger.ViewModels;
using DeepDroidChanger.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services;

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

    public Task<InstallPackageRequest?> ShowInstallPackageAsync(
        string deviceSerial,
        string deviceName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogDebug("Opening Single Device Install Package dialog.");
        using var scope = _scopeFactory.CreateScope();
        InstallPackageViewModel viewModel = scope.ServiceProvider
            .GetRequiredService<InstallPackageViewModel>();
        viewModel.Initialize(deviceSerial, deviceName);

        InstallPackageDialog window = scope.ServiceProvider
            .GetRequiredService<InstallPackageDialog>();
        window.Owner = Application.Current?.MainWindow;
        window.DataContext = viewModel;
        viewModel.CloseRequested += (_, result) => window.DialogResult = result;

        using CancellationTokenRegistration cancellationRegistration =
            DialogCancellation.RegisterClose(window, cancellationToken);
        bool dialogResult = window.ShowDialog() ?? false;
        cancellationToken.ThrowIfCancellationRequested();

        InstallPackageRequest? request = dialogResult ? viewModel.BuildRequest() : null;
        _logger.LogDebug(
            "Single Device Install Package dialog closed. Result: {HasResult}.",
            request != null);

        return Task.FromResult(request);
    }

    public Task<InstallPackageBatchRequest?> ShowInstallPackageBatchAsync(
        int targetCount,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogDebug("Opening Multiple Device Install Package dialog.");
        using var scope = _scopeFactory.CreateScope();
        InstallPackageBatchViewModel viewModel = scope.ServiceProvider
            .GetRequiredService<InstallPackageBatchViewModel>();
        viewModel.InitializeBatch(targetCount);

        InstallPackageBatchDialog window = scope.ServiceProvider
            .GetRequiredService<InstallPackageBatchDialog>();
        window.Owner = Application.Current?.MainWindow;
        window.DataContext = viewModel;
        viewModel.CloseRequested += (_, result) => window.DialogResult = result;

        using CancellationTokenRegistration cancellationRegistration =
            DialogCancellation.RegisterClose(window, cancellationToken);
        bool dialogResult = window.ShowDialog() ?? false;
        cancellationToken.ThrowIfCancellationRequested();

        InstallPackageBatchRequest? request = dialogResult ? viewModel.BuildRequest() : null;
        _logger.LogDebug(
            "Multiple Device Install Package dialog closed. Result: {HasResult}.",
            request != null);

        return Task.FromResult(request);
    }
}
