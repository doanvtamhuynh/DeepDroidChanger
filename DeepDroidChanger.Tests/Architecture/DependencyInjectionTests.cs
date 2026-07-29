using DeepDroidChanger.Authentication;
using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using DeepDroidChanger.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace DeepDroidChanger.Tests.Architecture;

[TestClass]
public sealed class DependencyInjectionTests
{
    [TestMethod]
    public void RegisterServices_AllServiceInterfacesHaveDirectRegistrations()
    {
        ServiceCollection services = new();
        services.AddLogging();
        App.RegisterServices(services, new AppSettings());
        Type[] serviceInterfaces = typeof(ISettingsService).Assembly.GetTypes()
            .Where(type => type.IsInterface
                && type.Namespace == "DeepDroidChanger.Services"
                && type.Name.EndsWith("Service", StringComparison.Ordinal))
            .ToArray();
        Type[] authenticationInterfaces = typeof(IAccountAuthenticationService).Assembly
            .GetExportedTypes()
            .Where(type => type.IsInterface)
            .ToArray();
        Type[] missing = serviceInterfaces
            .Concat(authenticationInterfaces)
            .Where(serviceType => !services.Any(descriptor => descriptor.ServiceType == serviceType))
            .ToArray();

        Assert.IsEmpty(missing, $"Missing direct DI registrations: {string.Join(", ", missing.Select(type => type.Name))}");
    }

    [TestMethod]
    public void RegisterServices_UsesRequiredSingletonAndTransientLifetimes()
    {
        ServiceCollection services = new();
        services.AddLogging();
        App.RegisterServices(services, new AppSettings());

        AssertLifetime<ISettingsService>(services, ServiceLifetime.Singleton);
        AssertLifetime<IAccountAuthenticationService>(services, ServiceLifetime.Singleton);
        AssertLifetime<IAccountStoreService>(services, ServiceLifetime.Singleton);
        AssertLifetime<IAuthenticationSessionService>(services, ServiceLifetime.Singleton);
        AssertLifetime<IIdentityProviderClient>(services, ServiceLifetime.Singleton);
        AssertLifetime<IAdbCommandService>(services, ServiceLifetime.Singleton);
        AssertLifetime<IDeviceActionGuardService>(services, ServiceLifetime.Singleton);
        AssertLifetime<MainViewModel>(services, ServiceLifetime.Singleton);
        AssertLifetime<DeviceManagerViewModel>(services, ServiceLifetime.Singleton);
        AssertLifetime<MainWindow>(services, ServiceLifetime.Singleton);
        AssertLifetime<ILoginDialogService>(services, ServiceLifetime.Transient);
        AssertLifetime<IDeviceViewerDialogService>(services, ServiceLifetime.Transient);
        AssertLifetime<LoginViewModel>(services, ServiceLifetime.Transient);
        AssertLifetime<DeviceViewerViewModel>(services, ServiceLifetime.Transient);
    }

    [TestMethod]
    public void RegisterServices_CoreGraph_ResolvesWithoutServiceLocatorFailures()
    {
        ServiceCollection services = new();
        services.AddLogging();
        App.RegisterServices(services, new AppSettings());

        using ServiceProvider provider = services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });

        Assert.IsNotNull(provider.GetRequiredService<ISettingsService>());
        Assert.IsNotNull(provider.GetRequiredService<IAdbCommandService>());
        Assert.IsNotNull(provider.GetRequiredService<IAccountAuthenticationService>());
        Assert.IsNotNull(provider.GetRequiredService<IAccountStoreService>());
        Assert.IsNotNull(provider.GetRequiredService<IAuthenticationSessionService>());
        Assert.IsNotNull(provider.GetRequiredService<DeviceManagerViewModel>());
    }

    private static void AssertLifetime<TService>(
        IEnumerable<ServiceDescriptor> services,
        ServiceLifetime expectedLifetime)
    {
        ServiceDescriptor? descriptor = services.LastOrDefault(item => item.ServiceType == typeof(TService));
        Assert.IsNotNull(descriptor, $"Missing DI registration for {typeof(TService).Name}.");
        Assert.AreEqual(expectedLifetime, descriptor.Lifetime, typeof(TService).Name);
    }
}
