using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Data;
using Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections;
using Jellyfin.Plugin.HomeScreenSections.JellyfinVersionSpecific;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using Jellyfin.Plugin.HomeScreenSections.Services;
using Jellyfin.Plugin.HomeScreenSections.Tests.Support;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Jellyfin.Plugin.HomeScreenSections.Tests.Services;

[Collection("Plugin Instance")]
public class ServiceInternalPathsTests
{
    private readonly Mock<IHomeScreenManager> m_homeScreenManager = new();
    private readonly Mock<ITranslationManager> m_translationManager = new();
    private readonly Mock<IServerConfigurationManager> m_serverConfigurationManager = new();
    private readonly Mock<IUserManager> m_userManager = new();
    private readonly Mock<ILibraryManager> m_libraryManager = new();
    private readonly Mock<IDtoService> m_dtoService = new();
    private readonly Mock<ICollectionManager> m_collectionManager = new();
    private readonly Mock<IPlaylistManager> m_playlistManager = new();
    private readonly UserSectionsDataCache m_dataCache = new();
    private readonly User m_user = new("ServiceUser", "AuthProvider", "PasswordResetProvider");
    private readonly Guid m_userId = Guid.NewGuid();

    public ServiceInternalPathsTests(PluginFixture fixture)
    {
        _ = fixture;

        MediaBrowser.Model.Configuration.ServerConfiguration serverConfiguration = new MediaBrowser.Model.Configuration.ServerConfiguration
        {
            UICulture = "de-DE"
        };
        m_serverConfigurationManager
            .Setup(manager => manager.Configuration)
            .Returns(serverConfiguration);

        m_translationManager
            .Setup(manager => manager.Translate(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<TranslationMetadata?>()))
            .Returns((string key, string language, string fallback, TranslationMetadata? metadata) => fallback);

        m_userManager
            .Setup(manager => manager.GetUserById(m_userId))
            .Returns(m_user);
    }

    private HomeScreenSectionService MakeService(MediaBrowser.Controller.Collections.ICollectionManager? collectionManager = null)
    {
        return new HomeScreenSectionService(
            m_homeScreenManager.Object,
            NullLogger<HomeScreenSectionsPlugin>.Instance,
            m_translationManager.Object,
            m_dataCache,
            m_serverConfigurationManager.Object,
            m_userManager.Object,
            m_libraryManager.Object,
            m_dtoService.Object,
            new CollectionManagerProxy(collectionManager ?? m_collectionManager.Object),
            m_playlistManager.Object);
    }

    private static PluginDefinedSection MakeSection(string additionalData)
    {
        return new PluginDefinedSection("LinkSection", "Link Section", additionalData: additionalData)
        {
            OnGetResults = _ => new QueryResult<BaseItemDto>()
        };
    }

    private Guid SeedPage(IHomeScreenSection section)
    {
        Guid pageHash = Guid.NewGuid();
        UserSectionsData data = new UserSectionsData
        {
            UserId = m_userId,
            MaxOrderIndex = 0
        };
        data.OrderedSections[0] = [section];
        m_dataCache.Cache[pageHash] = data;
        return pageHash;
    }

    [Fact]
    public void SectionToInfo_falls_back_to_system_ui_culture_without_language()
    {
        HomeScreenSectionService service = MakeService();
        Guid pageHash = SeedPage(MakeSection(string.Empty));

        IReadOnlyList<HomeScreenSectionInfo>? result = service.GetCachedSectionsForUser(m_userId, null, 1, 10, pageHash);

        Assert.NotNull(result);
        m_translationManager.Verify(
            manager => manager.Translate("LinkSection", "de-DE", "Link Section", null),
            Times.Once());
    }

    [Fact]
    public void TitleLink_resolves_guid_additional_data_to_item_dto()
    {
        Guid itemId = Guid.NewGuid();
        Movie item = new Movie { Id = itemId, Name = "Linked Movie" };
        BaseItemDto marker = new BaseItemDto { Id = itemId, Name = "Linked Movie" };

        m_libraryManager
            .Setup(manager => manager.GetItemById(itemId))
            .Returns(item);
        m_dtoService
            .Setup(service => service.GetBaseItemDto(item, It.IsAny<DtoOptions>(), m_user, It.IsAny<BaseItem>()))
            .Returns(marker);

        HomeScreenSectionService service = MakeService();
        Guid pageHash = SeedPage(MakeSection(itemId.ToString()));

        IReadOnlyList<HomeScreenSectionInfo>? result = service.GetCachedSectionsForUser(m_userId, "en", 1, 10, pageHash);

        Assert.Same(marker, Assert.Single(result!).OriginalPayload);
    }

