using System.Reflection;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections;
using Jellyfin.Plugin.HomeScreenSections.JellyfinVersionSpecific;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using Jellyfin.Plugin.HomeScreenSections.Tests.Support;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Controller.Session;
using MediaBrowser.Controller.TV;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Moq;

namespace Jellyfin.Plugin.HomeScreenSections.Tests.HomeScreen.Sections;

[Collection("Plugin Instance")]
public class StandaloneSectionsTests
{
    private readonly Mock<IUserManager> _userManager = new();
    private readonly Mock<ILibraryManager> _libraryManager = new();
    private readonly Mock<IDtoService> _dtoService = new();
    private readonly Mock<IUserViewManager> _userViewManager = new();
    private readonly Mock<ISessionManager> _sessionManager = new();
    private readonly Mock<ITVSeriesManager> _tvSeriesManager = new();
    private readonly Mock<IUserDataManager> _userDataManager = new();
    private readonly Mock<ICollectionManager> _collectionManager = new();
    private readonly Mock<IPlaylistManager> _playlistManager = new();
    private readonly Mock<ILiveTvManager> _liveTvManager = new();
    private readonly User _user = new("StandaloneUser", "AuthProvider", "PasswordResetProvider");
    private readonly Guid _userId = Guid.NewGuid();

    public StandaloneSectionsTests(PluginFixture fixture)
    {
        _ = fixture;

        _userManager
            .Setup(manager => manager.GetUserById(_userId))
            .Returns(_user);

        TestDtos.StubPassthrough(_dtoService);
    }

    [Fact]
    public void MyMedia_maps_user_views_to_dtos()
    {
        Folder viewFolder = new Folder { Id = Guid.NewGuid(), Name = "Movies" };
        _userViewManager
            .Setup(manager => manager.GetUserViews(It.IsAny<MediaBrowser.Model.Library.UserViewQuery>()))
            .Returns([viewFolder]);
        _dtoService
            .Setup(service => service.GetBaseItemDto(viewFolder, It.IsAny<DtoOptions>(), _user, It.IsAny<BaseItem>()))
            .Returns(new BaseItemDto { Id = viewFolder.Id, Name = "Movies" });

        MyMediaSection section = new MyMediaSection(_userViewManager.Object, _userManager.Object, _dtoService.Object);

        QueryResult<BaseItemDto> result = section.GetResults(new HomeScreenSectionPayload { UserId = _userId }, new FakeQueryCollection());

        Assert.Equal("Movies", Assert.Single(result.Items).Name);
    }

    [Fact]
    public void MyMedia_returns_empty_when_user_missing()
    {
        _userManager.Setup(manager => manager.GetUserById(_userId)).Returns((User?)null);
        MyMediaSection section = new MyMediaSection(_userViewManager.Object, _userManager.Object, _dtoService.Object);

        Assert.Empty(section.GetResults(new HomeScreenSectionPayload { UserId = _userId }, new FakeQueryCollection()).Items);
    }

    [Fact]
    public void MyMedia_GetInfo_allows_view_mode_change()
    {
        MyMediaSection section = new MyMediaSection(_userViewManager.Object, _userManager.Object, _dtoService.Object);

        HomeScreenSectionInfo info = section.GetInfo();

        Assert.Equal("MyMedia", info.Section);
        Assert.True(info.AllowViewModeChange);
        Assert.Equal(SectionViewMode.Landscape, info.ViewMode);
    }

