using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.HomeScreen;
using Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using Jellyfin.Plugin.HomeScreenSections.Tests.Support;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Jellyfin.Plugin.HomeScreenSections.Tests.HomeScreen;

/// <summary>
/// HomeScreenManager persists user settings and feature flags to Instance-relative config
/// paths, so these tests run inside the plugin fixture collection.
/// </summary>
[Collection("Plugin Instance")]
public class HomeScreenManagerTests : IDisposable
{
    private static readonly string[] s_expectedSectionIds =
    [
        "MyMedia",
        "ContinueWatching",
        "NextUp",
        "ContinueWatchingNextUp",
        "RecentlyAddedMovies",
        "RecentlyAddedShows",
        "RecentlyAddedAlbums",
        "RecentlyAddedArtists",
        "RecentlyAddedBooks",
        "RecentlyAddedAudioBooks",
        "RecentlyAddedMusicVideos",
        "LatestMovies",
        "LatestShows",
        "LatestAlbums",
        "LatestBooks",
        "LatestAudioBooks",
        "LatestMusicVideo",
        "BecauseYouWatched",
        "LiveTV",
        "MyList",
        "WatchAgain",
        "Discover",
        "DiscoverMovies",
        "DiscoverTV",
        "UpcomingShows",
        "UpcomingMovies",
        "UpcomingMusic",
        "UpcomingBooks",
        "Genre",
        "MyJellyseerrRequests",
        "Favorites",
        "RandomUnwatched",
        "Trending",
        "RecentlyPlayed",
        "Kids",
        "ComingSoonInLibrary",
        "Decade",
        "Studio",
        "Playlists",
        "UnwatchedCollections"
    ];

    private readonly FakeApplicationPaths _paths;
    private readonly TestServiceProvider _serviceProvider;

    public HomeScreenManagerTests(PluginFixture fixture)
    {
        _ = fixture;
        _paths = new FakeApplicationPaths(Path.Combine(fixture.TempRoot, "manager-" + Guid.NewGuid().ToString("N")));
        _serviceProvider = new TestServiceProvider(_paths);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        TestIO.DeleteBestEffort(_paths.Root);
    }

    private HomeScreenManager MakeManager()
    {
        return new HomeScreenManager(_serviceProvider, _paths, NullLogger<HomeScreenManager>.Instance);
    }

    [Fact]
    public void RegisterBuiltInResultsDelegates_registers_the_full_section_catalogue()
    {
        HomeScreenManager manager = MakeManager();

        manager.RegisterBuiltInResultsDelegates();

        List<IHomeScreenSection> sections = [.. manager.GetSectionTypes()];
        Assert.True(sections.Count >= 35, $"Expected at least 35 built-in sections, got {sections.Count}");

        HashSet<string> registered = sections
            .Select(section => section.Section)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        foreach (string expected in s_expectedSectionIds)
        {
            Assert.Contains(expected, registered, StringComparer.Ordinal);
        }
    }

    [Fact]
    public void RegisterBuiltInResultsDelegates_does_not_register_dev_only_sections()
    {
        HomeScreenManager manager = MakeManager();

        manager.RegisterBuiltInResultsDelegates();

        Assert.Null(manager.GetSection("TopTen"));
        Assert.Null(manager.GetSection("DirectedBy"));
        Assert.Null(manager.GetSection("Starring"));
    }

    [Fact]
    public void GetSection_returns_registered_instance_and_null_for_unknown()
    {
        HomeScreenManager manager = MakeManager();
        manager.RegisterBuiltInResultsDelegates();

        Assert.NotNull(manager.GetSection("NextUp"));
        Assert.Null(manager.GetSection("NotARealSection"));
    }

    [Fact]
    public void InvokeResultsDelegate_returns_empty_result_for_unknown_key()
    {
        HomeScreenManager manager = MakeManager();

        QueryResult<BaseItemDto> result = manager.InvokeResultsDelegate("missing", new HomeScreenSectionPayload(), new FakeQueryCollection());

        Assert.Empty(result.Items);
    }

