using System.Net;
using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using Jellyfin.Plugin.HomeScreenSections.Services;
using Jellyfin.Plugin.HomeScreenSections.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.HomeScreenSections.Tests.Services;

/// <summary>
/// Exercises the HTTP side of ArrApiService against a fake handler. Needs the plugin
/// fixture because the service reads arr URLs/keys from Instance.Configuration.
/// </summary>
[Collection("Plugin Instance")]
public class ArrApiServiceCalendarTests
{

    public ArrApiServiceCalendarTests(PluginFixture fixture)
    {
        _ = fixture;
    }

    [Fact]
    public async Task GetArrCalendarAsync_builds_radarr_v3_url_and_sends_api_key()
    {
        PluginConfiguration config = Configure(ArrServiceType.Radarr, "http://radarr.test/");
        try
        {
            FakeHttpMessageHandler handler = FakeHttpMessageHandler.RespondingWithJson("[]");
            ArrApiService service = MakeService(handler);
            DateTime start = new DateTime(2026, 8, 7, 0, 0, 0, DateTimeKind.Utc);
            DateTime end = start.AddDays(7);

            RadarrCalendarDto[]? result = await service.GetArrCalendarAsync<RadarrCalendarDto>(ArrServiceType.Radarr, start, end);

            Assert.NotNull(result);
            Assert.Empty(result!);
            HttpRequestMessage request = Assert.Single(handler.Requests);
            string url = request.RequestUri!.ToString();
            Assert.StartsWith("http://radarr.test/api/v3/calendar?", url, StringComparison.Ordinal);
            Assert.Contains("start=2026-08-07T00:00:00Z", url, StringComparison.Ordinal);
            Assert.Contains("end=2026-08-14T00:00:00Z", url, StringComparison.Ordinal);
            Assert.Equal("test-key", request.Headers.GetValues("X-API-KEY").Single());
        }
        finally
        {
            Reset(config, ArrServiceType.Radarr);
        }
    }

    [Theory]
    [InlineData(ArrServiceType.Sonarr, "v3", "includeSeries=true")]
    [InlineData(ArrServiceType.Lidarr, "v1", null)]
    [InlineData(ArrServiceType.Readarr, "v1", "includeAuthor=true")]
    public async Task GetArrCalendarAsync_uses_service_specific_api_paths(ArrServiceType serviceType, string apiVersion, string? extraParam)
    {
        PluginConfiguration config = Configure(serviceType, $"http://{serviceType}.test");
        try
        {
            FakeHttpMessageHandler handler = FakeHttpMessageHandler.RespondingWithJson("[]");
            ArrApiService service = MakeService(handler);

            object? result = serviceType switch
            {
                ArrServiceType.Sonarr => await service.GetArrCalendarAsync<SonarrCalendarDto>(serviceType, DateTime.UtcNow, DateTime.UtcNow.AddDays(1)),
                ArrServiceType.Lidarr => await service.GetArrCalendarAsync<LidarrCalendarDto>(serviceType, DateTime.UtcNow, DateTime.UtcNow.AddDays(1)),
                _ => await service.GetArrCalendarAsync<ReadarrCalendarDto>(serviceType, DateTime.UtcNow, DateTime.UtcNow.AddDays(1))
            };

            Assert.NotNull(result);
            string url = Assert.Single(handler.Requests).RequestUri!.ToString();
            Assert.Contains($"/api/{apiVersion}/calendar?", url, StringComparison.Ordinal);
            if (extraParam != null)
            {
                Assert.Contains(extraParam, url, StringComparison.Ordinal);
            }
        }
        finally
        {
            Reset(config, serviceType);
        }
    }