    [Fact]
    public void ContinueWatching_queries_resumable_video_items()
    {
        InternalItemsQuery? captured = null;
        _libraryManager
            .Setup(manager => manager.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(query => captured = query)
            .Returns(new QueryResult<BaseItem>([new Movie { Id = Guid.NewGuid(), Name = "Half-Watched" }]));

        ContinueWatchingSection section = new ContinueWatchingSection(
            _userViewManager.Object,
            _userManager.Object,
            _dtoService.Object,
            _libraryManager.Object,
            _sessionManager.Object);

        QueryResult<BaseItemDto> result = section.GetResults(new HomeScreenSectionPayload { UserId = _userId }, new FakeQueryCollection());

        Assert.Single(result.Items);
        Assert.NotNull(captured);
        Assert.True(captured!.IsResumable);
        Assert.Equal(12, captured.Limit);
    }

    [Fact]
    public void ContinueWatching_GetInfo_disables_view_mode_change()
    {
        ContinueWatchingSection section = new ContinueWatchingSection(
            _userViewManager.Object,
            _userManager.Object,
            _dtoService.Object,
            _libraryManager.Object,
            _sessionManager.Object);

        HomeScreenSectionInfo info = section.GetInfo();

        Assert.Equal("ContinueWatching", info.Section);
        Assert.Equal("list", info.Route);
        Assert.False(info.AllowViewModeChange);
    }

    [Fact]
    public void MyList_returns_children_of_the_my_list_playlist()
    {
        TestPlaylist myList = new(new BaseItem[] { new Movie { Id = Guid.NewGuid(), Name = "Saved Movie" } })
        {
            Id = Guid.NewGuid(),
            Name = "My List"
        };
        TestPlaylist other = new(new BaseItem[] { new Movie { Id = Guid.NewGuid() } })
        {
            Id = Guid.NewGuid(),
            Name = "Something Else"
        };
        _playlistManager
            .Setup(manager => manager.GetPlaylists(_user.Id))
            .Returns(new[] { other, myList });

        MyListSection section = new MyListSection(_userManager.Object, _dtoService.Object, _playlistManager.Object);

        QueryResult<BaseItemDto> result = section.GetResults(new HomeScreenSectionPayload { UserId = _userId }, new FakeQueryCollection());

        Assert.Equal("Saved Movie", Assert.Single(result.Items).Name);
    }

    [Fact]
    public void MyList_returns_empty_without_my_list_playlist()
    {
        _playlistManager
            .Setup(manager => manager.GetPlaylists(_user.Id))
            .Returns(Array.Empty<Playlist>());

        MyListSection section = new MyListSection(_userManager.Object, _dtoService.Object, _playlistManager.Object);

        Assert.Empty(section.GetResults(new HomeScreenSectionPayload { UserId = _userId }, new FakeQueryCollection()).Items);
    }

    [Fact]
    public void MyList_GetInfo_links_to_favorites_route()
    {
        MyListSection section = new MyListSection(_userManager.Object, _dtoService.Object, _playlistManager.Object);

        HomeScreenSectionInfo info = section.GetInfo();

        Assert.Equal("MyList", info.Section);
        Assert.Equal("favorites", info.Route);
        Assert.Same(section, Assert.Single(section.CreateInstances(_userId, 1)));
    }

    [Fact]
    public void LiveTv_returns_recommended_airing_programs()
    {
        QueryResult<BaseItemDto> expected = new QueryResult<BaseItemDto>([new BaseItemDto { Name = "Live Now" }]);
        _liveTvManager
            .Setup(manager => manager.GetRecommendedProgramsAsync(It.IsAny<InternalItemsQuery>(), It.IsAny<DtoOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        LiveTvSection section = new LiveTvSection(_userManager.Object, _dtoService.Object, _liveTvManager.Object);

        QueryResult<BaseItemDto> result = section.GetResults(new HomeScreenSectionPayload { UserId = _userId }, new FakeQueryCollection());

        Assert.Equal("Live Now", Assert.Single(result.Items).Name);
    }

    [Fact]
    public void LiveTv_GetInfo_reports_livetv_route()
    {
        LiveTvSection section = new LiveTvSection(_userManager.Object, _dtoService.Object, _liveTvManager.Object);

        HomeScreenSectionInfo info = section.GetInfo();

        Assert.Equal("LiveTV", info.Section);
        Assert.Equal("livetv", info.Route);
        Assert.Equal(SectionViewMode.Landscape, info.ViewMode);
    }

    [Fact]
    public void TopTen_GetResults_filters_collection_children_by_type()
    {
        Movie movie = new Movie { Id = Guid.NewGuid(), Name = "Top Movie" };
        Series show = new Series { Id = Guid.NewGuid(), Name = "Top Show" };
        TestBoxSet topTen = new(new BaseItem[] { movie, show })
        {
            Id = Guid.NewGuid(),
            Name = "Top Ten"
        };
        FakeCollectionManager collectionManager = new FakeCollectionManager([topTen]);

        TopTenSection section = new TopTenSection(_userManager.Object, collectionManager, _dtoService.Object);

        QueryResult<BaseItemDto> movies = section.GetResults(
            new HomeScreenSectionPayload { UserId = _userId, AdditionalData = "Movies" },
            new FakeQueryCollection());
        Assert.Equal("Top Movie", Assert.Single(movies.Items).Name);

        QueryResult<BaseItemDto> shows = section.GetResults(
            new HomeScreenSectionPayload { UserId = _userId, AdditionalData = "Shows" },
            new FakeQueryCollection());
        Assert.Equal("Top Show", Assert.Single(shows.Items).Name);
    }

    [Fact]
    public void TopTen_GetResults_without_collection_returns_empty()
    {
        FakeCollectionManager collectionManager = new FakeCollectionManager(Array.Empty<BoxSet>());
        TopTenSection section = new TopTenSection(_userManager.Object, collectionManager, _dtoService.Object);

        Assert.Empty(section.GetResults(new HomeScreenSectionPayload { UserId = _userId }, new FakeQueryCollection()).Items);
    }

    [Fact]
    public void TopTen_GetResults_throws_for_unknown_type()
    {
        FakeCollectionManager collectionManager = new FakeCollectionManager(Array.Empty<BoxSet>());
        TopTenSection section = new TopTenSection(_userManager.Object, collectionManager, _dtoService.Object);

        Assert.Throws<ArgumentException>(() => section.GetResults(
            new HomeScreenSectionPayload { UserId = _userId, AdditionalData = "Documentaries" },
            new FakeQueryCollection()));
    }

    [Fact]
    public void TopTen_CreateInstances_creates_movies_and_shows_rows()
    {
        FakeCollectionManager collectionManager = new FakeCollectionManager(Array.Empty<BoxSet>());
        TopTenSection section = new TopTenSection(_userManager.Object, collectionManager, _dtoService.Object)
        {
            DisplayText = "Top Ten"
        };

        List<IHomeScreenSection> instances = [.. section.CreateInstances(_userId, 5)];

        Assert.Equal(2, instances.Count);
        Assert.Contains(instances, i => string.Equals(i.AdditionalData, "Movies", StringComparison.Ordinal));
        Assert.Contains(instances, i => string.Equals(i.AdditionalData, "Shows", StringComparison.Ordinal));
    }

    [Fact]
    public void TopTen_CreateInstances_respects_instance_count()
    {
        FakeCollectionManager collectionManager = new FakeCollectionManager(Array.Empty<BoxSet>());
        TopTenSection section = new TopTenSection(_userManager.Object, collectionManager, _dtoService.Object);

        Assert.Single(section.CreateInstances(_userId, 1));
    }

    [Fact]
    public void TopTen_GetInfo_is_portrait_without_title_text()
    {
        FakeCollectionManager collectionManager = new FakeCollectionManager(Array.Empty<BoxSet>());
        TopTenSection section = new TopTenSection(_userManager.Object, collectionManager, _dtoService.Object);

        HomeScreenSectionInfo info = section.GetInfo();

        Assert.Equal("TopTen", info.Section);
        Assert.Equal(SectionViewMode.Portrait, info.ViewMode);
        Assert.False(info.DisplayTitleText);
        Assert.False(info.ShowDetailsMenu);
        Assert.False(info.AllowViewModeChange);
        Assert.Equal("top-ten", info.ContainerClass);
    }

    [Fact]
    public void WatchAgain_GetResults_without_libraries_returns_empty()
    {
        _libraryManager
            .Setup(manager => manager.GetVirtualFolders())
            .Returns([]);
        _libraryManager
            .Setup(manager => manager.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns([]);

        WatchAgainSection section = MakeWatchAgainSection();

        QueryResult<BaseItemDto> result = section.GetResults(new HomeScreenSectionPayload { UserId = _userId }, new FakeQueryCollection());

        Assert.Empty(result.Items);
    }

    [Fact]
    public void WatchAgain_metadata_and_info_are_stable()
    {
        WatchAgainSection section = MakeWatchAgainSection();

        Assert.Equal("WatchAgain", section.Section);
        Assert.Equal(1, section.Limit);
        Assert.Same(section, Assert.Single(section.CreateInstances(_userId, 1)));

        HomeScreenSectionInfo info = section.GetInfo();
        Assert.Equal(SectionViewMode.Landscape, info.ViewMode);
        Assert.Equal(1, info.Limit);
    }

    [Fact]
    public void WatchAgain_boxset_candidates_require_multiple_fully_played_old_movies()
    {
        WatchAgainSection section = MakeWatchAgainSection();
        MethodInfo tryAdd = typeof(WatchAgainSection)
            .GetMethod("TryAddBoxSetCandidate", BindingFlags.NonPublic | BindingFlags.Instance)!;

        DateTime oldDate = DateTime.Now.Subtract(TimeSpan.FromDays(60));
        DateTime recentDate = DateTime.Now.Subtract(TimeSpan.FromDays(2));
        Movie first = new Movie { Id = Guid.NewGuid() };
        Movie second = new Movie { Id = Guid.NewGuid() };
        Movie third = new Movie { Id = Guid.NewGuid() };

        List<(BaseItem Item, DateTime? LastPlayed)> results = [];

        // Fully played and old enough -> candidate.
        SetupPlayed(first, oldDate);
        SetupPlayed(second, oldDate);
        tryAdd.Invoke(section, [_user, new TestBoxSet(new BaseItem[] { first, second }), DateTime.Now.Subtract(TimeSpan.FromDays(28)), results]);
        Assert.Single(results);

        // Too recently played -> rejected.
        SetupPlayed(third, recentDate);
        tryAdd.Invoke(section, [_user, new TestBoxSet(new BaseItem[] { second, third }), DateTime.Now.Subtract(TimeSpan.FromDays(28)), results]);
        Assert.Single(results);

        // Not all movies played -> rejected.
        _userDataManager
            .Setup(manager => manager.GetUserData(_user, third))
            .Returns((UserItemData?)null);
        tryAdd.Invoke(section, [_user, new TestBoxSet(new BaseItem[] { second, third }), DateTime.Now.Subtract(TimeSpan.FromDays(28)), results]);
        Assert.Single(results);

        // Single-movie box sets are skipped.
        tryAdd.Invoke(section, [_user, new TestBoxSet(new BaseItem[] { first }), DateTime.Now.Subtract(TimeSpan.FromDays(28)), results]);
        Assert.Single(results);
    }

    private void SetupPlayed(Movie movie, DateTime lastPlayed)
    {
        _userDataManager
            .Setup(manager => manager.GetUserData(_user, movie))
            .Returns(new UserItemData { Key = movie.Id.ToString("N"), Played = true, LastPlayedDate = lastPlayed });
    }

    private WatchAgainSection MakeWatchAgainSection()
    {
        return new WatchAgainSection(
            _collectionManager.Object,
            _userManager.Object,
            _dtoService.Object,
            _userDataManager.Object,
            _tvSeriesManager.Object,
            _libraryManager.Object,
            new CollectionManagerProxy(_collectionManager.Object),
            _userViewManager.Object);
    }
}
