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
    private readonly Mock<IUserViewManager> _userViewManager = new();
    private readonly Mock<IUserManager> _userManager = new();
    private readonly Mock<IDtoService> _dtoService = new();
    private readonly Mock<ILibraryManager> _libraryManager = new();
    private readonly Mock<ISessionManager> _sessionManager = new();
    private readonly Mock<ITVSeriesManager> _tvSeriesManager = new();
    private readonly User _user = new("TestUser", "TestAuthProvider", "TestPasswordResetProvider");
    private readonly Guid _userId = Guid.NewGuid();

    private NextUpQuery? _capturedQuery;
    private DtoOptions? _capturedOptions;

    private NextUpSection MakeSection()
    {
        return new NextUpSection(
            _userViewManager.Object,
            _userManager.Object,
            _dtoService.Object,
            _libraryManager.Object,
            _sessionManager.Object,
            _tvSeriesManager.Object);
    }

    private void SetupNextUp(params BaseItem[] items)
    {
        _userManager
            .Setup(manager => manager.GetUserById(_userId))
            .Returns(_user);

        _tvSeriesManager
            .Setup(manager => manager.GetNextUp(It.IsAny<NextUpQuery>(), It.IsAny<DtoOptions>()))
            .Callback<NextUpQuery, DtoOptions>((query, options) =>
            {
                _capturedQuery = query;
                _capturedOptions = options;
            })
            .Returns(new QueryResult<BaseItem>(items));

        // GetBaseItemDtos' fourth parameter is optional, and expression trees cannot be
        // compiled against optional arguments, so it is matched explicitly.
        _dtoService
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

        QueryResult<BaseItemDto> result = section.GetResults(new HomeScreenSectionPayload { UserId = _userId }, new FakeQueryCollection());

        Assert.NotNull(result);
        Assert.NotNull(_capturedQuery);
        Assert.True(_capturedQuery!.EnableRewatching);
        Assert.Equal(DateTime.MinValue, _capturedQuery.NextUpDateCutoff);
        Assert.Equal(24, _capturedQuery.Limit);
        Assert.Same(_user, _capturedQuery.User);
        Assert.False(_capturedQuery.EnableTotalRecordCount);
        Assert.NotNull(_capturedOptions);
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

        section.GetResults(new HomeScreenSectionPayload { UserId = _userId }, query);

        Assert.False(_capturedQuery!.EnableRewatching);
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

        section.GetResults(new HomeScreenSectionPayload { UserId = _userId }, query);

        // Comparison is ordinal against lowercase "true".
        Assert.False(_capturedQuery!.EnableRewatching);
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

        section.GetResults(new HomeScreenSectionPayload { UserId = _userId }, query);

        Assert.Equal(new DateTime(2026, 1, 5), _capturedQuery!.NextUpDateCutoff);
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

        section.GetResults(new HomeScreenSectionPayload { UserId = _userId }, query);

        Assert.Equal(DateTime.MinValue, _capturedQuery!.NextUpDateCutoff);
    }

    [Fact]
    public void GetResults_wraps_dto_service_output_with_total_count()
    {
        SetupNextUp(new MediaBrowser.Controller.Entities.Movies.Movie());
        NextUpSection section = MakeSection();

        QueryResult<BaseItemDto> result = section.GetResults(new HomeScreenSectionPayload { UserId = _userId }, new FakeQueryCollection());

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

        List<Jellyfin.Plugin.HomeScreenSections.Library.IHomeScreenSection> instances = [.. section.CreateInstances(_userId, 5)];

        Assert.Single(instances);
        Assert.Same(section, instances[0]);
    }
}
