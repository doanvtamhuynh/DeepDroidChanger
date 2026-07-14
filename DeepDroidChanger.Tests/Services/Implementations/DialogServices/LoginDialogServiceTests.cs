using DeepDroidChanger.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DeepDroidChanger.Tests.Services.Implementations.DialogServices;

[TestClass]
public sealed class LoginDialogServiceTests
{
    [TestMethod]
    public async Task ShowLoginAsync_ResolutionFailure_DisposesTransientScope()
    {
        IServiceProvider provider = Substitute.For<IServiceProvider>();
        IServiceScope scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(provider);
        IServiceScopeFactory scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);
        var service = new LoginDialogService(scopeFactory, NullLogger<LoginDialogService>.Instance);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => service.ShowLoginAsync(CancellationToken.None));

        scope.Received(1).Dispose();
    }

    [TestMethod]
    public async Task ShowLoginAsync_PreCancelled_DoesNotCreateScope()
    {
        IServiceScopeFactory scopeFactory = Substitute.For<IServiceScopeFactory>();
        var service = new LoginDialogService(scopeFactory, NullLogger<LoginDialogService>.Instance);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => service.ShowLoginAsync(cancellation.Token));

        scopeFactory.DidNotReceive().CreateScope();
    }
}
