using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using Jellyfin.Plugin.HomeScreenSections.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.HomeScreenSections.Tests.Services;

public class ArrApiServiceTests
{
    private static readonly DateTime s_referenceDate = new(2026, 8, 7, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(14)]
    public void CalculateEndDate_days_adds_that_many_days(int days)
    {
        DateTime result = ArrApiService.CalculateEndDate(s_referenceDate, days, TimeframeUnit.Days);
        Assert.Equal(s_referenceDate.AddDays(days), result);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void CalculateEndDate_weeks_adds_seven_days_each(int weeks)
    {
        DateTime result = ArrApiService.CalculateEndDate(s_referenceDate, weeks, TimeframeUnit.Weeks);
        Assert.Equal(s_referenceDate.AddDays(weeks * 7), result);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    public void CalculateEndDate_months_adds_calendar_months(int months)
    {
        DateTime result = ArrApiService.CalculateEndDate(s_referenceDate, months, TimeframeUnit.Months);
        Assert.Equal(s_referenceDate.AddMonths(months), result);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void CalculateEndDate_years_adds_calendar_years(int years)
    {
        DateTime result = ArrApiService.CalculateEndDate(s_referenceDate, years, TimeframeUnit.Years);
        Assert.Equal(s_referenceDate.AddYears(years), result);
    }

    [Fact]
    public void CalculateEndDate_supports_negative_timeframes()
    {
        DateTime result = ArrApiService.CalculateEndDate(s_referenceDate, -3, TimeframeUnit.Days);
        Assert.Equal(s_referenceDate.AddDays(-3), result);
    }

    [Theory]
    [InlineData("YYYY/MM/DD", "/", "2026/08/07")]
    [InlineData("DD/MM/YYYY", "/", "07/08/2026")]
    [InlineData("MM/DD/YYYY", "/", "08/07/2026")]
    [InlineData("DD/MM", "/", "07/08")]
    [InlineData("MM/DD", "/", "08/07")]
    public void FormatDate_supports_all_known_formats(string format, string delimiter, string expected)
    {
        Assert.Equal(expected, ArrApiService.FormatDate(s_referenceDate, format, delimiter));
    }

    [Fact]
    public void FormatDate_uses_custom_delimiter()
    {
        Assert.Equal("2026-08-07", ArrApiService.FormatDate(s_referenceDate, "YYYY/MM/DD", "-"));
        Assert.Equal("07.08.2026", ArrApiService.FormatDate(s_referenceDate, "DD/MM/YYYY", "."));
    }

    [Fact]
    public void FormatDate_is_case_insensitive()
    {
        // Assert against the literal, not the uppercase call's own output, so a lowercase
        // path that silently fell back to ISO ordering would fail here.
        Assert.Equal("2026/08/07", ArrApiService.FormatDate(s_referenceDate, "yyyy/mm/dd", "/"));
    }

    [Fact]
    public void FormatDate_unknown_format_falls_back_to_iso_order()
    {
        Assert.Equal("2026/08/07", ArrApiService.FormatDate(s_referenceDate, "not-a-format", "/"));
    }

    [Fact]
    public void FormatDate_pads_single_digit_components()
    {
        DateTime date = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal("2026/01/02", ArrApiService.FormatDate(date, "YYYY/MM/DD", "/"));
    }

    [Theory]
    [InlineData(ArrServiceType.Sonarr)]
    [InlineData(ArrServiceType.Radarr)]
    [InlineData(ArrServiceType.Lidarr)]
    [InlineData(ArrServiceType.Readarr)]
    public async Task GetArrCalendarAsync_returns_null_when_service_not_configured(ArrServiceType serviceType)
    {
        // No configured arr services: every service must short-circuit to null before any HTTP call.
        using HttpClient httpClient = new HttpClient();
        ArrApiService service = new ArrApiService(NullLogger<ArrApiService>.Instance, httpClient);

        object? result = serviceType switch
        {
            ArrServiceType.Sonarr => await service.GetArrCalendarAsync<SonarrCalendarDto>(serviceType, s_referenceDate, s_referenceDate.AddDays(7)),
            ArrServiceType.Radarr => await service.GetArrCalendarAsync<RadarrCalendarDto>(serviceType, s_referenceDate, s_referenceDate.AddDays(7)),
            ArrServiceType.Lidarr => await service.GetArrCalendarAsync<LidarrCalendarDto>(serviceType, s_referenceDate, s_referenceDate.AddDays(7)),
            _ => await service.GetArrCalendarAsync<ReadarrCalendarDto>(serviceType, s_referenceDate, s_referenceDate.AddDays(7))
        };

        Assert.Null(result);
    }
}
