using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections.Extra;
using Jellyfin.Plugin.HomeScreenSections.JellyfinVersionSpecific;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using Jellyfin.Plugin.HomeScreenSections.Tests.Support;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Moq;

namespace Jellyfin.Plugin.HomeScreenSections.Tests.HomeScreen.Sections.Extra;

[Collection("Plugin Instance")]
public class MultiInstanceSectionsTests
{
    private readonly Mock<IUserManager> m_userManager = new();
    private readonly Mock<ILibraryManager> m_libraryManager = new();
    private readonly Mock<IDtoService> m_dtoService = new();
    private readonly Mock<IUserDataManager> m_userDataManager = new();
    private readonly Mock<IPlaylistManager> m_playlistManager = new();
    private readonly Mock<ICollectionManager> m_collectionManager = new();
    private readonly User m_user = new("MultiUser", "AuthProvider", "PasswordResetProvider");
    private readonly Guid m_userId = Guid.NewGuid();

    public MultiInstanceSectionsTests(PluginFixture fixture)
    {
        _ = fixture;

        m_userManager
            .Setup(manager => manager.GetUserById(m_userId))
            .Returns(m_user);

        TestDtos.StubPassthrough(m_dtoService);
    }

    [Fact]
    public void Decade_GetResults_requires_valid_year_additional_data()
    {
        DecadeSection section = new DecadeSection(m_userManager.Object, m_libraryManager.Object, m_dtoService.Object);

        Assert.Empty(section.GetResults(new HomeScreenSectionPayload { UserId = m_userId, AdditionalData = null }, new FakeQueryCollection()).Items);
        Assert.Empty(section.GetResults(new HomeScreenSectionPayload { UserId = m_userId, AdditionalData = "not-a-year" }, new FakeQueryCollection()).Items);
    }

