using DeepDroidChanger.ViewModels;
using DeepDroidChanger.Views;
using DeepDroidChanger.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Windows;

namespace DeepDroidChanger.Services;

public sealed class ChangeDeviceConfirmationDialogService : IChangeDeviceConfirmationDialogService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ChangeDeviceConfirmationDialogService> _logger;

    public ChangeDeviceConfirmationDialogService(
        IServiceScopeFactory scopeFactory,
        ILogger<ChangeDeviceConfirmationDialogService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public Task<bool> ShowChangeDeviceConfirmationAsync(
        string deviceName,
        string deviceSerial,
        DeviceChangeOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogDebug("Opening Change Device confirmation dialog for device {Serial}.", deviceSerial);

        using var scope = _scopeFactory.CreateScope();
        var viewModel = scope.ServiceProvider.GetRequiredService<ChangeDeviceConfirmationViewModel>();
        viewModel.Initialize(deviceName, deviceSerial, options);

        var window = scope.ServiceProvider.GetRequiredService<ChangeDeviceConfirmationDialog>();
        window.Owner = Application.Current?.MainWindow;
        window.DataContext = viewModel;
        viewModel.CloseRequested += (_, confirmed) => window.DialogResult = confirmed;

        bool confirmed = window.ShowDialog() ?? false;
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogDebug("Change Device confirmation dialog closed. Confirmed: {Confirmed}.", confirmed);
        return Task.FromResult(confirmed);
    }
}
