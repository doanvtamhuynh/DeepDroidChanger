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

    public Task<AdvancedChangeConfigDialogResult?> ShowAdvancedChangeConfigAsync(
        string deviceSerial,
        DeviceChangeOptions currentOptions,
        bool useIntegritySecurityPatch,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogDebug("Opening advanced Change Device configuration for device {Serial}.", deviceSerial);

        using var scope = _scopeFactory.CreateScope();
        AdvancedChangeConfigViewModel viewModel =
            scope.ServiceProvider.GetRequiredService<AdvancedChangeConfigViewModel>();
        viewModel.Initialize(deviceSerial, currentOptions, useIntegritySecurityPatch);

        AdvancedChangeConfigDialog window =
            scope.ServiceProvider.GetRequiredService<AdvancedChangeConfigDialog>();
        window.Owner = Application.Current?.MainWindow;
        window.DataContext = viewModel;

        AdvancedChangeConfigDialogResult? result = null;
        viewModel.CloseRequested += (_, dialogResult) =>
        {
            result = dialogResult;
            window.DialogResult = dialogResult != null;
        };

        window.ShowDialog();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(result);
    }

    public Task<AdvancedChangeConfigDialogResult?> ShowAdvancedChangeConfigAsync(
        IReadOnlyList<string> deviceSerials,
        DeviceChangeOptions currentOptions,
        bool useIntegritySecurityPatch,
        bool isMultiple,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(deviceSerials);
        if (deviceSerials.Count == 0)
            throw new ArgumentException("At least one device serial is required.", nameof(deviceSerials));

        _logger.LogDebug(
            "Opening advanced Change Device configuration for {DeviceCount} device(s).",
            deviceSerials.Count);

        using var scope = _scopeFactory.CreateScope();
        AdvancedChangeConfigViewModel viewModel =
            scope.ServiceProvider.GetRequiredService<AdvancedChangeConfigViewModel>();
        viewModel.Initialize(deviceSerials, currentOptions, useIntegritySecurityPatch, isMultiple);

        AdvancedChangeConfigDialog window =
            scope.ServiceProvider.GetRequiredService<AdvancedChangeConfigDialog>();
        window.Owner = Application.Current?.MainWindow;
        window.DataContext = viewModel;

        AdvancedChangeConfigDialogResult? result = null;
        viewModel.CloseRequested += (_, dialogResult) =>
        {
            result = dialogResult;
            window.DialogResult = dialogResult != null;
        };

        window.ShowDialog();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(result);
    }
}
