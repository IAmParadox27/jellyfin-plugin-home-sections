using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Data;
using Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections;
using Jellyfin.Plugin.HomeScreenSections.JellyfinVersionSpecific;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using Jellyfin.Plugin.HomeScreenSections.Services;
using Jellyfin.Plugin.HomeScreenSections.Tests.Support;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Jellyfin.Plugin.HomeScreenSections.Tests.Services;

[Collection("Plugin Instance")]
public class HomeScreenSectionServiceTests
{
    private readonly Mock<IHomeScreenManager> m_homeScreenManager = new();
    private readonly Mock<ITranslationManager> m_translationManager = new();
    private readonly Mock<IServerConfigurationManager> m_serverConfigurationManager = new();
    private readonly Mock<IUserManager> m_userManager = new();
    private readonly Mock<ILibraryManager> m_libraryManager = new();
    private readonly Mock<IDtoService> m_dtoService = new();
    private readonly Mock<MediaBrowser.Controller.Collections.ICollectionManager> m_collectionManager = new();
    private readonly Mock<IPlaylistManager> m_playlistManager = new();
    private readonly UserSectionsDataCache m_dataCache = new();

    public HomeScreenSectionServiceTests(PluginFixture fixture)
    {
        _ = fixture;

        m_translationManager
            .Setup(manager => manager.Translate(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<TranslationMetadata?>()))
            .Returns((string key, string language, string fallback, TranslationMetadata? metadata) => fallback);

        m_serverConfigurationManager
            .Setup(manager => manager.Configuration)
            .Returns(new ServerConfiguration());
    }

