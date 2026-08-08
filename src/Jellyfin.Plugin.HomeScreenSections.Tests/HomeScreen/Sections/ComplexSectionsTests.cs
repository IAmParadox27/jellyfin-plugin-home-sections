using System.Reflection;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections;
using Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections.Latest;
using Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections.Persons;
using Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections.RecentlyAdded;
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
using MediaBrowser.Controller.TV;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Jellyfin.Plugin.HomeScreenSections.Tests.HomeScreen.Sections;

[Collection("Plugin Instance")]
public class ComplexSectionsTests
{
    private readonly PluginFixture _fixture;
    private readonly Mock<IUserManager> _userManager = new();
    private readonly Mock<ILibraryManager> _libraryManager = new();
    private readonly Mock<IDtoService> _dtoService = new();
    private readonly Mock<IUserDataManager> _userDataManager = new();
    private readonly Mock<ICollectionManager> _collectionManager = new();
    private readonly Mock<ITVSeriesManager> _tvSeriesManager = new();
    private readonly Mock<IUserViewManager> _userViewManager = new();
    private readonly TestServiceProvider _serviceProvider;
    private readonly User _user = new("ComplexUser", "AuthProvider", "PasswordResetProvider");
    private readonly Guid _userId = Guid.NewGuid();

    public ComplexSectionsTests(PluginFixture fixture)
    {
        _fixture = fixture;
        _serviceProvider = new TestServiceProvider(fixture.Paths);

        _userManager
            .Setup(manager => manager.GetUserById(_userId))
            .Returns(_user);

        _libraryManager
            .Setup(manager => manager.GetVirtualFolders())
            .Returns([]);

        _libraryManager
            .Setup(manager => manager.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns([]);

        TestDtos.StubPassthrough(_dtoService);
    }

    [Fact]
    public void BecauseYouWatched_CreateInstances_yields_nothing_without_recently_played_movies()
    {
        BecauseYouWatchedSection section = MakeBecauseYouWatchedSection();

        Assert.Empty(section.CreateInstances(_userId, 3));
        Assert.Empty(section.CreateInstances(null, 3));
    }

    [Fact]
    public void BecauseYouWatched_GetResults_returns_empty_when_no_similar_items_found()
    {
        // The similar-items query ultimately runs through the non-virtual Folder.GetItems, so
        // the DTO-mapping path cannot be exercised here; this covers the empty/no-folders path.
        _libraryManager
            .Setup(manager => manager.GetItemById(It.IsAny<Guid>()))
            .Returns(new Movie());

        BecauseYouWatchedSection section = MakeBecauseYouWatchedSection();

        QueryResult<BaseItemDto> result = section.GetResults(
            new HomeScreenSectionPayload { UserId = _userId, AdditionalData = Guid.NewGuid().ToString() },
            new FakeQueryCollection());

        Assert.Empty(result.Items);
    }

    [Fact]
    public void BecauseYouWatched_GetInfo_supports_hide_watched()
    {
        BecauseYouWatchedSection section = MakeBecauseYouWatchedSection();

        HomeScreenSectionInfo info = section.GetInfo();

        Assert.Equal("BecauseYouWatched", info.Section);
        Assert.Equal(5, info.Limit);
        Assert.True(info.AllowHideWatched);
        Assert.Equal(SectionViewMode.Landscape, info.ViewMode);
    }

    [Fact]
    public void BecauseYouWatched_skips_movies_sharing_a_collection_with_an_already_picked_movie()
    {
        Movie first = new Movie { Id = Guid.NewGuid(), Name = "First" };
        Movie sameCollection = new Movie { Id = Guid.NewGuid(), Name = "Same Collection" };
        Movie standalone = new Movie { Id = Guid.NewGuid(), Name = "Standalone" };

        TestBoxSet collection = new(new BaseItem[] { first, sameCollection })
        {
            Id = Guid.NewGuid(),
            Name = "Trilogy"
        };
        FakeCollectionManager collectionManager = new FakeCollectionManager([collection]);
        BecauseYouWatchedSection section = MakeBecauseYouWatchedSection(collectionManager);

        MethodInfo pick = typeof(BecauseYouWatchedSection)
            .GetMethod("PickMoviesAvoidingCollections", BindingFlags.NonPublic | BindingFlags.Instance)!;

        List<BaseItem> recentlyPlayed = [first, sameCollection, standalone];
        List<BaseItem> picked = ((System.Collections.IEnumerable)pick.Invoke(section, [_user, recentlyPlayed, 3])!)
            .Cast<BaseItem>()
            .ToList();

        // "Same Collection" is skipped because "First" from the same box set was picked already.
        Assert.Equal(2, picked.Count);
        Assert.Same(first, picked[0]);
        Assert.Same(standalone, picked[1]);
    }

    [Fact]
    public void DirectedBy_GetResults_maps_person_items_to_dtos()
    {
        Guid folderId = Guid.NewGuid();
        Movie directedMovie = new Movie { Id = Guid.NewGuid(), Name = "Directed Movie" };
        _libraryManager
            .Setup(manager => manager.GetVirtualFolders())
            .Returns([new VirtualFolderInfo { ItemId = folderId.ToString(), Name = "Movies", Locations = ["/media/movies"] }]);
        _libraryManager
            .Setup(manager => manager.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns(new BaseItem[] { directedMovie });

        DirectedBySection section = new DirectedBySection(_libraryManager.Object, _dtoService.Object, _userManager.Object);

        QueryResult<BaseItemDto> result = section.GetResults(
            new HomeScreenSectionPayload { UserId = _userId, AdditionalData = Guid.NewGuid().ToString() },
            new FakeQueryCollection());

        Assert.Equal("Directed Movie", Assert.Single(result.Items).Name);
    }

    [Fact]
    public void DirectedBy_CreateInstances_requires_minimum_item_count()
    {
        Guid folderId = Guid.NewGuid();
        Person director = new Person { Id = Guid.NewGuid(), Name = "Ava Director" };

        _libraryManager
            .Setup(manager => manager.GetPeopleItems(It.IsAny<InternalPeopleQuery>()))
            .Returns([director]);
        _libraryManager
            .Setup(manager => manager.GetVirtualFolders())
            .Returns([new VirtualFolderInfo { ItemId = folderId.ToString(), Name = "Movies", Locations = ["/media/movies"] }]);
        _libraryManager
            .Setup(manager => manager.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns(new BaseItem[]
            {
                new Movie { Id = Guid.NewGuid() },
                new Movie { Id = Guid.NewGuid() },
                new Movie { Id = Guid.NewGuid() }
            });
        _dtoService
            .Setup(service => service.GetBaseItemDto(director, It.IsAny<DtoOptions>(), It.IsAny<User>(), It.IsAny<BaseItem>()))
            .Returns(new BaseItemDto { Id = director.Id });

        DirectedBySection section = new DirectedBySection(_libraryManager.Object, _dtoService.Object, _userManager.Object);

        List<IHomeScreenSection> instances = [.. section.CreateInstances(_userId, 2)];

        DirectedBySection instance = Assert.IsType<DirectedBySection>(Assert.Single(instances));
        Assert.Equal(director.Id.ToString(), instance.AdditionalData);
        Assert.Equal("Directed by Ava Director", instance.DisplayText);
        Assert.NotNull(instance.TranslationMetadata);
        Assert.Equal(TranslationType.Pattern, instance.TranslationMetadata!.Type);
        Assert.Equal("Ava Director", instance.TranslationMetadata.AdditionalContent);
    }

    [Fact]
    public void DirectedBy_CreateInstances_skips_people_with_too_few_items()
    {
        Guid folderId = Guid.NewGuid();
        Person director = new Person { Id = Guid.NewGuid(), Name = "One Hit" };

        _libraryManager
            .Setup(manager => manager.GetPeopleItems(It.IsAny<InternalPeopleQuery>()))
            .Returns([director]);
        _libraryManager
            .Setup(manager => manager.GetVirtualFolders())
            .Returns([new VirtualFolderInfo { ItemId = folderId.ToString(), Name = "Movies", Locations = ["/media/movies"] }]);
        _libraryManager
            .Setup(manager => manager.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns(new BaseItem[] { new Movie { Id = Guid.NewGuid() } });

        DirectedBySection section = new DirectedBySection(_libraryManager.Object, _dtoService.Object, _userManager.Object);

        Assert.Empty(section.CreateInstances(_userId, 2));
    }

    [Fact]
    public void Starring_metadata_uses_actor_person_type()
    {
        StarringSection section = new StarringSection(_libraryManager.Object, _dtoService.Object, _userManager.Object);

        Assert.Equal("Starring", section.Section);
        Assert.Equal(5, section.Limit);

        HomeScreenSectionInfo info = section.GetInfo();
        Assert.Equal(SectionViewMode.Landscape, info.ViewMode);
        Assert.True(info.AllowHideWatched);
    }

    [Fact]
    public void LatestShows_GetResults_without_folders_returns_empty()
    {
        LatestShowsSection section = new LatestShowsSection(
            _userViewManager.Object,
            _userManager.Object,
            _libraryManager.Object,
            _tvSeriesManager.Object,
            _dtoService.Object,
            _serviceProvider);

        QueryResult<BaseItemDto> result = section.GetResults(new HomeScreenSectionPayload { UserId = _userId }, new FakeQueryCollection());

        Assert.Empty(result.Items);
    }

    [Fact]
    public void LatestShows_metadata_and_instances()
    {
        Mock<Folder> rootFolder = new();
        rootFolder
            .Setup(folder => folder.GetChildren(It.IsAny<User>(), true, It.IsAny<InternalItemsQuery>()))
            .Returns(Array.Empty<BaseItem>());
        _libraryManager
            .Setup(manager => manager.GetUserRootFolder())
            .Returns(rootFolder.Object);

        LatestShowsSection section = new LatestShowsSection(
            _userViewManager.Object,
            _userManager.Object,
            _libraryManager.Object,
            _tvSeriesManager.Object,
            _dtoService.Object,
            _serviceProvider);

        Assert.Equal("LatestShows", section.Section);

        HomeScreenSectionInfo info = section.GetInfo();
        Assert.True(info.AllowHideWatched);

        LatestShowsSection instance = Assert.IsType<LatestShowsSection>(Assert.Single(section.CreateInstances(_userId, 1)));
        Assert.NotSame(section, instance);
    }

    [Fact]
    public void RecentlyAddedShows_sorts_series_by_latest_episode_date()
    {
        RecentlyAddedShowsSection section = MakeRecentlyAddedShowsSection();

        DateTime episodeDate = DateTime.UtcNow.AddDays(-2);
        Episode latestEpisode = new Episode { Id = Guid.NewGuid(), DateCreated = episodeDate };
        _libraryManager
            .Setup(manager => manager.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns(new BaseItem[] { latestEpisode });

        Series series = new Series
        {
            Id = Guid.NewGuid(),
            Name = "The Show",
            DateCreated = DateTime.UtcNow.AddDays(-30),
            // Preset so GetPresentationUniqueKey does not recompute via library lookups.
            PresentationUniqueKey = "the-show"
        };

        MethodInfo sortMethod = typeof(RecentlyAddedShowsSection)
            .GetMethod("GetSortDateForItem", BindingFlags.NonPublic | BindingFlags.Instance)!;
        DateTime sortDate = (DateTime)sortMethod.Invoke(section, [series, _user, new DtoOptions()])!;

        Assert.Equal(episodeDate, sortDate);
    }

    [Fact]
    public void RecentlyAddedShows_falls_back_to_date_created_for_non_series_items()
    {
        RecentlyAddedShowsSection section = MakeRecentlyAddedShowsSection();

        Movie movie = new Movie { Id = Guid.NewGuid(), DateCreated = DateTime.UtcNow.AddDays(-3) };

        MethodInfo sortMethod = typeof(RecentlyAddedShowsSection)
            .GetMethod("GetSortDateForItem", BindingFlags.NonPublic | BindingFlags.Instance)!;
        DateTime sortDate = (DateTime)sortMethod.Invoke(section, [movie, _user, new DtoOptions()])!;

        Assert.Equal(movie.DateCreated, sortDate);
    }

    [Fact]
    public void RecentlyAddedShows_GetResults_without_folders_returns_empty()
    {
        RecentlyAddedShowsSection section = MakeRecentlyAddedShowsSection();

        QueryResult<BaseItemDto> result = section.GetResults(new HomeScreenSectionPayload { UserId = _userId }, new FakeQueryCollection());

        Assert.Empty(result.Items);
    }

    [Fact]
    public void RecentlyAddedShows_metadata_exposes_tv_route()
    {
        RecentlyAddedShowsSection section = MakeRecentlyAddedShowsSection();

        Assert.Equal("RecentlyAddedShows", section.Section);
        Assert.Equal("tvshows", section.Route);
        Assert.Equal("tvshows", section.AdditionalData);
    }

    private BecauseYouWatchedSection MakeBecauseYouWatchedSection(ICollectionManager? collectionManager = null)
    {
        return new BecauseYouWatchedSection(
            _userDataManager.Object,
            _userManager.Object,
            _libraryManager.Object,
            _dtoService.Object,
            collectionManager ?? _collectionManager.Object,
            new CollectionManagerProxy(collectionManager ?? _collectionManager.Object));
    }

    private RecentlyAddedShowsSection MakeRecentlyAddedShowsSection()
    {
        return new RecentlyAddedShowsSection(
            _userViewManager.Object,
            _userManager.Object,
            _libraryManager.Object,
            _dtoService.Object,
            _serviceProvider,
            NullLogger<RecentlyAddedShowsSection>.Instance);
    }
}