    [Fact]
    public void TitleLink_resolves_genre_name_additional_data()
    {
        Genre genre = new Genre { Id = Guid.NewGuid(), Name = "Action" };
        BaseItemDto marker = new BaseItemDto { Id = genre.Id, Name = "Action" };

        m_libraryManager
            .Setup(manager => manager.GetGenre("Action"))
            .Returns(genre);
        m_dtoService
            .Setup(service => service.GetBaseItemDto(genre, It.IsAny<DtoOptions>(), m_user, It.IsAny<BaseItem>()))
            .Returns(marker);

        HomeScreenSectionService service = MakeService();
        Guid pageHash = SeedPage(MakeSection("Action"));

        IReadOnlyList<HomeScreenSectionInfo>? result = service.GetCachedSectionsForUser(m_userId, "en", 1, 10, pageHash);

        Assert.Same(marker, Assert.Single(result!).OriginalPayload);
    }

    [Fact]
    public void TitleLink_resolves_collection_name_additional_data()
    {
        // ResolveTitleLinkByName tries collections first; FakeCollectionManager exposes the
        // private GetCollections(User) the proxy resolves via reflection.
        TestBoxSet collection = new(Array.Empty<BaseItem>())
        {
            Id = Guid.NewGuid(),
            Name = "My Collection"
        };
        BaseItemDto marker = new BaseItemDto { Id = collection.Id, Name = "My Collection" };

        m_dtoService
            .Setup(service => service.GetBaseItemDto(collection, It.IsAny<DtoOptions>(), m_user, It.IsAny<BaseItem>()))
            .Returns(marker);

        HomeScreenSectionService service = MakeService(new FakeCollectionManager([collection]));
        Guid pageHash = SeedPage(MakeSection("My Collection"));

        IReadOnlyList<HomeScreenSectionInfo>? result = service.GetCachedSectionsForUser(m_userId, "en", 1, 10, pageHash);

        Assert.Same(marker, Assert.Single(result!).OriginalPayload);
    }

    [Fact]
    public void TitleLink_resolves_playlist_name_additional_data()
    {
        Guid playlistId = Guid.NewGuid();
        TestPlaylist playlist = new(Array.Empty<BaseItem>())
        {
            Id = playlistId,
            Name = "Road Trip"
        };
        BaseItemDto marker = new BaseItemDto { Id = playlistId, Name = "Road Trip" };

        m_playlistManager
            .Setup(manager => manager.GetPlaylists(m_userId))
            .Returns([playlist]);
        m_dtoService
            .Setup(service => service.GetBaseItemDto(playlist, It.IsAny<DtoOptions>(), m_user, It.IsAny<BaseItem>()))
            .Returns(marker);

        HomeScreenSectionService service = MakeService();
        Guid pageHash = SeedPage(MakeSection("Road Trip"));

        IReadOnlyList<HomeScreenSectionInfo>? result = service.GetCachedSectionsForUser(m_userId, "en", 1, 10, pageHash);

        Assert.Same(marker, Assert.Single(result!).OriginalPayload);
    }

    [Fact]
    public void TitleLink_stays_null_when_resolution_throws()
    {
        Guid itemId = Guid.NewGuid();
        m_libraryManager
            .Setup(manager => manager.GetItemById(itemId))
            .Throws(new InvalidOperationException("boom"));

        HomeScreenSectionService service = MakeService();
        Guid pageHash = SeedPage(MakeSection(itemId.ToString()));

        IReadOnlyList<HomeScreenSectionInfo>? result = service.GetCachedSectionsForUser(m_userId, "en", 1, 10, pageHash);

        Assert.Null(Assert.Single(result!).OriginalPayload);
    }

