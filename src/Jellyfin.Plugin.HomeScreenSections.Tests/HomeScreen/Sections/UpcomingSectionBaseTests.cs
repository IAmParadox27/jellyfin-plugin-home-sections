using System.Reflection;
using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using Jellyfin.Plugin.HomeScreenSections.Services;
using Jellyfin.Plugin.HomeScreenSections.Tests.Support;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Jellyfin.Plugin.HomeScreenSections.Tests.HomeScreen.Sections;

[Collection("Plugin Instance")]
public class UpcomingSectionBaseTests
{
    // Fixed clock so countdown bucketing is deterministic; the production code reads
    // DateTime.Now internally, which made these tests flaky across midnight.
    private static readonly DateTime s_now = new DateTime(2026, 8, 7, 14, 30, 0, DateTimeKind.Local);

    private readonly PluginFixture m_fixture;

    public UpcomingSectionBaseTests(PluginFixture fixture)
    {
        m_fixture = fixture;
    }

    [Fact]
    public void Countdown_for_past_or_current_date_is_today()
    {
        TestUpcomingSection section = MakeSection();
        PluginConfiguration config = HomeScreenSectionsPlugin.Instance.Configuration;

        Assert.StartsWith("Today! - ", section.ExposedCountdown(s_now.AddDays(-2), config, s_now), StringComparison.Ordinal);
        Assert.StartsWith("Today! - ", section.ExposedCountdown(s_now.Date, config, s_now), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, "1 Day")]
    [InlineData(2, "2 Days")]
    [InlineData(6, "6 Days")]
    public void Countdown_below_a_week_uses_days(int daysFromNow, string expectedText)
    {
        TestUpcomingSection section = MakeSection();
        PluginConfiguration config = HomeScreenSectionsPlugin.Instance.Configuration;

        string countdown = section.ExposedCountdown(s_now.Date.AddDays(daysFromNow), config, s_now);

        Assert.StartsWith(expectedText + " - ", countdown, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(7, "1 Week")]
    [InlineData(13, "1 Week, 6 Days")]
    [InlineData(29, "4 Weeks, 1 Day")]
    public void Countdown_below_a_month_uses_weeks(int daysFromNow, string expectedText)
    {
        TestUpcomingSection section = MakeSection();
        PluginConfiguration config = HomeScreenSectionsPlugin.Instance.Configuration;

        string countdown = section.ExposedCountdown(s_now.Date.AddDays(daysFromNow), config, s_now);

        Assert.StartsWith(expectedText + " - ", countdown, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(30, "1 Month")]
    [InlineData(44, "1 Month, 2 Weeks")]
    [InlineData(364, "12 Months")]
    public void Countdown_below_a_year_uses_months(int daysFromNow, string expectedText)
    {
        TestUpcomingSection section = MakeSection();
        PluginConfiguration config = HomeScreenSectionsPlugin.Instance.Configuration;

        string countdown = section.ExposedCountdown(s_now.Date.AddDays(daysFromNow), config, s_now);

        Assert.StartsWith(expectedText + " - ", countdown, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(365, "1 Year")]
    [InlineData(395, "1 Year, 1 Month")]
    public void Countdown_beyond_a_year_uses_years(int daysFromNow, string expectedText)
    {
        TestUpcomingSection section = MakeSection();
        PluginConfiguration config = HomeScreenSectionsPlugin.Instance.Configuration;

        string countdown = section.ExposedCountdown(s_now.Date.AddDays(daysFromNow), config, s_now);

        Assert.StartsWith(expectedText + " - ", countdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Countdown_appends_formatted_release_date()
    {
        TestUpcomingSection section = MakeSection();
        PluginConfiguration config = HomeScreenSectionsPlugin.Instance.Configuration;
        DateTime releaseDate = s_now.Date.AddDays(3);

        string countdown = section.ExposedCountdown(releaseDate, config, s_now);

        string expectedSuffix = ArrApiService.FormatDate(releaseDate.ToLocalTime(), config.DateFormat, config.DateDelimiter);
        Assert.EndsWith(" - " + expectedSuffix, countdown, StringComparison.Ordinal);
    }

    [Fact]
    public void GetResults_returns_empty_when_service_not_configured()
    {
        PluginConfiguration config = HomeScreenSectionsPlugin.Instance.Configuration;
        config.Radarr.Url = string.Empty;
        config.Radarr.ApiKey = string.Empty;

        TestUpcomingSection section = MakeSection(() => throw new InvalidOperationException("must not fetch"));

        QueryResult<BaseItemDto> result = section.GetResults(new HomeScreenSectionPayload { UserId = Guid.NewGuid() }, new FakeQueryCollection());

        Assert.Empty(result.Items);
    }

    [Fact]
    public void GetResults_maps_calendar_items_to_dtos()
    {
        PluginConfiguration config = HomeScreenSectionsPlugin.Instance.Configuration;
        config.Radarr.Url = "http://radarr.test";
        config.Radarr.ApiKey = "test-key";
        config.FilterUpcomingByLibraryAccess = false;
        try
        {
            Guid firstId = Guid.NewGuid();
            Guid secondId = Guid.NewGuid();
            RadarrCalendarDto[] calendar =
            [
                new RadarrCalendarDto { Id = 1, Title = "First", Path = "/movies/first" },
                new RadarrCalendarDto { Id = 2, Title = "Second", Path = "/movies/second" }
            ];

            TestUpcomingSection section = MakeSection(
                () => calendar,
                item => new BaseItemDto { Id = item.Id == 1 ? firstId : secondId, Name = item.Title });

            QueryResult<BaseItemDto> result = section.GetResults(new HomeScreenSectionPayload { UserId = Guid.NewGuid() }, new FakeQueryCollection());

            Assert.Equal(2, result.Items.Count);
            Assert.Contains(result.Items, dto => dto.Id == firstId);
            Assert.Contains(result.Items, dto => dto.Id == secondId);
        }
        finally
        {
            config.Radarr.Url = string.Empty;
            config.Radarr.ApiKey = string.Empty;
            config.FilterUpcomingByLibraryAccess = true;
        }
    }

    [Fact]
    public void GetResults_caps_output_at_sixteen_items()
    {
        PluginConfiguration config = HomeScreenSectionsPlugin.Instance.Configuration;
        config.Radarr.Url = "http://radarr.test";
        config.Radarr.ApiKey = "test-key";
        config.FilterUpcomingByLibraryAccess = false;
        try
        {
            RadarrCalendarDto[] calendar = Enumerable.Range(0, 25)
                .Select(id => new RadarrCalendarDto { Id = id, Title = $"Movie {id}" })
                .ToArray();

            TestUpcomingSection section = MakeSection(() => calendar);

            QueryResult<BaseItemDto> result = section.GetResults(new HomeScreenSectionPayload { UserId = Guid.NewGuid() }, new FakeQueryCollection());

            Assert.Equal(16, result.Items.Count);
        }
        finally
        {
            config.Radarr.Url = string.Empty;
            config.Radarr.ApiKey = string.Empty;
            config.FilterUpcomingByLibraryAccess = true;
        }
    }

    [Fact]
    public void GetResults_swallows_expected_exceptions()
    {
        PluginConfiguration config = HomeScreenSectionsPlugin.Instance.Configuration;
        config.Radarr.Url = "http://radarr.test";
        config.Radarr.ApiKey = "test-key";
        try
        {
            TestUpcomingSection section = MakeSection(() => throw new HttpRequestException("boom"));

            QueryResult<BaseItemDto> result = section.GetResults(new HomeScreenSectionPayload { UserId = Guid.NewGuid() }, new FakeQueryCollection());

            Assert.Empty(result.Items);
        }
        finally
        {
            config.Radarr.Url = string.Empty;
            config.Radarr.ApiKey = string.Empty;
        }
    }

    [Fact]
    public void Random_background_colors_stay_in_dark_range()
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            string color = TestUpcomingSection.ExposedRandomBgColor();

            Assert.Equal(6, color.Length);
            int red = int.Parse(color.AsSpan(0, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
            int green = int.Parse(color.AsSpan(2, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
            int blue = int.Parse(color.AsSpan(4, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
            Assert.InRange(red, 0, 127);
            Assert.InRange(green, 0, 127);
            Assert.InRange(blue, 0, 127);
        }
    }

    [Fact]
    public void Fallback_cover_url_points_at_placeholder_service()
    {
        TestUpcomingSection section = MakeSection();

        string url = section.ExposedFallbackCoverUrl(new RadarrCalendarDto { Title = "Anything" });

        Assert.StartsWith("https://placehold.co/", url, StringComparison.Ordinal);
        Assert.Contains("Unknown%20Item", url, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"C:\media\movies\", "C:/media/movies")]
    [InlineData("a/b/c/", "a/b/c")]
    [InlineData("no-trailing", "no-trailing")]
    public void NormalizePath_converts_backslashes_and_trims_trailing_slash(string input, string expected)
    {
        string result = (string)InvokeUpcomingBaseStatic("NormalizePath", input)!;
        Assert.Equal(expected, result);
    }

    [Fact]
    public void IsItemAccessible_allows_missing_or_unmapped_paths()
    {
        string[] allLocations = ["/media/movies", "/media/tv"];
        string[] permitted = ["/media/movies"];

        Assert.True((bool)InvokeUpcomingBaseStatic("IsItemAccessible", null, allLocations, permitted)!);
        Assert.True((bool)InvokeUpcomingBaseStatic("IsItemAccessible", string.Empty, allLocations, permitted)!);
        // Path under no known library cannot be mapped, so it defaults to accessible.
        Assert.True((bool)InvokeUpcomingBaseStatic("IsItemAccessible", "/elsewhere/file.mkv", allLocations, permitted)!);
    }

    [Fact]
    public void IsItemAccessible_checks_membership_against_permitted_libraries()
    {
        string[] allLocations = ["/media/movies", "/media/tv"];
        string[] permitted = ["/media/movies"];

        Assert.True((bool)InvokeUpcomingBaseStatic("IsItemAccessible", "/media/movies/Film.mkv", allLocations, permitted)!);
        Assert.False((bool)InvokeUpcomingBaseStatic("IsItemAccessible", "/media/tv/Episode.mkv", allLocations, permitted)!);
    }

    [Theory]
    [InlineData(1, 0, "Week", "Day", "1 Week")]
    [InlineData(2, 3, "Month", "Week", "2 Months, 3 Weeks")]
    [InlineData(1, 1, "Year", "Month", "1 Year, 1 Month")]
    [InlineData(5, 0, "Day", "Day", "5 Days")]
    public void FormatTimeUnit_pluralizes_and_joins_secondary(int primary, int secondary, string primaryUnit, string secondaryUnit, string expected)
    {
        string result = (string)InvokeUpcomingBaseStatic("FormatTimeUnit", primary, secondary, primaryUnit, secondaryUnit)!;
        Assert.Equal(expected, result);
    }

    private static readonly Type s_upcomingBaseType = typeof(UpcomingSectionBase<RadarrCalendarDto>);

    private static object? InvokeUpcomingBaseStatic(string name, params object?[] args)
    {
        MethodInfo method = s_upcomingBaseType.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Private static '{name}' not found on {s_upcomingBaseType.Name}.");
        return method.Invoke(null, args);
    }

    private TestUpcomingSection MakeSection(
        Func<RadarrCalendarDto[]>? calendarProvider = null,
        Func<RadarrCalendarDto, BaseItemDto>? dtoCreator = null)
    {
        ImageCacheService imageCacheService = new ImageCacheService(
            NullLogger<ImageCacheService>.Instance,
            m_fixture.Paths,
            new HttpClient());

        return new TestUpcomingSection(
            new Mock<IUserManager>().Object,
            new Mock<ILibraryManager>().Object,
            new Mock<IDtoService>().Object,
            new ArrApiService(NullLogger<ArrApiService>.Instance, new HttpClient()),
            imageCacheService,
            NullLogger<TestUpcomingSection>.Instance,
            calendarProvider ?? (() => []),
            dtoCreator);
    }

    private sealed class TestUpcomingSection : UpcomingSectionBase<RadarrCalendarDto>
    {
        private readonly Func<RadarrCalendarDto[]> m_calendarProvider;
        private readonly Func<RadarrCalendarDto, BaseItemDto>? m_dtoCreator;

        public TestUpcomingSection(
            IUserManager userManager,
            ILibraryManager libraryManager,
            IDtoService dtoService,
            ArrApiService arrApiService,
            ImageCacheService imageCacheService,
            Microsoft.Extensions.Logging.ILogger logger,
            Func<RadarrCalendarDto[]> calendarProvider,
            Func<RadarrCalendarDto, BaseItemDto>? dtoCreator)
            : base(userManager, libraryManager, dtoService, arrApiService, imageCacheService, logger)
        {
            m_calendarProvider = calendarProvider;
            m_dtoCreator = dtoCreator;
        }

        public override string? Section => "TestUpcoming";

        public override string? DisplayText { get; set; } = "Test Upcoming";

        public string ExposedCountdown(DateTime releaseDate, PluginConfiguration config, DateTime? now = null)
        {
            return CalculateCountdown(releaseDate, config, now);
        }

        public static string ExposedRandomBgColor()
        {
            return GetRandomBgColor();
        }

        public string ExposedFallbackCoverUrl(RadarrCalendarDto item)
        {
            return GetFallbackCoverUrl(item);
        }

        protected override (string? url, string? apiKey) GetServiceConfiguration(PluginConfiguration config)
        {
            return (config.Radarr.Url, config.Radarr.ApiKey);
        }

        protected override (int value, TimeframeUnit unit) GetTimeframeConfiguration(PluginConfiguration config)
        {
            return (config.Radarr.UpcomingTimeframeValue, config.Radarr.UpcomingTimeframeUnit);
        }

        protected override RadarrCalendarDto[] GetCalendarItems(DateTime startDate, DateTime endDate)
        {
            return m_calendarProvider();
        }

        protected override IOrderedEnumerable<RadarrCalendarDto> FilterAndSortItems(RadarrCalendarDto[] items)
        {
            return items.OrderBy(item => item.Id);
        }

        protected override string? GetItemPath(RadarrCalendarDto item)
        {
            return item.Path;
        }

        protected override BaseItemDto CreateDto(RadarrCalendarDto item, PluginConfiguration config)
        {
            return m_dtoCreator?.Invoke(item) ?? new BaseItemDto { Id = Guid.NewGuid(), Name = item.Title };
        }

        protected override string GetServiceName()
        {
            return "Radarr";
        }

        protected override string GetSectionName()
        {
            return "TestUpcoming";
        }

        public override IEnumerable<IHomeScreenSection> CreateInstances(Guid? userId, int instanceCount)
        {
            yield return this;
        }

        public override HomeScreenSectionInfo GetInfo()
        {
            return new HomeScreenSectionInfo
            {
                Section = Section,
                DisplayText = DisplayText
            };
        }
    }
}
