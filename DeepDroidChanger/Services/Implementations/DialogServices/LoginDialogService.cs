using DeepDroidChanger.ViewModels;
using DeepDroidChanger.Views;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services
{
    public sealed class LoginDialogService : ILoginDialogService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<LoginDialogService> _logger;

        public LoginDialogService(IServiceScopeFactory scopeFactory, ILogger<LoginDialogService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task<bool> ShowLoginAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogDebug("Opening startup login dialog.");
            using var scope = _scopeFactory.CreateScope();

            var viewModel = scope.ServiceProvider.GetRequiredService<LoginViewModel>();
            await viewModel.InitializeAsync(cancellationToken).ConfigureAwait(true);

            var window = scope.ServiceProvider.GetRequiredService<LoginDialog>();
            if (Application.Current?.MainWindow is { IsVisible: true } owner)
                window.Owner = owner;

            window.DataContext = viewModel;
            window.SetPassword(viewModel.Password);

            viewModel.CloseRequested += (_, result) =>
            {
                window.CompleteDialog(result);
            };

            var dialogResult = window.ShowDialog() ?? false;
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogDebug("Startup login dialog closed. Success: {Succeeded}.", dialogResult);
            return dialogResult;
        }
    }
}