    [Fact]
    public void InvokeResultsDelegate_routes_to_registered_section()
    {
        HomeScreenManager manager = MakeManager();
        HomeScreenSectionPayload? received = null;
        PluginDefinedSection section = new PluginDefinedSection("Custom", "Custom")
        {
            OnGetResults = payload =>
            {
                received = payload;
                return new QueryResult<BaseItemDto>([new BaseItemDto()]);
            }
        };
        manager.RegisterResultsDelegate(section);

        HomeScreenSectionPayload payload = new HomeScreenSectionPayload { UserId = Guid.NewGuid() };
        QueryResult<BaseItemDto> result = manager.InvokeResultsDelegate("Custom", payload, new FakeQueryCollection());

        Assert.Single(result.Items);
        Assert.Same(payload, received);
    }

    [Fact]
    public void RegisterResultsDelegate_by_type_throws_on_duplicate_section_id()
    {
        HomeScreenManager manager = MakeManager();
        manager.RegisterResultsDelegate(new PluginDefinedSection("NextUp", "Collision")
        {
            OnGetResults = _ => new QueryResult<BaseItemDto>()
        });

        // The Type overload is exercised deliberately here: it is the only overload that
        // rejects duplicate registrations, which is the behaviour under test.
#pragma warning disable CA2263
        Assert.Throws<InvalidOperationException>(() => manager.RegisterResultsDelegate(typeof(NextUpSection)));
#pragma warning restore CA2263
    }

    [Fact]
    public void RegisterResultsDelegate_instance_overload_keeps_first_registration_on_duplicate()
    {
        // Regression for upstream #258: the instance overload used to overwrite existing
        // handlers, letting external registrations replace built-in sections.
        HomeScreenManager manager = MakeManager();
        PluginDefinedSection original = new PluginDefinedSection("Duplicate", "Original")
        {
            OnGetResults = _ => new QueryResult<BaseItemDto>()
        };
        PluginDefinedSection impostor = new PluginDefinedSection("Duplicate", "Impostor")
        {
            OnGetResults = _ => new QueryResult<BaseItemDto>([new BaseItemDto()])
        };

        manager.RegisterResultsDelegate(original);
        manager.RegisterResultsDelegate(impostor);

        Assert.Same(original, manager.GetSection("Duplicate"));
    }

    [Fact]
    public void RegisterResultsDelegate_ignores_sections_without_id()
    {
        HomeScreenManager manager = MakeManager();

        manager.RegisterResultsDelegate(new PluginDefinedSection(null!, "No Id")
        {
            OnGetResults = _ => new QueryResult<BaseItemDto>()
        });

        Assert.Empty(manager.GetSectionTypes());
    }

    [Fact]
    public void User_feature_flags_persist_across_manager_instances()
    {
        HomeScreenManager manager = MakeManager();
        Guid userId = Guid.NewGuid();

        Assert.False(manager.GetUserFeatureEnabled(userId));

        manager.SetUserFeatureEnabled(userId, true);
        Assert.True(manager.GetUserFeatureEnabled(userId));

        HomeScreenManager reloaded = MakeManager();
        Assert.True(reloaded.GetUserFeatureEnabled(userId));
    }

    [Fact]
    public void GetUserSettings_without_saved_file_returns_admin_defaults()
    {
        HomeScreenManager manager = MakeManager();
        Guid userId = Guid.NewGuid();
        PluginConfiguration config = HomeScreenSectionsPlugin.Instance.Configuration;
        SectionSettings[] original = config.SectionSettings;
        config.SectionSettings =
        [
            new SectionSettings { SectionId = "One", Enabled = true, AllowUserOverride = true },
            new SectionSettings { SectionId = "Two", Enabled = true, AllowUserOverride = false },
            new SectionSettings { SectionId = "Three", Enabled = false, AllowUserOverride = true }
        ];
        try
        {
            ModularHomeUserSettings? settings = manager.GetUserSettings(userId);

            Assert.NotNull(settings);
            Assert.Equal(userId, settings!.UserId);
            Assert.Contains("One", settings.EnabledSections, StringComparer.Ordinal);
            Assert.Contains("Two", settings.EnabledSections, StringComparer.Ordinal);
            Assert.DoesNotContain("Three", settings.EnabledSections, StringComparer.Ordinal);
            Assert.Equal("Two", Assert.Single(settings.LockedSections));
        }
        finally
        {
            config.SectionSettings = original;
        }
    }