    [Fact]
    public async Task GetArrCalendarAsync_deserializes_items()
    {
        PluginConfiguration config = Configure(ArrServiceType.Radarr, "http://radarr.test");
        try
        {
            FakeHttpMessageHandler handler = FakeHttpMessageHandler.RespondingWithJson("""
                [
                    { "id": 1, "title": "First Movie", "monitored": true },
                    { "id": 2, "title": "Second Movie", "monitored": false }
                ]
                """);
            ArrApiService service = MakeService(handler);

            RadarrCalendarDto[]? result = await service.GetArrCalendarAsync<RadarrCalendarDto>(
                ArrServiceType.Radarr, DateTime.UtcNow, DateTime.UtcNow.AddDays(30));

            Assert.NotNull(result);
            Assert.Equal(2, result!.Length);
            Assert.Equal("First Movie", result[0].Title);
            Assert.True(result[0].Monitored);
            Assert.False(result[1].Monitored);
        }
        finally
        {
            Reset(config, ArrServiceType.Radarr);
        }
    }

    [Fact]
    public async Task GetArrCalendarAsync_returns_null_for_http_failure()
    {
        PluginConfiguration config = Configure(ArrServiceType.Radarr, "http://radarr.test");
        try
        {
            ArrApiService service = MakeService(FakeHttpMessageHandler.RespondingWithStatus(HttpStatusCode.InternalServerError));

            RadarrCalendarDto[]? result = await service.GetArrCalendarAsync<RadarrCalendarDto>(
                ArrServiceType.Radarr, DateTime.UtcNow, DateTime.UtcNow.AddDays(30));

            Assert.Null(result);
        }
        finally
        {
            Reset(config, ArrServiceType.Radarr);
        }
    }

    [Fact]
    public async Task GetArrCalendarAsync_returns_empty_array_for_empty_body()
    {
        PluginConfiguration config = Configure(ArrServiceType.Radarr, "http://radarr.test");
        try
        {
            ArrApiService service = MakeService(FakeHttpMessageHandler.RespondingWithJson(string.Empty));

            RadarrCalendarDto[]? result = await service.GetArrCalendarAsync<RadarrCalendarDto>(
                ArrServiceType.Radarr, DateTime.UtcNow, DateTime.UtcNow.AddDays(30));

            Assert.NotNull(result);
            Assert.Empty(result!);
        }
        finally
        {
            Reset(config, ArrServiceType.Radarr);
        }
    }

    [Fact]
    public async Task GetArrCalendarAsync_returns_null_for_invalid_json()
    {
        PluginConfiguration config = Configure(ArrServiceType.Radarr, "http://radarr.test");
        try
        {
            ArrApiService service = MakeService(FakeHttpMessageHandler.RespondingWithJson("this is not json"));

            RadarrCalendarDto[]? result = await service.GetArrCalendarAsync<RadarrCalendarDto>(
                ArrServiceType.Radarr, DateTime.UtcNow, DateTime.UtcNow.AddDays(30));

            Assert.Null(result);
        }
        finally
        {
            Reset(config, ArrServiceType.Radarr);
        }
    }

    private static ArrApiService MakeService(FakeHttpMessageHandler handler)
    {
        return new ArrApiService(NullLogger<ArrApiService>.Instance, new HttpClient(handler));
    }

    private static PluginConfiguration Configure(ArrServiceType serviceType, string url)
    {
        PluginConfiguration config = HomeScreenSectionsPlugin.Instance.Configuration;
        ArrConfig arrConfig = serviceType switch
        {
            ArrServiceType.Sonarr => config.Sonarr,
            ArrServiceType.Radarr => config.Radarr,
            ArrServiceType.Lidarr => config.Lidarr,
            _ => config.Readarr
        };
        arrConfig.Url = url;
        arrConfig.ApiKey = "test-key";
        return config;
    }

    private static void Reset(PluginConfiguration config, ArrServiceType serviceType)
    {
        ArrConfig arrConfig = serviceType switch
        {
            ArrServiceType.Sonarr => config.Sonarr,
            ArrServiceType.Radarr => config.Radarr,
            ArrServiceType.Lidarr => config.Lidarr,
            _ => config.Readarr
        };
        arrConfig.Url = string.Empty;
        arrConfig.ApiKey = string.Empty;
    }
}
