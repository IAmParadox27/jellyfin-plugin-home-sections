using System.Net;
using System.Text.Json;
using System.Xml.Serialization;
using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections.Upcoming;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using Jellyfin.Plugin.HomeScreenSections.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.HomeScreenSections.Tests
{
    public class ArrInstancesTests : IDisposable
    {
        private readonly string m_testDirectory = Path.Combine(Path.GetTempPath(), $"hss-tests-{Guid.NewGuid():N}");
        private readonly PluginConfiguration m_config;
        private readonly CalendarHandler m_handler = new CalendarHandler();
        private readonly HttpClient m_httpClient;
        private readonly ArrApiService m_service;
        private readonly HttpClient m_imageHttpClient;
        private readonly ImageCacheService m_imageCache;

        public ArrInstancesTests()
        {
            Mock<IApplicationPaths> applicationPaths = new Mock<IApplicationPaths>();
            applicationPaths.SetupGet(x => x.PluginConfigurationsPath).Returns(m_testDirectory);
            applicationPaths.SetupGet(x => x.PluginsPath).Returns(m_testDirectory);
            applicationPaths.SetupGet(x => x.ProgramDataPath).Returns(m_testDirectory);
            applicationPaths.SetupGet(x => x.DataPath).Returns(m_testDirectory);
            applicationPaths.SetupGet(x => x.CachePath).Returns(m_testDirectory);

            m_config = new PluginConfiguration { FilterUpcomingByLibraryAccess = false };
            Mock<IXmlSerializer> xmlSerializer = new Mock<IXmlSerializer>();
            xmlSerializer.Setup(x => x.DeserializeFromFile(It.IsAny<Type>(), It.IsAny<string>())).Returns(m_config);
            HomeScreenSectionsPlugin plugin = new HomeScreenSectionsPlugin(
                applicationPaths.Object,
                xmlSerializer.Object,
                Mock.Of<IServerConfigurationManager>(),
                Mock.Of<IServiceProvider>(),
                Mock.Of<IHomeScreenManager>(),
                Mock.Of<ITranslationManager>());
            Assert.Same(m_config, plugin.Configuration);
            m_httpClient = new HttpClient(m_handler);
            m_service = new ArrApiService(NullLogger<ArrApiService>.Instance, m_httpClient);
            m_imageHttpClient = new HttpClient(new CalendarHandler());
            m_imageCache = new ImageCacheService(NullLogger<ImageCacheService>.Instance, applicationPaths.Object, m_imageHttpClient);
        }

        [Fact]
        public void LegacyXmlConnectionRemainsAvailable()
        {
            ArrConfig config = ReadXml("<ArrConfig><Url>http://legacy.test/sonarr</Url><ApiKey>legacy-key</ApiKey><UpcomingTimeframeValue>2</UpcomingTimeframeValue></ArrConfig>");
            ArrInstanceConfig instance = Assert.Single(config.GetInstances());
            Assert.Equal("http://legacy.test/sonarr", instance.Url);
            Assert.Equal("legacy-key", instance.ApiKey);
            Assert.Equal(2, config.UpcomingTimeframeValue);
        }

        [Fact]
        public void ExplicitEmptyCollectionDoesNotRestoreLegacyConnection()
        {
            ArrConfig config = new ArrConfig { Url = "http://legacy.test", ApiKey = "legacy-key", Instances = [] };
            Assert.Empty(RoundTripXml(config).GetInstances());
        }

        [Fact]
        public void MissingCollectionSurvivesXmlRoundTrip()
        {
            ArrConfig config = new ArrConfig { Url = "http://legacy.test", ApiKey = "legacy-key" };
            Assert.Single(RoundTripXml(config).GetInstances());
        }

        [Fact]
        public void NamedInstancesSurviveXmlRoundTrip()
        {
            ArrConfig config = new ArrConfig { Instances = CreateInstances() };
            ArrConfig restored = RoundTripXml(config);
            Assert.Equal(new[] { "Standard", "Anime" }, restored.GetInstances().Select(x => x.Name));
            Assert.Equal(new[] { "first-key", "second-key" }, restored.GetInstances().Select(x => x.ApiKey));
        }

        [Fact]
        public async Task CalendarQueriesEveryInstanceWithItsOwnKeyAndBasePath()
        {
            m_config.Sonarr.Instances = CreateInstances();
            m_handler.Response = request => JsonResponse("""[{"id":1}]""");
            SonarrCalendarDto[]? items = await m_service.GetArrCalendarAsync<SonarrCalendarDto>(
                ArrServiceType.Sonarr, DateTime.UtcNow, DateTime.UtcNow.AddDays(7));

            Assert.NotNull(items);
            Assert.Equal(2, items.Length);
            Assert.NotEqual(items[0].SourceId, items[1].SourceId);
            Assert.All(items, item => Assert.DoesNotContain("SourceId", JsonSerializer.Serialize(item)));
            Assert.Equal(new[] { "/sonarr/api/v3/calendar", "/anime/api/v3/calendar" }, m_handler.Requests.Select(x => x.Path));
            Assert.Equal(new[] { "first-key", "second-key" }, m_handler.Requests.Select(x => x.ApiKey));
            Assert.All(m_handler.Requests, request => Assert.Contains("includeSeries=true", request.Query));
        }

        [Theory]
        [InlineData("http")]
        [InlineData("json")]
        [InlineData("network")]
        [InlineData("timeout")]
        public async Task FailedInstanceDoesNotDiscardOtherResults(string failure)
        {
            m_config.Radarr.Instances = CreateInstances();
            m_handler.Response = request =>
            {
                if (request.RequestUri!.Host == "first.test")
                {
                    return failure switch
                    {
                        "http" => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
                        "json" => JsonResponse("invalid json"),
                        "network" => throw new HttpRequestException("Offline"),
                        _ => throw new TaskCanceledException("Timed out")
                    };
                }
                return JsonResponse("""[{"id":2,"tmdbId":42}]""");
            };

            RadarrCalendarDto[]? items = await m_service.GetArrCalendarAsync<RadarrCalendarDto>(
                ArrServiceType.Radarr, DateTime.UtcNow, DateTime.UtcNow.AddMonths(3));

            Assert.Equal(42, Assert.Single(items!).TmdbId);
            Assert.Equal(2, m_handler.Requests.Count);
        }

        [Fact]
        public async Task InvalidAndIncompleteInstancesDoNotBlockValidInstance()
        {
            m_config.Radarr.Instances =
            [
                new ArrInstanceConfig { Url = "file:///invalid", ApiKey = "invalid" },
                new ArrInstanceConfig { Url = "http://incomplete.test" },
                CreateInstances()[1]
            ];
            m_handler.Response = request => JsonResponse("[]");
            Assert.Empty((await m_service.GetArrCalendarAsync<RadarrCalendarDto>(
                ArrServiceType.Radarr, DateTime.UtcNow, DateTime.UtcNow.AddMonths(3)))!);
            Assert.Single(m_handler.Requests);
        }

        [Fact]
        public async Task ExplicitEmptyCollectionMakesNoRequests()
        {
            m_config.Sonarr.Url = "http://legacy.test";
            m_config.Sonarr.ApiKey = "legacy-key";
            m_config.Sonarr.Instances = [];
            Assert.Null(await m_service.GetArrCalendarAsync<SonarrCalendarDto>(
                ArrServiceType.Sonarr, DateTime.UtcNow, DateTime.UtcNow.AddDays(7)));
            Assert.Empty(m_handler.Requests);
        }

        [Fact]
        public void UpcomingMoviesDeduplicateBeforeApplyingLimit()
        {
            m_config.Radarr.Instances = CreateInstances();
            m_handler.Response = request => JsonResponse(JsonSerializer.Serialize(
                Enumerable.Range(1, 20).Select(id => new
                {
                    id,
                    tmdbId = id,
                    title = $"Movie {id}",
                    monitored = true,
                    hasFile = false,
                    digitalRelease = DateTime.UtcNow.AddDays(id)
                })));
            UpcomingMoviesSection section = CreateMoviesSection();
            MediaBrowser.Model.Dto.BaseItemDto[] items = section.GetResults(new HomeScreenSectionPayload(), new QueryCollection()).Items.ToArray();
            Assert.Equal(16, items.Length);
            Assert.Equal(16, items.Select(x => x.Name).Distinct().Count());
            Assert.Equal("Movie 1", items[0].Name);
        }

        [Fact]
        public void UpcomingMoviesPreserveCollidingLocalIdsWithoutProviderIds()
        {
            m_config.Radarr.Instances = CreateInstances();
            m_handler.Response = request => JsonResponse(JsonSerializer.Serialize(new[]
            {
                new { id = 1, title = request.RequestUri!.Host, monitored = true, digitalRelease = DateTime.UtcNow.AddDays(1) }
            }));
            MediaBrowser.Model.Dto.BaseItemDto[] items = CreateMoviesSection().GetResults(new HomeScreenSectionPayload(), new QueryCollection()).Items.ToArray();
            Assert.Equal(2, items.Length);
            Assert.NotEqual(items[0].UserData!.Key, items[1].UserData!.Key);
        }

        [Fact]
        public void UpcomingMoviesUseImdbWhenTmdbIsMissing()
        {
            m_config.Radarr.Instances = CreateInstances();
            m_handler.Response = request => JsonResponse(JsonSerializer.Serialize(new[]
            {
                new { id = request.RequestUri!.Host == "first.test" ? 1 : 9, imdbId = "tt12345", monitored = true, digitalRelease = DateTime.UtcNow.AddDays(1) }
            }));
            Assert.Single(CreateMoviesSection().GetResults(new HomeScreenSectionPayload(), new QueryCollection()).Items);
        }

        [Fact]
        public void UpcomingShowsDeduplicateEpisodesUsingSeriesTvdbId()
        {
            m_config.Sonarr.Instances = CreateInstances();
            m_handler.Response = request => JsonResponse(JsonSerializer.Serialize(new[]
            {
                new
                {
                    id = request.RequestUri!.Host == "first.test" ? 1 : 9,
                    seriesId = request.RequestUri.Host == "first.test" ? 3 : 7,
                    seasonNumber = 1,
                    episodeNumber = 2,
                    airDateUtc = DateTime.UtcNow.AddDays(1),
                    monitored = true,
                    series = new { tvdbId = 123, title = "Same series" }
                }
            }));
            Assert.Single(CreateShowsSection().GetResults(new HomeScreenSectionPayload(), new QueryCollection()).Items);
        }

        [Fact]
        public void UpcomingShowsPreserveDifferentEpisodesAndSeries()
        {
            m_config.Sonarr.Instances = CreateInstances();
            m_handler.Response = request => JsonResponse(JsonSerializer.Serialize(new[]
            {
                new { id = 1, seriesId = 1, seasonNumber = 1, episodeNumber = 1, monitored = true, airDateUtc = DateTime.UtcNow.AddDays(1), series = new { tvdbId = request.RequestUri!.Host == "first.test" ? 123 : 456, title = "Series" } },
                new { id = 2, seriesId = 1, seasonNumber = 1, episodeNumber = 2, monitored = true, airDateUtc = DateTime.UtcNow.AddDays(2), series = new { tvdbId = request.RequestUri.Host == "first.test" ? 123 : 456, title = "Series" } }
            }));
            MediaBrowser.Model.Dto.BaseItemDto[] items = CreateShowsSection().GetResults(new HomeScreenSectionPayload(), new QueryCollection()).Items.ToArray();
            Assert.Equal(4, items.Length);
            Assert.Equal(4, items.Select(x => x.UserData!.Key).Distinct().Count());
        }

        private UpcomingMoviesSection CreateMoviesSection()
        {
            return new UpcomingMoviesSection(Mock.Of<IUserManager>(), Mock.Of<ILibraryManager>(), Mock.Of<IDtoService>(),
                m_service, m_imageCache, NullLogger<UpcomingMoviesSection>.Instance);
        }

        private UpcomingShowsSection CreateShowsSection()
        {
            return new UpcomingShowsSection(Mock.Of<IUserManager>(), Mock.Of<ILibraryManager>(), Mock.Of<IDtoService>(),
                m_service, m_imageCache, NullLogger<UpcomingShowsSection>.Instance);
        }

        private static ArrInstanceConfig[] CreateInstances()
        {
            return
            [
                new ArrInstanceConfig { Name = "Standard", Url = "http://first.test/sonarr/", ApiKey = "first-key" },
                new ArrInstanceConfig { Name = "Anime", Url = "http://second.test/anime", ApiKey = "second-key" }
            ];
        }

        private static HttpResponseMessage JsonResponse(string json)
        {
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
        }

        private static ArrConfig ReadXml(string xml)
        {
            using StringReader reader = new StringReader(xml);
            return (ArrConfig)new XmlSerializer(typeof(ArrConfig)).Deserialize(reader)!;
        }

        private static ArrConfig RoundTripXml(ArrConfig config)
        {
            using StringWriter writer = new StringWriter();
            new XmlSerializer(typeof(ArrConfig)).Serialize(writer, config);
            return ReadXml(writer.ToString());
        }

        public void Dispose()
        {
            m_httpClient.Dispose();
            m_imageHttpClient.Dispose();
            string fullPath = Path.GetFullPath(m_testDirectory);
            if (fullPath.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(fullPath).StartsWith("hss-tests-", StringComparison.Ordinal) && Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, true);
            }
        }

        private sealed class CalendarHandler : HttpMessageHandler
        {
            public Func<HttpRequestMessage, HttpResponseMessage> Response { get; set; } =
                request => new HttpResponseMessage(HttpStatusCode.NotFound);

            public List<(string Path, string Query, string ApiKey)> Requests { get; } = new List<(string Path, string Query, string ApiKey)>();

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requests.Add((request.RequestUri!.AbsolutePath, request.RequestUri.Query,
                    request.Headers.TryGetValues("X-API-KEY", out IEnumerable<string>? keys) ? keys.Single() : ""));
                return Task.FromResult(Response(request));
            }
        }
    }
}
