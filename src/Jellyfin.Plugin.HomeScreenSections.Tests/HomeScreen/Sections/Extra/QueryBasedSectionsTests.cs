using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections.Extra;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using Jellyfin.Plugin.HomeScreenSections.Tests.Support;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Moq;

namespace Jellyfin.Plugin.HomeScreenSections.Tests.HomeScreen.Sections.Extra;

[Collection("Plugin Instance")]
public class QueryBasedSectionsTests
{
    private readonly Mock<IUserManager> m_userManager = new();
    private readonly Mock<ILibraryManager> m_libraryManager = new();
    private readonly Mock<IDtoService> m_dtoService = new();
    private readonly User m_user = new("QueryUser", "AuthProvider", "PasswordResetProvider");
    private readonly Guid m_userId = Guid.NewGuid();

    public QueryBasedSectionsTests(PluginFixture fixture)
    {
        // The fixture parameter binds this class to the "Plugin Instance" collection so the
        // shared HomeScreenSectionsPlugin.Instance is initialized; the object itself is unused.
        _ = fixture;
    }

    private InternalItemsQuery? m_capturedQuery;

    private void SetupLibrary(params BaseItem[] items)
    {
        m_userManager
            .Setup(manager => manager.GetUserById(m_userId))
            .Returns(m_user);

        m_libraryManager
            .Setup(manager => manager.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(query => m_capturedQuery = query)
            .Returns(new QueryResult<BaseItem>(items));

        m_dtoService
            .Setup(service => service.GetBaseItemDtos(
                It.IsAny<IReadOnlyList<BaseItem>>(),
                It.IsAny<DtoOptions>(),
                It.IsAny<User>(),
                It.IsAny<BaseItem>()))
            .Returns((IReadOnlyList<BaseItem> list, DtoOptions options, User user, BaseItem owner) =>
                list.Select(_ => new BaseItemDto { Id = Guid.NewGuid() }).ToArray());
    }

    [Fact]
    public void Favorites_returns_mapped_dtos_for_user()
    {
        SetupLibrary(new Movie());
        FavoritesSection section = new FavoritesSection(m_userManager.Object, m_libraryManager.Object, m_dtoService.Object);

        QueryResult<BaseItemDto> result = section.GetResults(new HomeScreenSectionPayload { UserId = m_userId }, new FakeQueryCollection());

        Assert.Single(result.Items);
        Assert.True(m_capturedQuery!.IsFavorite);
        Assert.False(m_capturedQuery!.IsPlayed.HasValue);
    }

    [Fact]
    public void Favorites_returns_empty_when_user_missing()
    {
        m_userManager.Setup(manager => manager.GetUserById(m_userId)).Returns((User?)null);
        FavoritesSection section = new FavoritesSection(m_userManager.Object, m_libraryManager.Object, m_dtoService.Object);

        QueryResult<BaseItemDto> result = section.GetResults(new HomeScreenSectionPayload { UserId = m_userId }, new FakeQueryCollection());

        Assert.Empty(result.Items);
    }

    [Fact]
    public void RandomUnwatched_queries_only_unplayed_items()
    {
        SetupLibrary(new Movie());
        RandomUnwatchedSection section = new RandomUnwatchedSection(m_userManager.Object, m_libraryManager.Object, m_dtoService.Object);

        QueryResult<BaseItemDto> result = section.GetResults(new HomeScreenSectionPayload { UserId = m_userId }, new FakeQueryCollection());

        Assert.Single(result.Items);
        Assert.False(m_capturedQuery!.IsPlayed);
    }

    [Fact]
    public void Trending_queries_played_items_by_play_count()
    {
        SetupLibrary(new Movie());
        TrendingSection section = new TrendingSection(m_userManager.Object, m_libraryManager.Object, m_dtoService.Object);

        QueryResult<BaseItemDto> result = section.GetResults(new HomeScreenSectionPayload { UserId = m_userId }, new FakeQueryCollection());

        Assert.Single(result.Items);
        Assert.True(m_capturedQuery!.IsPlayed);
    }

    [Fact]
    public void RecentlyPlayed_queries_played_non_resumable_items()
    {
        SetupLibrary(new Movie());
        RecentlyPlayedSection section = new RecentlyPlayedSection(m_userManager.Object, m_libraryManager.Object, m_dtoService.Object);

        QueryResult<BaseItemDto> result = section.GetResults(new HomeScreenSectionPayload { UserId = m_userId }, new FakeQueryCollection());

        Assert.Single(result.Items);
        Assert.True(m_capturedQuery!.IsPlayed);
        Assert.False(m_capturedQuery!.IsResumable);
    }

    [Fact]
    public void ComingSoon_constrains_premiere_window_to_next_90_days()
    {
        SetupLibrary(new Movie());
        ComingSoonInLibrarySection section = new ComingSoonInLibrarySection(m_userManager.Object, m_libraryManager.Object, m_dtoService.Object);

        QueryResult<BaseItemDto> result = section.GetResults(new HomeScreenSectionPayload { UserId = m_userId }, new FakeQueryCollection());

        Assert.Single(result.Items);
        Assert.NotNull(m_capturedQuery!.MinPremiereDate);
        Assert.NotNull(m_capturedQuery!.MaxPremiereDate);

        // The window spans exactly 90 days, anchored at UTC midnight today.
        TimeSpan window = m_capturedQuery!.MaxPremiereDate!.Value - m_capturedQuery!.MinPremiereDate!.Value;
        Assert.Equal(90, window.TotalDays);
        Assert.Equal(TimeSpan.Zero, m_capturedQuery!.MinPremiereDate!.Value.TimeOfDay);
        Assert.InRange((DateTime.UtcNow.Date - m_capturedQuery!.MinPremiereDate!.Value.Date).TotalDays, 0, 1);
    }

    [Fact]
    public void Kids_returns_empty_when_user_missing()
    {
        m_userManager.Setup(manager => manager.GetUserById(m_userId)).Returns((User?)null);
        KidsSection section = new KidsSection(m_userManager.Object, m_libraryManager.Object, m_dtoService.Object);

        QueryResult<BaseItemDto> result = section.GetResults(new HomeScreenSectionPayload { UserId = m_userId }, new FakeQueryCollection());

        Assert.Empty(result.Items);
    }

    [Fact]
    public void Kids_applies_family_ratings_filter()
    {
        SetupLibrary(new Movie());
        KidsSection section = new KidsSection(m_userManager.Object, m_libraryManager.Object, m_dtoService.Object);

        QueryResult<BaseItemDto> result = section.GetResults(new HomeScreenSectionPayload { UserId = m_userId }, new FakeQueryCollection());

        Assert.Single(result.Items);
        Assert.NotNull(m_capturedQuery!.OfficialRatings);
        Assert.Contains("PG", m_capturedQuery!.OfficialRatings, StringComparer.Ordinal);
    }

    [Fact]
    public void Kids_hides_watched_items_when_admin_setting_enabled()
    {
        SectionSettings[] original = HomeScreenSectionsPlugin.Instance.Configuration.SectionSettings;
        HomeScreenSectionsPlugin.Instance.Configuration.SectionSettings =
        [
            new SectionSettings { SectionId = "Kids", Enabled = true, HideWatchedItems = true }
        ];
        try
        {
            SetupLibrary(new Movie());
            KidsSection section = new KidsSection(m_userManager.Object, m_libraryManager.Object, m_dtoService.Object);

            section.GetResults(new HomeScreenSectionPayload { UserId = m_userId }, new FakeQueryCollection());

            Assert.False(m_capturedQuery!.IsPlayed);
        }
        finally
        {
            HomeScreenSectionsPlugin.Instance.Configuration.SectionSettings = original;
        }
    }

    [Theory]
    [InlineData("Favorites")]
    [InlineData("RandomUnwatched")]
    [InlineData("Trending")]
    [InlineData("RecentlyPlayed")]
    [InlineData("ComingSoonInLibrary")]
    [InlineData("Kids")]
    public void GetInfo_reports_landscape_single_row(string expectedSection)
    {
        IHomeScreenSection section = expectedSection switch
        {
            "Favorites" => new FavoritesSection(m_userManager.Object, m_libraryManager.Object, m_dtoService.Object),
            "RandomUnwatched" => new RandomUnwatchedSection(m_userManager.Object, m_libraryManager.Object, m_dtoService.Object),
            "Trending" => new TrendingSection(m_userManager.Object, m_libraryManager.Object, m_dtoService.Object),
            "RecentlyPlayed" => new RecentlyPlayedSection(m_userManager.Object, m_libraryManager.Object, m_dtoService.Object),
            "ComingSoonInLibrary" => new ComingSoonInLibrarySection(m_userManager.Object, m_libraryManager.Object, m_dtoService.Object),
            _ => new KidsSection(m_userManager.Object, m_libraryManager.Object, m_dtoService.Object)
        };

        HomeScreenSectionInfo info = section.GetInfo();

        Assert.Equal(expectedSection, info.Section);
        Assert.Equal(SectionViewMode.Landscape, info.ViewMode);
        Assert.Equal(1, info.Limit);
        Assert.Same(section, Assert.Single(section.CreateInstances(m_userId, 1)));
    }
}