    [Fact]
    public void UpdateUserSettings_round_trips_through_GetUserSettings()
    {
        HomeScreenManager manager = MakeManager();
        Guid userId = Guid.NewGuid();
        ModularHomeUserSettings saved = new ModularHomeUserSettings
        {
            UserId = userId,
            EnabledSections = ["ContinueWatching", "NextUp"],
            SectionOrder = ["NextUp", "ContinueWatching"]
        };

        Assert.True(manager.UpdateUserSettings(userId, saved));

        ModularHomeUserSettings? loaded = manager.GetUserSettings(userId);
        Assert.NotNull(loaded);
        Assert.Equal(userId, loaded!.UserId);
        Assert.Equal(saved.EnabledSections, loaded.EnabledSections);
        Assert.Equal(saved.SectionOrder, loaded.SectionOrder);
    }

    [Fact]
    public void UpdateUserSettings_keeps_other_users_intact()
    {
        HomeScreenManager manager = MakeManager();
        Guid userA = Guid.NewGuid();
        Guid userB = Guid.NewGuid();

        manager.UpdateUserSettings(userA, new ModularHomeUserSettings { UserId = userA, EnabledSections = ["A1"] });
        manager.UpdateUserSettings(userB, new ModularHomeUserSettings { UserId = userB, EnabledSections = ["B1"] });
        manager.UpdateUserSettings(userA, new ModularHomeUserSettings { UserId = userA, EnabledSections = ["A2"] });

        ModularHomeUserSettings? loadedA = manager.GetUserSettings(userA);
        ModularHomeUserSettings? loadedB = manager.GetUserSettings(userB);
        Assert.Equal(["A2"], loadedA!.EnabledSections);
        Assert.Equal(["B1"], loadedB!.EnabledSections);
    }

    [Fact]
    public void GetUserSettings_forces_admin_locked_sections()
    {
        HomeScreenManager manager = MakeManager();
        Guid userId = Guid.NewGuid();
        PluginConfiguration config = HomeScreenSectionsPlugin.Instance.Configuration;
        SectionSettings[] original = config.SectionSettings;
        config.SectionSettings =
        [
            new SectionSettings { SectionId = "LockedEnabled", Enabled = true, AllowUserOverride = false },
            new SectionSettings { SectionId = "LockedDisabled", Enabled = false, AllowUserOverride = false }
        ];
        try
        {
            // User tries to disable the locked-enabled section and enable the locked-disabled one.
            manager.UpdateUserSettings(userId, new ModularHomeUserSettings
            {
                UserId = userId,
                EnabledSections = ["LockedDisabled"]
            });

            ModularHomeUserSettings? loaded = manager.GetUserSettings(userId);

            Assert.NotNull(loaded);
            Assert.Contains("LockedEnabled", loaded!.EnabledSections, StringComparer.Ordinal);
            Assert.DoesNotContain("LockedDisabled", loaded.EnabledSections, StringComparer.Ordinal);
        }
        finally
        {
            config.SectionSettings = original;
        }
    }

    [Fact]
    public void GetUserSettings_refills_empty_enabled_sections_with_defaults()
    {
        HomeScreenManager manager = MakeManager();
        Guid userId = Guid.NewGuid();
        PluginConfiguration config = HomeScreenSectionsPlugin.Instance.Configuration;
        SectionSettings[] original = config.SectionSettings;
        config.SectionSettings =
        [
            new SectionSettings { SectionId = "DefaultOn", Enabled = true, AllowUserOverride = true }
        ];
        try
        {
            manager.UpdateUserSettings(userId, new ModularHomeUserSettings { UserId = userId });

            ModularHomeUserSettings? loaded = manager.GetUserSettings(userId);

            Assert.NotNull(loaded);
            Assert.Contains("DefaultOn", loaded!.EnabledSections, StringComparer.Ordinal);
        }
        finally
        {
            config.SectionSettings = original;
        }
    }
}
