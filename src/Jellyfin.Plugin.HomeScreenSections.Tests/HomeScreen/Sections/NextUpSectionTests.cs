using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using Jellyfin.Plugin.HomeScreenSections.Tests.Support;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Controller.TV;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Moq;

namespace Jellyfin.Plugin.HomeScreenSections.Tests.HomeScreen.Sections;

public class NextUpSectionTests
{
    private readonly Mock<IUserViewManager> m_userViewManager = new();
    private readonly Mock<IUserManager> m_userManager = new();
    private readonly Mock<IDtoService> m_dtoService = new();
    private readonly Mock<ILibraryManager> m_libraryManager = new();
    private readonly Mock<ISessionManager> m_sessionManager = new();
    private readonly Mock<ITVSeriesManager> m_tvSeriesManager = new();
    private readonly User m_user = new("TestUser", "TestAuthProvider", "TestPasswordResetProvider");
    private readonly Guid m_userId = Guid.NewGuid();

    private NextUpQuery? m_capturedQuery;
    private DtoOptions? m_capturedOptions;

    private NextUpSection MakeSection()
    {
        return new NextUpSection(
            m_userViewManager.Object,
            m_userManager.Object,
            m_dtoService.Object,
            m_libraryManager.Object,
            m_sessionManager.Object,
            m_tvSeriesManager.Object);
    }

    private void SetupNextUp(params BaseItem[] items)
    {
        m_userManager
            .Setup(manager => manager.GetUserById(m_userId))
            .Returns(m_user);

        m_tvSeriesManager
            .Setup(manager => manager.GetNextUp(It.IsAny<NextUpQuery>(), It.IsAny<DtoOptions>()))
            .Callback<NextUpQuery, DtoOptions>((query, options) =>
            {
                m_capturedQuery = query;
                m_capturedOptions = options;
            })
            .Returns(new QueryResult<BaseItem>(items));

        // GetBaseItemDtos' fourth parameter is optional, and expression trees cannot be
        // compiled against optional arguments, so it is matched explicitly.
        m_dtoService
            .Setup(service => service.GetBaseItemDtos(
                It.IsAny<IReadOnlyList<BaseItem>>(),
                It.IsAny<DtoOptions>(),
                It.IsAny<User>(),
                It.IsAny<BaseItem>()))
            .Returns(items.Select(_ => new BaseItemDto { Id = Guid.NewGuid() }).ToArray());
    }

    [Fact]
    public void GetResults_defaults_to_rewatching_enabled_and_no_cutoff()
    {
        SetupNextUp();
        NextUpSection section = MakeSection();

        QueryResult<BaseItemDto> result = section.GetResults(new HomeScreenSectionPayload { UserId = m_userId }, new FakeQueryCollection());

        Assert.NotNull(result);
        Assert.NotNull(m_capturedQuery);
        Assert.True(m_capturedQuery!.EnableRewatching);
        Assert.Equal(DateTime.MinValue, m_capturedQuery.NextUpDateCutoff);
        Assert.Equal(24, m_capturedQuery.Limit);
        Assert.Same(m_user, m_capturedQuery.User);
        Assert.False(m_capturedQuery.EnableTotalRecordCount);
        Assert.NotNull(m_capturedOptions);
    }

    [Fact]
    public void GetResults_honours_rewatching_disabled()
    {
        SetupNextUp();
        NextUpSection section = MakeSection();
        FakeQueryCollection query = new FakeQueryCollection
        {
            ["EnableRewatching"] = "false"
        };

        section.GetResults(new HomeScreenSectionPayload { UserId = m_userId }, query);

        Assert.False(m_capturedQuery!.EnableRewatching);
    }

    [Fact]
    public void GetResults_treats_non_true_rewatching_values_as_disabled()
    {
        SetupNextUp();
        NextUpSection section = MakeSection();
        FakeQueryCollection query = new FakeQueryCollection
        {
            ["EnableRewatching"] = "TRUE"
        };

        section.GetResults(new HomeScreenSectionPayload { UserId = m_userId }, query);

        // Comparison is ordinal against lowercase "true".
        Assert.False(m_capturedQuery!.EnableRewatching);
    }

    [Fact]
    public void GetResults_parses_valid_date_cutoff()
    {
        SetupNextUp();
        NextUpSection section = MakeSection();
        FakeQueryCollection query = new FakeQueryCollection
        {
            ["NextUpDateCutoff"] = "2026-01-05"
        };

        section.GetResults(new HomeScreenSectionPayload { UserId = m_userId }, query);

        Assert.Equal(new DateTime(2026, 1, 5), m_capturedQuery!.NextUpDateCutoff);
    }

    [Fact]
    public void GetResults_keeps_min_value_cutoff_for_invalid_date()
    {
        SetupNextUp();
        NextUpSection section = MakeSection();
        FakeQueryCollection query = new FakeQueryCollection
        {
            ["NextUpDateCutoff"] = "not-a-date"
        };

        section.GetResults(new HomeScreenSectionPayload { UserId = m_userId }, query);

        Assert.Equal(DateTime.MinValue, m_capturedQuery!.NextUpDateCutoff);
    }

    [Fact]
    public void GetResults_wraps_dto_service_output_with_total_count()
    {
        SetupNextUp(new MediaBrowser.Controller.Entities.Movies.Movie());
        NextUpSection section = MakeSection();

        QueryResult<BaseItemDto> result = section.GetResults(new HomeScreenSectionPayload { UserId = m_userId }, new FakeQueryCollection());

        Assert.Single(result.Items);
        Assert.Equal(1, result.TotalRecordCount);
    }

    [Fact]
    public void Section_metadata_is_stable()
    {
        NextUpSection section = MakeSection();

        Assert.Equal("NextUp", section.Section);
        Assert.Equal("Next Up", section.DisplayText);
        Assert.Equal(1, section.Limit);
        Assert.Equal("nextup", section.Route);
        Assert.Null(section.OriginalPayload);
    }

    [Fact]
    public void GetInfo_reports_landscape_view_mode()
    {
        NextUpSection section = MakeSection();

        HomeScreenSectionInfo info = section.GetInfo();

        Assert.Equal("NextUp", info.Section);
        Assert.Equal(SectionViewMode.Landscape, info.ViewMode);
        Assert.Equal(1, info.Limit);
    }

    [Fact]
    public void CreateInstances_yields_itself()
    {
        NextUpSection section = MakeSection();

        List<Jellyfin.Plugin.HomeScreenSections.Library.IHomeScreenSection> instances = [.. section.CreateInstances(m_userId, 5)];

        Assert.Single(instances);
        Assert.Same(section, instances[0]);
    }
}
