using DeepDroidChanger.Models;
using DeepDroidChanger.ViewModels;
using DeepDroidChanger.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Windows;

namespace DeepDroidChanger.Services;

public sealed class AdvancedChangeConfigDialogService : IAdvancedChangeConfigDialogService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AdvancedChangeConfigDialogService> _logger;

    public AdvancedChangeConfigDialogService(
        IServiceScopeFactory scopeFactory,
        ILogger<AdvancedChangeConfigDialogService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public Task<DeviceChangeOptions?> ShowAdvancedChangeConfigAsync(
        string deviceSerial,
        DeviceChangeOptions currentOptions,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogDebug("Opening advanced Change Device configuration for device {Serial}.", deviceSerial);

        using var scope = _scopeFactory.CreateScope();
        AdvancedChangeConfigViewModel viewModel =
            scope.ServiceProvider.GetRequiredService<AdvancedChangeConfigViewModel>();
        viewModel.Initialize(deviceSerial, currentOptions);

        AdvancedChangeConfigDialog window =
            scope.ServiceProvider.GetRequiredService<AdvancedChangeConfigDialog>();
        window.Owner = Application.Current?.MainWindow;
        window.DataContext = viewModel;

        DeviceChangeOptions? result = null;
        viewModel.CloseRequested += (_, options) =>
        {
            result = options;
            window.DialogResult = options != null;
        };

        window.ShowDialog();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(result);
    }
}
