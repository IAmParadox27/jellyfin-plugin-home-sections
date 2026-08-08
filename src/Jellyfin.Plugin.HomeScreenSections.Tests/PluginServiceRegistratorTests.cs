using Jellyfin.Plugin.HomeScreenSections.Data;
using Jellyfin.Plugin.HomeScreenSections.HomeScreen;
using Jellyfin.Plugin.HomeScreenSections.JellyfinVersionSpecific;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Services;
using Jellyfin.Plugin.HomeScreenSections.Tests.Support;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Jellyfin.Plugin.HomeScreenSections.Tests;

public class PluginServiceRegistratorTests : IDisposable
{
    private readonly FakeApplicationPaths _paths =
        new(Path.Combine(Path.GetTempPath(), "hss-registrator-tests", Guid.NewGuid().ToString("N")));

    [Fact]
    public void RegisterServices_registers_all_plugin_services()
    {
        ServiceCollection services = new ServiceCollection();
        Mock<IServerApplicationHost> applicationHost = new();

        new PluginServiceRegistrator().RegisterServices(services, applicationHost.Object);

        List<ServiceDescriptor> descriptors = [.. services];
        Assert.Contains(descriptors, d => d.ServiceType == typeof(CollectionManagerProxy));
        Assert.Contains(descriptors, d => d.ServiceType == typeof(HomeScreenSectionService));
        Assert.Contains(descriptors, d => d.ServiceType == typeof(ArrApiService));
        Assert.Contains(descriptors, d => d.ServiceType == typeof(ImageCacheService));
        Assert.Contains(descriptors, d => d.ServiceType == typeof(UserSectionsDataCache));
        Assert.Contains(descriptors, d => d.ServiceType == typeof(ITranslationManager) && d.ImplementationType == typeof(TranslationManager));
        Assert.Contains(descriptors, d => d.ServiceType == typeof(IHomeScreenManager));
        Assert.Contains(descriptors, d => d.ServiceType == typeof(IHttpClientFactory));
    }

    [Fact]
    public void Registered_factories_resolve_against_a_real_provider()
    {
        ServiceCollection services = new ServiceCollection();
        Mock<IServerApplicationHost> applicationHost = new();

        new PluginServiceRegistrator().RegisterServices(services, applicationHost.Object);

        services.AddSingleton<IApplicationPaths>(_paths);
        services.AddSingleton<IServerConfigurationManager>(new Mock<IServerConfigurationManager>().Object);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton(NullLogger.Instance);
        services.AddSingleton<ILogger<HomeScreenManager>>(NullLogger<HomeScreenManager>.Instance);

        using ServiceProvider provider = services.BuildServiceProvider();

        // Simple singletons.
        Assert.NotNull(provider.GetRequiredService<UserSectionsDataCache>());
        Assert.NotNull(provider.GetRequiredService<ITranslationManager>());

        // Factory registrations pull IHttpClientFactory + loggers from the container.
        Assert.NotNull(provider.GetRequiredService<ArrApiService>());
        Assert.NotNull(provider.GetRequiredService<ImageCacheService>());

        // The HomeScreenManager factory also scans the plugin config dir for extra section DLLs;
        // the temp directory has none, so the built-in manager must come back cleanly.
        IHomeScreenManager homeScreenManager = provider.GetRequiredService<IHomeScreenManager>();
        Assert.IsType<HomeScreenManager>(homeScreenManager);
        Assert.True(Directory.Exists(Path.Combine(_paths.PluginConfigurationsPath, "Jellyfin.Plugin.HomeScreenSections")));
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        TestIO.DeleteBestEffort(_paths.Root);
    }
}