    [Fact]
    public void Decade_GetResults_queries_the_ten_years_of_the_decade()
    {
        InternalItemsQuery? captured = null;
        m_libraryManager
            .Setup(manager => manager.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(query => captured = query)
            .Returns(new QueryResult<BaseItem>([new Movie { Id = Guid.NewGuid(), Name = "Nineties Movie" }]));

        DecadeSection section = new DecadeSection(m_userManager.Object, m_libraryManager.Object, m_dtoService.Object);

        QueryResult<BaseItemDto> result = section.GetResults(
            new HomeScreenSectionPayload { UserId = m_userId, AdditionalData = "1990" },
            new FakeQueryCollection());

        Assert.Single(result.Items);
        Assert.NotNull(captured);
        Assert.Equal(Enumerable.Range(1990, 10), captured!.Years);
    }

    [Fact]
    public void Decade_CreateInstances_picks_decades_present_in_the_sample()
    {
        m_libraryManager
            .Setup(manager => manager.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Returns(new QueryResult<BaseItem>(
            [
                new Movie { Id = Guid.NewGuid(), ProductionYear = 1995 },
                new Movie { Id = Guid.NewGuid(), ProductionYear = 2003 },
                new Movie { Id = Guid.NewGuid(), ProductionYear = 1850 }, // outside 1900-2100, ignored
                new Movie { Id = Guid.NewGuid() } // no year, ignored
            ]));

        DecadeSection section = new DecadeSection(m_userManager.Object, m_libraryManager.Object, m_dtoService.Object);

        List<IHomeScreenSection> instances = [.. section.CreateInstances(m_userId, 5)];

        Assert.Equal(2, instances.Count);
        string?[] additionalData = instances.Select(instance => instance.AdditionalData).ToArray();
        Assert.Contains("2000", additionalData, StringComparer.Ordinal);
        Assert.Contains("1990", additionalData, StringComparer.Ordinal);
    }

    [Fact]
    public void Decade_CreateInstances_yields_nothing_without_user_or_movies()
    {
        m_libraryManager
            .Setup(manager => manager.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Returns(new QueryResult<BaseItem>(Array.Empty<BaseItem>()));

        DecadeSection section = new DecadeSection(m_userManager.Object, m_libraryManager.Object, m_dtoService.Object);

        Assert.Empty(section.CreateInstances(m_userId, 3));

        m_userManager.Setup(manager => manager.GetUserById(m_userId)).Returns((User?)null);
        Assert.Empty(section.CreateInstances(m_userId, 3));
    }

    [Fact]
    public void Studio_GetResults_filters_sample_by_studio_name()
    {
        Movie matched = new Movie { Id = Guid.NewGuid(), Name = "Matched", Studios = ["A24", "Other"] };
        Movie unmatched = new Movie { Id = Guid.NewGuid(), Name = "Unmatched", Studios = ["Blumhouse"] };
        Movie studioless = new Movie { Id = Guid.NewGuid(), Name = "Studioless" };
        m_libraryManager
            .Setup(manager => manager.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Returns(new QueryResult<BaseItem>([matched, unmatched, studioless]));

        StudioSection section = new StudioSection(m_userManager.Object, m_libraryManager.Object, m_dtoService.Object, m_userDataManager.Object);

        QueryResult<BaseItemDto> result = section.GetResults(
            new HomeScreenSectionPayload { UserId = m_userId, AdditionalData = "A24" },
            new FakeQueryCollection());

        BaseItemDto dto = Assert.Single(result.Items);
        Assert.Equal("Matched", dto.Name);
    }

    [Fact]
    public void Studio_GetResults_requires_additional_data()
    {
        StudioSection section = new StudioSection(m_userManager.Object, m_libraryManager.Object, m_dtoService.Object, m_userDataManager.Object);

        Assert.Empty(section.GetResults(new HomeScreenSectionPayload { UserId = m_userId }, new FakeQueryCollection()).Items);
    }

    [Fact]
    public void Studio_CreateInstances_orders_studios_by_weighted_play_count()
    {
        Movie heavyStudio = new Movie { Id = Guid.NewGuid(), Studios = ["Heavy"] };
        Movie lightStudio = new Movie { Id = Guid.NewGuid(), Studios = ["Light"] };
        m_libraryManager
            .Setup(manager => manager.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Returns(new QueryResult<BaseItem>([heavyStudio, lightStudio]));

        m_userDataManager
            .Setup(manager => manager.GetUserData(m_user, heavyStudio))
            .Returns(new UserItemData { Key = "heavy", PlayCount = 10 });
        m_userDataManager
            .Setup(manager => manager.GetUserData(m_user, lightStudio))
            .Returns(new UserItemData { Key = "light", PlayCount = 1 });

        StudioSection section = new StudioSection(m_userManager.Object, m_libraryManager.Object, m_dtoService.Object, m_userDataManager.Object);

        List<IHomeScreenSection> instances = [.. section.CreateInstances(m_userId, 2)];

        // Order is randomized; both studios must appear as their own instances.
        string?[] studioNames = instances.Select(instance => instance.AdditionalData).ToArray();
        Assert.Equal(2, studioNames.Length);
        Assert.Contains("Heavy", studioNames, StringComparer.Ordinal);
        Assert.Contains("Light", studioNames, StringComparer.Ordinal);
        Assert.All(instances, instance => Assert.Equal(instance.AdditionalData, instance.DisplayText));
    }

    [Fact]
    public void Playlists_GetResults_requires_playlist_additional_data()
    {
        PlaylistsSection section = MakePlaylistsSection();

        Assert.Empty(section.GetResults(new HomeScreenSectionPayload { UserId = m_userId }, new FakeQueryCollection()).Items);
        Assert.Empty(section.GetResults(new HomeScreenSectionPayload { UserId = m_userId, AdditionalData = "nope" }, new FakeQueryCollection()).Items);
    }

    [Fact]
    public void Playlists_GetResults_returns_playlist_children()
    {
        Guid playlistId = Guid.NewGuid();
        TestPlaylist playlist = new([new Movie { Id = Guid.NewGuid(), Name = "Track" }])
        {
            Id = playlistId
        };
        m_libraryManager
            .Setup(manager => manager.GetItemById(playlistId))
            .Returns(playlist);

        PlaylistsSection section = MakePlaylistsSection();

        QueryResult<BaseItemDto> result = section.GetResults(
            new HomeScreenSectionPayload { UserId = m_userId, AdditionalData = playlistId.ToString() },
            new FakeQueryCollection());

        Assert.Single(result.Items);
    }

    [Fact]
    public void Playlists_GetResults_returns_empty_for_non_playlist_item()
    {
        Guid otherId = Guid.NewGuid();
        m_libraryManager
            .Setup(manager => manager.GetItemById(otherId))
            .Returns(new Movie());

        PlaylistsSection section = MakePlaylistsSection();

        Assert.Empty(section.GetResults(
            new HomeScreenSectionPayload { UserId = m_userId, AdditionalData = otherId.ToString() },
            new FakeQueryCollection()).Items);
    }

    [Fact]
    public void Playlists_CreateInstances_skips_my_list_and_empty_playlists()
    {
        Guid keepId = Guid.NewGuid();

        TestPlaylist keep = new([new Movie { Id = Guid.NewGuid() }])
        {
            Id = keepId,
            Name = "Road Trip Mix"
        };
        TestPlaylist myList = new([new Movie { Id = Guid.NewGuid() }])
        {
            Id = Guid.NewGuid(),
            Name = "My List"
        };
        TestPlaylist empty = new(Array.Empty<BaseItem>())
        {
            Id = Guid.NewGuid(),
            Name = "Empty"
        };

        m_playlistManager
            .Setup(manager => manager.GetPlaylists(m_user.Id))
            .Returns(new[] { keep, myList, empty });

        m_dtoService
            .Setup(service => service.GetBaseItemDto(keep, It.IsAny<DtoOptions>(), m_user, It.IsAny<BaseItem>()))
            .Returns(new BaseItemDto { Id = keepId, Name = "Road Trip Mix" });

        PlaylistsSection section = MakePlaylistsSection();

        List<IHomeScreenSection> instances = [.. section.CreateInstances(m_userId, 5)];

        PlaylistsSection instance = Assert.IsType<PlaylistsSection>(Assert.Single(instances));
        Assert.Equal(keepId.ToString("N"), instance.AdditionalData);
        Assert.Equal("Road Trip Mix", instance.DisplayText);
        Assert.NotNull(instance.OriginalPayload);
    }

    [Fact]
    public void UnwatchedCollections_GetResults_returns_only_unwatched_children()
    {
        Guid collectionId = Guid.NewGuid();
        Movie watched = new Movie { Id = Guid.NewGuid(), Name = "Watched" };
        Movie unwatched = new Movie { Id = Guid.NewGuid(), Name = "Unwatched" };

        TestBoxSet boxSet = new(new BaseItem[] { watched, unwatched })
        {
            Id = collectionId
        };
        m_libraryManager
            .Setup(manager => manager.GetItemById(collectionId))
            .Returns(boxSet);

        m_userDataManager
            .Setup(manager => manager.GetUserData(m_user, watched))
            .Returns(new UserItemData { Key = "w", Played = true });
        m_userDataManager
            .Setup(manager => manager.GetUserData(m_user, unwatched))
            .Returns((UserItemData?)null);

        UnwatchedCollectionsSection section = MakeUnwatchedCollectionsSection();

        QueryResult<BaseItemDto> result = section.GetResults(
            new HomeScreenSectionPayload { UserId = m_userId, AdditionalData = collectionId.ToString() },
            new FakeQueryCollection());

        BaseItemDto dto = Assert.Single(result.Items);
        Assert.Equal("Unwatched", dto.Name);
    }

    [Fact]
    public void UnwatchedCollections_GetResults_validates_additional_data()
    {
        UnwatchedCollectionsSection section = MakeUnwatchedCollectionsSection();

        Assert.Empty(section.GetResults(new HomeScreenSectionPayload { UserId = m_userId }, new FakeQueryCollection()).Items);
        Assert.Empty(section.GetResults(new HomeScreenSectionPayload { UserId = m_userId, AdditionalData = "bad" }, new FakeQueryCollection()).Items);
    }

    [Fact]
    public void UnwatchedCollections_CreateInstances_picks_partially_watched_collections()
    {
        Guid partialId = Guid.NewGuid();

        Movie watchedChild = new Movie { Id = Guid.NewGuid() };
        Movie watchedChild2 = new Movie { Id = Guid.NewGuid() };
        Movie unwatchedChild = new Movie { Id = Guid.NewGuid() };
        Movie onlyChild = new Movie { Id = Guid.NewGuid() };

        // All children watched -> not "finish this collection" material.
        TestBoxSet fullyWatched = new(new BaseItem[] { watchedChild, watchedChild2 })
        {
            Id = Guid.NewGuid(),
            Name = "Done"
        };
        // One watched, one unwatched -> partial.
        TestBoxSet partial = new(new BaseItem[] { watchedChild, unwatchedChild })
        {
            Id = partialId,
            Name = "Saga"
        };
        // Too few children to count as a collection.
        TestBoxSet tooSmall = new(new BaseItem[] { onlyChild })
        {
            Id = Guid.NewGuid(),
            Name = "Tiny"
        };

        m_userDataManager
            .Setup(manager => manager.GetUserData(m_user, watchedChild))
            .Returns(new UserItemData { Key = "wc", Played = true });
        m_userDataManager
            .Setup(manager => manager.GetUserData(m_user, watchedChild2))
            .Returns(new UserItemData { Key = "wc2", Played = true });
        m_userDataManager
            .Setup(manager => manager.GetUserData(m_user, unwatchedChild))
            .Returns((UserItemData?)null);
        m_userDataManager
            .Setup(manager => manager.GetUserData(m_user, onlyChild))
            .Returns((UserItemData?)null);

        FakeCollectionManager collectionManager = new FakeCollectionManager([fullyWatched, partial, tooSmall]);
        UnwatchedCollectionsSection section = MakeUnwatchedCollectionsSection(collectionManager);

        m_dtoService
            .Setup(service => service.GetBaseItemDto(partial, It.IsAny<DtoOptions>(), m_user, It.IsAny<BaseItem>()))
            .Returns(new BaseItemDto { Id = partialId });

        List<IHomeScreenSection> instances = [.. section.CreateInstances(m_userId, 3)];

        UnwatchedCollectionsSection instance = Assert.IsType<UnwatchedCollectionsSection>(Assert.Single(instances));
        Assert.Equal(partialId.ToString("N"), instance.AdditionalData);
        Assert.Equal("Continue: Saga", instance.DisplayText);
    }

    [Fact]
    public void UnwatchedCollections_CreateInstances_requires_user()
    {
        m_userManager.Setup(manager => manager.GetUserById(m_userId)).Returns((User?)null);
        UnwatchedCollectionsSection section = MakeUnwatchedCollectionsSection();

        Assert.Empty(section.CreateInstances(m_userId, 2));
        Assert.Empty(section.CreateInstances(null, 2));
    }

    private PlaylistsSection MakePlaylistsSection()
    {
        return new PlaylistsSection(
            m_userManager.Object,
            m_dtoService.Object,
            m_playlistManager.Object,
            m_libraryManager.Object);
    }

    private UnwatchedCollectionsSection MakeUnwatchedCollectionsSection(ICollectionManager? collectionManager = null)
    {
        return new UnwatchedCollectionsSection(
            m_userManager.Object,
            m_dtoService.Object,
            new CollectionManagerProxy(collectionManager ?? m_collectionManager.Object),
            m_libraryManager.Object,
            m_userDataManager.Object);
    }
}