    [Fact]
    public void MonitorLiveUpdatedSections_with_page_hash_serves_seeded_page()
    {
        HomeScreenSectionService service = MakeService();
        Guid pageHash = SeedPage(MakeSection(string.Empty));

        IReadOnlyList<HomeScreenSectionInfo>? result =
            service.MonitorLiveUpdatedSectionsForUser(m_userId, "en", 1, 10, pageHash);

        Assert.NotNull(result);
        Assert.Equal("LinkSection", Assert.Single(result!).Section);
    }

    [Fact]
    public void MonitorLiveUpdatedSections_with_page_hash_returns_built_page_sections()
    {
        PluginConfiguration config = HomeScreenSectionsPlugin.Instance.Configuration;
        SectionSettings[] original = config.SectionSettings;
        config.SectionSettings =
        [
            new SectionSettings { SectionId = "OnDemand", Enabled = true, OrderIndex = 0 }
        ];
        try
        {
            m_homeScreenManager
                .Setup(manager => manager.GetUserSettings(m_userId))
                .Returns(new ModularHomeUserSettings { UserId = m_userId, EnabledSections = ["OnDemand"] });
            m_homeScreenManager
                .Setup(manager => manager.GetSectionTypes())
                .Returns(new IHomeScreenSection[]
                {
                    new PluginDefinedSection("OnDemand", "On Demand")
                    {
                        OnGetResults = _ => new QueryResult<BaseItemDto>()
                    }
                });

            HomeScreenSectionService service = MakeService();
            Guid pageHash = Guid.NewGuid();

            // Build the page synchronously. MonitorLiveUpdatedSectionsForUser's on-demand path
            // otherwise spins up a fire-and-forget background build, which races with the reads
            // below and makes the test flaky; building first keeps it deterministic while still
            // exercising the pageHash branch end to end.
            service.CacheSectionsForUser(m_userId, pageHash);

            IReadOnlyList<HomeScreenSectionInfo>? result =
                service.MonitorLiveUpdatedSectionsForUser(m_userId, "en", 1, 10, pageHash);

            Assert.NotNull(result);
            Assert.Equal("OnDemand", Assert.Single(result!).Section);
        }
        finally
        {
            config.SectionSettings = original;
        }
    }

    [Fact]
    public void CacheSections_isolates_section_creation_failures()
    {
        PluginConfiguration config = HomeScreenSectionsPlugin.Instance.Configuration;
        SectionSettings[] original = config.SectionSettings;
        config.SectionSettings =
        [
            new SectionSettings { SectionId = "Throwing", Enabled = true, OrderIndex = 0 }
        ];
        try
        {
            m_homeScreenManager
                .Setup(manager => manager.GetUserSettings(m_userId))
                .Returns(new ModularHomeUserSettings { UserId = m_userId, EnabledSections = ["Throwing"] });
            m_homeScreenManager
                .Setup(manager => manager.GetSectionTypes())
                .Returns(new IHomeScreenSection[] { new ThrowingSection() });

            HomeScreenSectionService service = MakeService();
            Guid pageHash = Guid.NewGuid();

            // Must not throw despite the section's CreateInstances exploding.
            service.CacheSectionsForUser(m_userId, pageHash);

            IReadOnlyList<HomeScreenSectionInfo>? result = service.GetCachedSectionsForUser(m_userId, "en", 1, 10, pageHash);
            Assert.NotNull(result);
            Assert.Empty(result!);
        }
        finally
        {
            config.SectionSettings = original;
        }
    }

    private sealed class ThrowingSection : IHomeScreenSection
    {
        public string? Section => "Throwing";

        public string? DisplayText { get; set; } = "Throwing";

        public int? Limit => 1;

        public string? Route => null;

        public string? AdditionalData { get; set; }

        public object? OriginalPayload => null;

        public QueryResult<BaseItemDto> GetResults(HomeScreenSectionPayload payload, IQueryCollection queryCollection)
        {
            return new QueryResult<BaseItemDto>();
        }

        public IEnumerable<IHomeScreenSection> CreateInstances(Guid? userId, int instanceCount)
        {
            throw new InvalidOperationException("section exploded");
        }

        public HomeScreenSectionInfo GetInfo()
        {
            return new HomeScreenSectionInfo
            {
                Section = Section,
                DisplayText = DisplayText
            };
        }
    }
}