    private HomeScreenSectionService MakeService()
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
            new CollectionManagerProxy(m_collectionManager.Object),
            m_playlistManager.Object);
    }

    private static PluginDefinedSection MakeSection(string sectionId, string displayText)
    {
        return new PluginDefinedSection(sectionId, displayText)
        {
            OnGetResults = _ => new QueryResult<BaseItemDto>()
        };
    }

    private static UserSectionsData SeedPage(Guid userId, params (int Order, IHomeScreenSection Section)[] sections)
    {
        UserSectionsData data = new UserSectionsData
        {
            UserId = userId,
            MaxOrderIndex = sections.Length > 0 ? sections.Max(s => s.Order) : 0
        };
        foreach ((int order, IHomeScreenSection section) in sections)
        {
            data.OrderedSections[order] = new[] { section };
        }
        return data;
    }

    [Fact]
    public void GetCachedSectionsForUser_returns_null_for_unknown_page()
    {
        HomeScreenSectionService service = MakeService();

        Assert.Null(service.GetCachedSectionsForUser(Guid.NewGuid(), "en", 1, 10, Guid.NewGuid()));
    }

    [Fact]
    public void GetCachedSectionsForUser_returns_ordered_infos_for_complete_page()
    {
        HomeScreenSectionService service = MakeService();
        Guid userId = Guid.NewGuid();
        Guid pageHash = Guid.NewGuid();
        m_dataCache.Cache[pageHash] = SeedPage(
            userId,
            (0, MakeSection("First", "First Section")),
            (1, MakeSection("Second", "Second Section")));

        IReadOnlyList<HomeScreenSectionInfo>? result = service.GetCachedSectionsForUser(userId, "en", 1, 10, pageHash);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Count);
        Assert.Equal("First", result[0].Section);
        Assert.Equal(0, result[0].OrderIndex);
        Assert.Equal("Second", result[1].Section);
        Assert.Equal(1, result[1].OrderIndex);
        Assert.NotNull(m_dataCache.Cache[pageHash].LastAccessed);
    }

    [Fact]
    public void GetCachedSectionsForUser_returns_null_when_order_has_unexplained_gap()
    {
        HomeScreenSectionService service = MakeService();
        Guid userId = Guid.NewGuid();
        Guid pageHash = Guid.NewGuid();
        m_dataCache.Cache[pageHash] = SeedPage(
            userId,
            (0, MakeSection("First", "First")),
            (2, MakeSection("Third", "Third")));

        Assert.Null(service.GetCachedSectionsForUser(userId, "en", 1, 10, pageHash));
    }

    [Fact]
    public void GetCachedSectionsForUser_accepts_gap_covered_by_empty_index_range()
    {
        HomeScreenSectionService service = MakeService();
        Guid userId = Guid.NewGuid();
        Guid pageHash = Guid.NewGuid();
        // Keys 1 and 3 leave index 2 empty; a range covering index 2 marks the page cohesive.
        UserSectionsData data = SeedPage(
            userId,
            (1, MakeSection("First", "First")),
            (3, MakeSection("Third", "Third")));
        data.OrderIndicesWithoutSections.Add(new IntRange { Start = 2, End = 2 });
        m_dataCache.Cache[pageHash] = data;

        IReadOnlyList<HomeScreenSectionInfo>? result = service.GetCachedSectionsForUser(userId, "en", 1, 10, pageHash);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Count);
    }

    [Fact]
    public void GetCachedSectionsForUser_returns_null_while_sections_still_building()
    {
        HomeScreenSectionService service = MakeService();
        Guid userId = Guid.NewGuid();
        Guid pageHash = Guid.NewGuid();
        UserSectionsData data = SeedPage(userId, (0, MakeSection("First", "First")));
        data.SectionsInProgress[1] = true;
        m_dataCache.Cache[pageHash] = data;

        Assert.Null(service.GetCachedSectionsForUser(userId, "en", 1, 10, pageHash));
    }

    [Fact]
    public void GetCachedSectionsForUser_returns_full_page_before_completeness_check()
    {
        HomeScreenSectionService service = MakeService();
        Guid userId = Guid.NewGuid();
        Guid pageHash = Guid.NewGuid();
        // Sections 0 and 1 are ready but section 2 is still building, so the page is NOT
        // complete; with pageSize=2 the two ready sections must still be returned via the
        // "full page" short-circuit. If that short-circuit were removed this would return null.
        UserSectionsData data = SeedPage(
            userId,
            (0, MakeSection("First", "First")),
            (1, MakeSection("Second", "Second")));
        data.MaxOrderIndex = 2;
        data.SectionsInProgress[2] = true;
        m_dataCache.Cache[pageHash] = data;

        IReadOnlyList<HomeScreenSectionInfo>? result = service.GetCachedSectionsForUser(userId, "en", 1, 2, pageHash);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Count);
    }

    [Fact]
    public void CacheSectionsForUser_builds_enabled_sections_from_admin_settings()
    {
        HomeScreenSectionService service = MakeService();
        Guid userId = Guid.NewGuid();
        Guid pageHash = Guid.NewGuid();

        PluginConfiguration config = HomeScreenSectionsPlugin.Instance.Configuration;
        SectionSettings[] original = config.SectionSettings;
        config.SectionSettings =
        [
            new SectionSettings { SectionId = "TestSection", Enabled = true, OrderIndex = 0 }
        ];
        try
        {
            m_homeScreenManager
                .Setup(manager => manager.GetUserSettings(userId))
                .Returns(new ModularHomeUserSettings { UserId = userId, EnabledSections = ["TestSection"] });
            m_homeScreenManager
                .Setup(manager => manager.GetSectionTypes())
                .Returns(new[] { MakeSection("TestSection", "Test") });

            service.CacheSectionsForUser(userId, pageHash);

            IReadOnlyList<HomeScreenSectionInfo>? result = service.GetCachedSectionsForUser(userId, "en", 1, 10, pageHash);
            Assert.NotNull(result);
            HomeScreenSectionInfo info = Assert.Single(result!);
            Assert.Equal("TestSection", info.Section);
            Assert.Equal(0, info.OrderIndex);
        }
        finally
        {
            config.SectionSettings = original;
        }
    }

    [Fact]
    public void CacheSectionsForUser_is_idempotent_for_existing_page()
    {
        HomeScreenSectionService service = MakeService();
        Guid userId = Guid.NewGuid();
        Guid pageHash = Guid.NewGuid();
        UserSectionsData seeded = SeedPage(userId);
        m_dataCache.Cache[pageHash] = seeded;

        // Second call must not throw or overwrite the existing page.
        service.CacheSectionsForUser(userId, pageHash);

        Assert.Same(seeded, m_dataCache.Cache[pageHash]);
        m_homeScreenManager.Verify(manager => manager.GetUserSettings(It.IsAny<Guid>()), Times.Never());
    }

    [Fact]
    public void MonitorLiveUpdatedSectionsForUser_without_page_hash_builds_and_returns_full_page()
    {
        HomeScreenSectionService service = MakeService();
        Guid userId = Guid.NewGuid();

        PluginConfiguration config = HomeScreenSectionsPlugin.Instance.Configuration;
        SectionSettings[] original = config.SectionSettings;
        config.SectionSettings =
        [
            new SectionSettings { SectionId = "LiveSection", Enabled = true, OrderIndex = 0 }
        ];
        try
        {
            m_homeScreenManager
                .Setup(manager => manager.GetUserSettings(userId))
                .Returns(new ModularHomeUserSettings { UserId = userId, EnabledSections = ["LiveSection"] });
            m_homeScreenManager
                .Setup(manager => manager.GetSectionTypes())
                .Returns(new[] { MakeSection("LiveSection", "Live") });

            IReadOnlyList<HomeScreenSectionInfo>? result = service.MonitorLiveUpdatedSectionsForUser(userId, "en", 1);

            Assert.NotNull(result);
            Assert.Equal("LiveSection", Assert.Single(result!).Section);
        }
        finally
        {
            config.SectionSettings = original;
        }
    }

    [Fact]
    public void MonitorLiveUpdatedSectionsForUser_with_empty_section_settings_returns_empty_page()
    {
        // Regression for upstream #247: a fresh install has no admin SectionSettings yet.
        // The unguarded Enumerable.Max used to throw "Sequence contains no elements" and
        // 500 the whole endpoint; it must return an empty page instead.
        HomeScreenSectionService service = MakeService();
        Guid userId = Guid.NewGuid();

        PluginConfiguration config = HomeScreenSectionsPlugin.Instance.Configuration;
        SectionSettings[] original = config.SectionSettings;
        config.SectionSettings = [];
        try
        {
            m_homeScreenManager
                .Setup(manager => manager.GetUserSettings(userId))
                .Returns(new ModularHomeUserSettings { UserId = userId, EnabledSections = ["ContinueWatching"] });
            m_homeScreenManager
                .Setup(manager => manager.GetSectionTypes())
                .Returns(new[] { MakeSection("ContinueWatching", "Continue Watching") });

            IReadOnlyList<HomeScreenSectionInfo>? result = service.MonitorLiveUpdatedSectionsForUser(userId, "en", 1);

            Assert.NotNull(result);
            Assert.Empty(result!);
        }
        finally
        {
            config.SectionSettings = original;
        }
    }

    [Fact(Timeout = 30000)]
    public void MonitorLiveUpdatedSectionsForUser_with_empty_section_settings_and_page_hash_does_not_hang()
    {
        // Same fresh-install scenario as above, but through the paginated path where the
        // cache is built on a background task and the request busy-waits for it.
        HomeScreenSectionService service = MakeService();
        Guid userId = Guid.NewGuid();

        PluginConfiguration config = HomeScreenSectionsPlugin.Instance.Configuration;
        SectionSettings[] original = config.SectionSettings;
        config.SectionSettings = [];
        try
        {
            m_homeScreenManager
                .Setup(manager => manager.GetUserSettings(userId))
                .Returns(new ModularHomeUserSettings { UserId = userId, EnabledSections = ["ContinueWatching"] });
            m_homeScreenManager
                .Setup(manager => manager.GetSectionTypes())
                .Returns(new[] { MakeSection("ContinueWatching", "Continue Watching") });

            IReadOnlyList<HomeScreenSectionInfo>? result =
                service.MonitorLiveUpdatedSectionsForUser(userId, "en", 1, 10, Guid.NewGuid());

            Assert.NotNull(result);
            Assert.Empty(result!);
        }
        finally
        {
            config.SectionSettings = original;
        }
    }

    [Fact]
    public void CacheSectionsForUser_honours_user_defined_section_order()
    {
        HomeScreenSectionService service = MakeService();
        Guid userId = Guid.NewGuid();
        Guid pageHash = Guid.NewGuid();

        PluginConfiguration config = HomeScreenSectionsPlugin.Instance.Configuration;
        SectionSettings[] original = config.SectionSettings;
        config.SectionSettings =
        [
            new SectionSettings { SectionId = "B", Enabled = true, OrderIndex = 0 },
            new SectionSettings { SectionId = "A", Enabled = true, OrderIndex = 1 }
        ];
        try
        {
            // User puts "A" first despite admin ordering.
            m_homeScreenManager
                .Setup(manager => manager.GetUserSettings(userId))
                .Returns(new ModularHomeUserSettings
                {
                    UserId = userId,
                    EnabledSections = ["A", "B"],
                    SectionOrder = ["A", "B"]
                });
            m_homeScreenManager
                .Setup(manager => manager.GetSectionTypes())
                .Returns(new[] { MakeSection("A", "Section A"), MakeSection("B", "Section B") });

            service.CacheSectionsForUser(userId, pageHash);

            IReadOnlyList<HomeScreenSectionInfo>? result = service.GetCachedSectionsForUser(userId, "en", 1, 10, pageHash);
            Assert.NotNull(result);
            Assert.Equal(2, result!.Count);
            Assert.Equal("A", result[0].Section);
            Assert.Equal(0, result[0].OrderIndex);
            Assert.Equal("B", result[1].Section);
            Assert.Equal(1, result[1].OrderIndex);
        }
        finally
        {
            config.SectionSettings = original;
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    public void ResolveInstanceCount_returns_one_for_single_instance_sections(int? sectionLimit)
    {
        SectionSettings settings = new SectionSettings { LowerLimit = 3, UpperLimit = 7 };

        Assert.Equal(1, HomeScreenSectionService.ResolveInstanceCount(sectionLimit, settings));
    }

    [Fact]
    public void ResolveInstanceCount_defaults_unset_limits_to_one_instance()
    {
        // Regression for upstream #153: config defaults are 0/0, which used to produce
        // rnd.Next(0, 0) == 0 instances and silently removed the section from the home.
        SectionSettings settings = new SectionSettings();

        Assert.Equal(1, HomeScreenSectionService.ResolveInstanceCount(5, settings));
    }

    [Fact]
    public void ResolveInstanceCount_stays_within_inclusive_configured_bounds()
    {
        SectionSettings settings = new SectionSettings { LowerLimit = 2, UpperLimit = 4 };

        for (int i = 0; i < 200; i++)
        {
            int count = HomeScreenSectionService.ResolveInstanceCount(5, settings);
            Assert.InRange(count, 2, 4);
        }
    }

    [Fact]
    public void ResolveInstanceCount_clamps_inverted_limits_to_the_lower_bound()
    {
        SectionSettings settings = new SectionSettings { LowerLimit = 4, UpperLimit = 2 };

        Assert.Equal(4, HomeScreenSectionService.ResolveInstanceCount(5, settings));
    }

    [Fact]
    public void UserHomeSections_defaults_to_empty_section_list()
    {
        UserHomeSections homeSections = new UserHomeSections();

        Assert.Equal(Guid.Empty, homeSections.PageHash);
        Assert.Empty(homeSections.Sections);
    }

    [Fact]
    public void FillOrderIndicesWithoutSections_records_gaps_between_in_progress_indices()
    {
        UserSectionsData data = new UserSectionsData
        {
            UserId = Guid.NewGuid(),
            MaxOrderIndex = 5
        };
        data.SectionsInProgress.TryAdd(0, true);
        data.SectionsInProgress.TryAdd(3, true);
        data.SectionsInProgress.TryAdd(5, true);

        InvokeServiceStatic("FillOrderIndicesWithoutSections", data);

        Assert.Equal(2, data.OrderIndicesWithoutSections.Count);
        Assert.Contains(data.OrderIndicesWithoutSections, r => r.Start == 1 && r.End == 2);
        Assert.Contains(data.OrderIndicesWithoutSections, r => r.Start == 4 && r.End == 4);
    }

    [Fact]
    public void BuildOrderedSectionGroups_orders_by_admin_index_without_user_order()
    {
        PluginConfiguration config = HomeScreenSectionsPlugin.Instance.Configuration;
        SectionSettings[] original = config.SectionSettings;
        config.SectionSettings =
        [
            new SectionSettings { SectionId = "A", OrderIndex = 5 },
            new SectionSettings { SectionId = "B", OrderIndex = 1 }
        ];
        try
        {
            object? result = InvokeServiceStatic("BuildOrderedSectionGroups", (ModularHomeUserSettings?)null);

            Assert.Equal(["B", "A"], EnumerateGroupSectionIds(result));
        }
        finally
        {
            config.SectionSettings = original;
        }
    }

    [Fact]
    public void BuildOrderedSectionGroups_prefers_user_defined_order()
    {
        PluginConfiguration config = HomeScreenSectionsPlugin.Instance.Configuration;
        SectionSettings[] original = config.SectionSettings;
        config.SectionSettings =
        [
            new SectionSettings { SectionId = "A", OrderIndex = 0 },
            new SectionSettings { SectionId = "B", OrderIndex = 1 },
            new SectionSettings { SectionId = "C", OrderIndex = 2 }
        ];
        try
        {
            ModularHomeUserSettings settings = new ModularHomeUserSettings
            {
                SectionOrder = ["C", "A", "B"]
            };

            object? result = InvokeServiceStatic("BuildOrderedSectionGroups", settings);

            Assert.Equal(["C", "A", "B"], EnumerateGroupSectionIds(result));
        }
        finally
        {
            config.SectionSettings = original;
        }
    }

    private static object? InvokeServiceStatic(string name, params object?[] args)
    {
        System.Reflection.MethodInfo method = typeof(HomeScreenSectionService)
            .GetMethod(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new InvalidOperationException($"Private static '{name}' not found on {nameof(HomeScreenSectionService)}.");
        return method.Invoke(null, args);
    }

    private static List<string> EnumerateGroupSectionIds(object? groupingsResult)
    {
        List<string> orderedIds = [];
        foreach (object group in (System.Collections.IEnumerable)groupingsResult!)
        {
            foreach (SectionSettings section in (System.Collections.IEnumerable)group)
            {
                orderedIds.Add(section.SectionId);
            }
        }

        return orderedIds;
    }
}
