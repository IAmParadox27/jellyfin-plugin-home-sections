using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Data;
using Jellyfin.Plugin.HomeScreenSections.Library;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Model.Serialization;
using Moq;

namespace Jellyfin.Plugin.HomeScreenSections.Tests.Support;

/// <summary>
/// Constructs HomeScreenSectionsPlugin with mocked Jellyfin services, which sets the
/// static Instance that most of the plugin dereferences. Tests that need Instance share
/// this fixture through the "Plugin Instance" collection.
/// </summary>
public sealed class PluginFixture : IDisposable
{
    public PluginFixture()
    {
        TempRoot = Path.Combine(Path.GetTempPath(), "hss-plugin-tests", Guid.NewGuid().ToString("N"));
        Paths = new FakeApplicationPaths(TempRoot);

        Mock<IXmlSerializer> xmlSerializer = new Mock<IXmlSerializer>();
        xmlSerializer
            .Setup(x => x.DeserializeFromFile(typeof(PluginConfiguration), It.IsAny<string>()))
            .Returns(() => new PluginConfiguration());

        SectionsCache = new UserSectionsDataCache();

        ServerApplicationHostMock = new Mock<IServerApplicationHost>();
        HomeScreenManagerMock = new Mock<IHomeScreenManager>();
        TranslationManagerMock = new Mock<ITranslationManager>();

        Mock<IServiceProvider> serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(p => p.GetService(typeof(UserSectionsDataCache)))
            .Returns(SectionsCache);
        // PluginInterface and friends resolve these through Instance.ServiceProvider.
        serviceProvider
            .Setup(p => p.GetService(typeof(IHomeScreenManager)))
            .Returns(HomeScreenManagerMock.Object);
        serviceProvider
            .Setup(p => p.GetService(typeof(IServerApplicationHost)))
            .Returns(ServerApplicationHostMock.Object);

        Mock<IServerConfigurationManager> serverConfigurationManager = new Mock<IServerConfigurationManager>();
        serverConfigurationManager
            .Setup(c => c.GetConfiguration(It.IsAny<string>()))
            .Returns(new NetworkConfiguration());

        Plugin = new HomeScreenSectionsPlugin(
            Paths,
            xmlSerializer.Object,
            serverConfigurationManager.Object,
            serviceProvider.Object,
            HomeScreenManagerMock.Object,
            TranslationManagerMock.Object);
    }

    public string TempRoot { get; }

    public FakeApplicationPaths Paths { get; }

    public HomeScreenSectionsPlugin Plugin { get; }

    public UserSectionsDataCache SectionsCache { get; }

    public Mock<IHomeScreenManager> HomeScreenManagerMock { get; }

    public Mock<ITranslationManager> TranslationManagerMock { get; }

    public Mock<IServerApplicationHost> ServerApplicationHostMock { get; }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        TestIO.DeleteBestEffort(TempRoot);
    }
}

[CollectionDefinition("Plugin Instance")]
public sealed class PluginInstanceCollectionDefinition : ICollectionFixture<PluginFixture>
{
}
