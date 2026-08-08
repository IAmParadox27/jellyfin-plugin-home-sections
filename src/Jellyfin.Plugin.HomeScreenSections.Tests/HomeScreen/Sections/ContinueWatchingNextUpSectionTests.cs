using System.Reflection;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using Jellyfin.Plugin.HomeScreenSections.Tests.Support;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Controller.TV;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Moq;

namespace Jellyfin.Plugin.HomeScreenSections.Tests.HomeScreen.Sections;

[Collection("Plugin Instance")]
public class ContinueWatchingNextUpSectionTests
{
    private static readonly Guid s_userId = Guid.NewGuid();

    private readonly Mock<IHomeScreenManager> _homeScreenManager = new();
    private readonly Mock<ILibraryManager> _libraryManager = new();
    private readonly Mock<IUserManager> _userManager = new();
    private readonly Mock<IUserDataManager> _userDataManager = new();
    private readonly Mock<ITVSeriesManager> _tvSeriesManager = new();
    private readonly Mock<IDtoService> _dtoService = new();
    private readonly User _user = new("ComboUser", "AuthProvider", "PasswordResetProvider");

    public ContinueWatchingNextUpSectionTests(PluginFixture fixture)
    {
        _ = fixture;
    }

    private ContinueWatchingNextUpSection MakeSection(params BaseItemDto[] nextUpDtos)
    {
        _userManager
            .Setup(manager => manager.GetUserById(s_userId))
            .Returns(_user);

        _tvSeriesManager
            .Setup(manager => manager.GetNextUp(It.IsAny<NextUpQuery>(), It.IsAny<DtoOptions>()))
            .Returns(new QueryResult<BaseItem>(Array.Empty<BaseItem>()));

        // NextUpSection pipes GetNextUp items through IDtoService; returning the DTOs we
        // control here lets the merge section see scripted Next Up results.
        _dtoService
            .Setup(service => service.GetBaseItemDtos(
                It.IsAny<IReadOnlyList<BaseItem>>(),
                It.IsAny<DtoOptions>(),
                It.IsAny<User>(),
                It.IsAny<BaseItem>()))
            .Returns(nextUpDtos);

        NextUpSection nextUp = new NextUpSection(
            new Mock<IUserViewManager>().Object,
            _userManager.Object,
            _dtoService.Object,
            _libraryManager.Object,
            new Mock<ISessionManager>().Object,
            _tvSeriesManager.Object);

        // Continue Watching resolves to null, covering the missing-section branch.
        _homeScreenManager
            .Setup(manager => manager.GetSection("ContinueWatching"))
            .Returns((IHomeScreenSection?)null);
        _homeScreenManager
            .Setup(manager => manager.GetSection("NextUp"))
            .Returns(nextUp);

        return new ContinueWatchingNextUpSection(
            _homeScreenManager.Object,
            _libraryManager.Object,
            _userManager.Object,
            _userDataManager.Object);
    }

    [Fact]
    public void GetResults_returns_next_up_items_when_continue_watching_absent()
    {
        ContinueWatchingNextUpSection section = MakeSection(
            new BaseItemDto { Id = Guid.NewGuid(), Name = "Episode A", Type = BaseItemKind.Episode, DateCreated = DateTime.UtcNow.AddHours(-1) },
            new BaseItemDto { Id = Guid.NewGuid(), Name = "Episode B", Type = BaseItemKind.Episode, DateCreated = DateTime.UtcNow.AddHours(-2) });

        QueryResult<BaseItemDto> result = section.GetResults(new HomeScreenSectionPayload { UserId = s_userId }, QueryWithUserId());

        Assert.Equal(2, result.Items.Count);
        Assert.Contains(result.Items, item => string.Equals(item.Name, "Episode A", StringComparison.Ordinal));
        Assert.Contains(result.Items, item => string.Equals(item.Name, "Episode B", StringComparison.Ordinal));
    }

    [Fact]
    public void GetResults_filters_watched_items_when_hide_watched_enabled()
    {
        PluginConfiguration config = HomeScreenSectionsPlugin.Instance.Configuration;
        SectionSettings[] original = config.SectionSettings;
        config.SectionSettings =
        [
            new SectionSettings { SectionId = "ContinueWatchingNextUp", HideWatchedItems = true }
        ];
        try
        {
            BaseItemDto watched = new BaseItemDto
            {
                Id = Guid.NewGuid(),
                Name = "Watched",
                Type = BaseItemKind.Episode,
                DateCreated = DateTime.UtcNow,
                UserData = new UserItemDataDto { Key = "watched", Played = true }
            };
            BaseItemDto unwatched = new BaseItemDto
            {
                Id = Guid.NewGuid(),
                Name = "Unwatched",
                Type = BaseItemKind.Episode,
                DateCreated = DateTime.UtcNow
            };
            ContinueWatchingNextUpSection section = MakeSection(watched, unwatched);

            QueryResult<BaseItemDto> result = section.GetResults(new HomeScreenSectionPayload { UserId = s_userId }, QueryWithUserId());

            BaseItemDto kept = Assert.Single(result.Items);
            Assert.Equal("Unwatched", kept.Name);
        }
        finally
        {
            config.SectionSettings = original;
        }
    }

    [Fact]
    public void GetResults_sorts_by_last_played_then_series_lookup_then_created()
    {
        Guid seriesId = Guid.NewGuid();
        DateTime recentPlay = DateTime.UtcNow.AddMinutes(-5);
        // More recent than Fresh's DateCreated so the series lookup outranks it.
        DateTime seriesPlay = DateTime.UtcNow.AddHours(-6);

        BaseItemDto playingNow = new BaseItemDto
        {
            Id = Guid.NewGuid(),
            Name = "Playing Now",
            Type = BaseItemKind.Episode,
            UserData = new UserItemDataDto { Key = "playing-now", LastPlayedDate = recentPlay }
        };
        BaseItemDto fromSeries = new BaseItemDto
        {
            Id = Guid.NewGuid(),
            Name = "From Series",
            Type = BaseItemKind.Episode,
            SeriesId = seriesId
        };
        BaseItemDto freshItem = new BaseItemDto
        {
            Id = Guid.NewGuid(),
            Name = "Fresh",
            Type = BaseItemKind.Episode,
            DateCreated = DateTime.UtcNow.AddDays(-1)
        };

        ContinueWatchingNextUpSection section = MakeSection(fromSeries, freshItem, playingNow);

        // Series lookup: one played episode in the series carrying an older LastPlayedDate.
        MediaBrowser.Controller.Entities.TV.Episode playedEpisode = new MediaBrowser.Controller.Entities.TV.Episode();
        _libraryManager
            .Setup(manager => manager.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns(new BaseItem[] { playedEpisode });
        _userDataManager
            .Setup(manager => manager.GetUserData(_user, playedEpisode))
            .Returns(new UserItemData { Key = "played-episode", LastPlayedDate = seriesPlay });

        QueryResult<BaseItemDto> result = section.GetResults(new HomeScreenSectionPayload { UserId = s_userId }, QueryWithUserId());

        Assert.Equal(3, result.Items.Count);
        Assert.Equal("Playing Now", result.Items[0].Name);
        Assert.Equal("From Series", result.Items[1].Name);
        Assert.Equal("Fresh", result.Items[2].Name);
    }

    [Fact]
    public void GetSortDate_prefers_item_last_played_date()
    {
        DateTime lastPlayed = DateTime.UtcNow.AddHours(-4);
        BaseItemDto item = new BaseItemDto
        {
            SeriesId = Guid.NewGuid(),
            DateCreated = DateTime.UtcNow.AddDays(-1),
            UserData = new UserItemDataDto { Key = "sort-case", LastPlayedDate = lastPlayed }
        };

        Assert.Equal(lastPlayed, InvokeGetSortDate(item, []));
    }

    [Fact]
    public void GetSortDate_uses_series_lookup_when_item_has_no_play_history()
    {
        Guid seriesId = Guid.NewGuid();
        DateTime seriesDate = DateTime.UtcNow.AddDays(-2);
        BaseItemDto item = new BaseItemDto
        {
            SeriesId = seriesId,
            DateCreated = DateTime.UtcNow.AddDays(-1)
        };
        Dictionary<Guid, DateTime> lookup = new Dictionary<Guid, DateTime>
        {
            [seriesId] = seriesDate
        };

        Assert.Equal(seriesDate, InvokeGetSortDate(item, lookup));
    }

    [Fact]
    public void GetSortDate_falls_back_to_date_created_or_min_value()
    {
        DateTime created = DateTime.UtcNow.AddDays(-7);
        BaseItemDto withCreated = new BaseItemDto { DateCreated = created };
        BaseItemDto bare = new BaseItemDto();

        Assert.Equal(created, InvokeGetSortDate(withCreated, []));
        Assert.Equal(DateTime.MinValue, InvokeGetSortDate(bare, []));
    }

    [Fact]
    public void Section_metadata_and_info_are_stable()
    {
        ContinueWatchingNextUpSection section = MakeSection();

        Assert.Equal("ContinueWatchingNextUp", section.Section);
        Assert.Equal(1, section.Limit);
        Assert.Equal("nextup", section.Route);
        Assert.Null(section.OriginalPayload);

        HomeScreenSectionInfo info = section.GetInfo();
        Assert.Equal(SectionViewMode.Landscape, info.ViewMode);
        Assert.True(info.AllowHideWatched);

        Assert.Same(section, Assert.Single(section.CreateInstances(s_userId, 1)));
    }

    private static FakeQueryCollection QueryWithUserId()
    {
        return new FakeQueryCollection
        {
            ["UserId"] = s_userId.ToString()
        };
    }

    private static DateTime InvokeGetSortDate(BaseItemDto item, Dictionary<Guid, DateTime> lookup)
    {
        MethodInfo method = typeof(ContinueWatchingNextUpSection)
            .GetMethod("GetSortDate", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (DateTime)method.Invoke(null, [item, lookup])!;
    }
}
