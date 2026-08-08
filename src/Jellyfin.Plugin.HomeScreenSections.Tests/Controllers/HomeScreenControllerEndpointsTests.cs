using System.Security.Claims;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Controllers;
using Jellyfin.Plugin.HomeScreenSections.Data;
using Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections;
using Jellyfin.Plugin.HomeScreenSections.JellyfinVersionSpecific;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using Jellyfin.Plugin.HomeScreenSections.Services;
using Jellyfin.Plugin.HomeScreenSections.Tests.Support;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json;

namespace Jellyfin.Plugin.HomeScreenSections.Tests.Controllers;

[Collection("Plugin Instance")]
public class HomeScreenControllerEndpointsTests : IDisposable
{
    private readonly PluginFixture m_fixture;
    private readonly Mock<IHomeScreenManager> m_homeScreenManager = new();
    private readonly Mock<IDisplayPreferencesManager> m_displayPreferencesManager = new();
    private readonly Mock<IServerApplicationHost> m_serverApplicationHost = new();
    private readonly Mock<IServerConfigurationManager> m_serverConfigurationManager = new();
    private readonly Mock<IUserManager> m_userManager = new();
    private readonly Mock<ILibraryManager> m_libraryManager = new();
    private readonly Mock<IDtoService> m_dtoService = new();
    private readonly Mock<ICollectionManager> m_collectionManager = new();
    private readonly Mock<IPlaylistManager> m_playlistManager = new();
    private readonly UserSectionsDataCache m_dataCache = new();
    private readonly JellyseerrFakeServer m_server;

    private string? m_originalJellyseerrUrl;
    private string? m_originalApiKey;

    public HomeScreenControllerEndpointsTests(PluginFixture fixture)
    {
        m_fixture = fixture;
        m_server = JellyseerrFakeServer.Start(Respond);

        m_serverConfigurationManager
            .Setup(manager => manager.Configuration)
            .Returns(new MediaBrowser.Model.Configuration.ServerConfiguration());

        PluginConfiguration config = HomeScreenSectionsPlugin.Instance.Configuration;
        m_originalJellyseerrUrl = config.JellyseerrUrl;
        m_originalApiKey = config.JellyseerrApiKey;
        config.JellyseerrUrl = m_server.BaseUrl;
        config.JellyseerrApiKey = "test-key";
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        PluginConfiguration config = HomeScreenSectionsPlugin.Instance.Configuration;
        config.JellyseerrUrl = m_originalJellyseerrUrl;
        config.JellyseerrApiKey = m_originalApiKey;
        m_server.Dispose();
    }

    private static (int StatusCode, string Json) Respond(string pathAndQuery)
    {
        if (pathAndQuery.StartsWith("/api/v1/user?", StringComparison.Ordinal))
        {
            return (200, """{ "results": [ { "id": 12, "jellyfinUsername": "EndpointUser" } ] }""");
        }

        if (pathAndQuery.StartsWith("/api/v1/request", StringComparison.Ordinal))
        {
            return (201, """{ "id": 55, "status": 1 }""");
        }

        if (pathAndQuery.StartsWith("/sections/results", StringComparison.Ordinal))
        {
            QueryResult<BaseItemDto> result = new QueryResult<BaseItemDto>(
            [
                new BaseItemDto { Id = Guid.NewGuid(), Name = "Endpoint Section Item" }
            ]);
            return (200, JsonConvert.SerializeObject(result));
        }

        return (404, "{}");
    }

    private HomeScreenController MakeController(string? userIdClaim = null)
    {
        HomeScreenSectionService sectionService = new HomeScreenSectionService(
            m_homeScreenManager.Object,
            NullLogger<HomeScreenSectionsPlugin>.Instance,
            m_fixture.TranslationManagerMock.Object,
            m_dataCache,
            m_serverConfigurationManager.Object,
            m_userManager.Object,
            m_libraryManager.Object,
            m_dtoService.Object,
            new CollectionManagerProxy(m_collectionManager.Object),
            m_playlistManager.Object);

        ImageCacheService imageCacheService = new ImageCacheService(
            NullLogger<ImageCacheService>.Instance,
            m_fixture.Paths,
            new HttpClient(FakeHttpMessageHandler.RespondingWithStatus(System.Net.HttpStatusCode.NotFound)));

        HomeScreenController controller = new HomeScreenController(
            m_homeScreenManager.Object,
            m_displayPreferencesManager.Object,
            m_serverApplicationHost.Object,
            m_fixture.Paths,
            sectionService,
            imageCacheService);

        DefaultHttpContext httpContext = new DefaultHttpContext();
        if (userIdClaim != null)
        {
            ClaimsIdentity identity = new ClaimsIdentity(
            [
                new Claim("Jellyfin-UserId", userIdClaim)
            ]);
            httpContext.User = new ClaimsPrincipal(identity);
        }

        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    [Fact]
    public void GetReady_returns_503_when_no_sections_registered()
    {
        m_homeScreenManager
            .Setup(manager => manager.GetSectionTypes())
            .Returns(Array.Empty<IHomeScreenSection>());

        ActionResult result = MakeController().GetReady();

        ObjectResult status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(503, status.StatusCode);
    }

    [Fact]
    public void GetReady_returns_ok_when_sections_registered()
    {
        m_homeScreenManager
            .Setup(manager => manager.GetSectionTypes())
            .Returns(new IHomeScreenSection[]
            {
                new PluginDefinedSection("Ready", "Ready")
                {
                    OnGetResults = _ => new QueryResult<BaseItemDto>()
                }
            });

        ActionResult result = MakeController().GetReady();

        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public void GetHomeScreenSections_returns_live_updated_sections()
    {
        Guid userId = Guid.NewGuid();
        PluginConfiguration config = HomeScreenSectionsPlugin.Instance.Configuration;
        SectionSettings[] original = config.SectionSettings;
        config.SectionSettings =
        [
            new SectionSettings { SectionId = "EndpointSection", Enabled = true, OrderIndex = 0 }
        ];
        try
        {
            m_homeScreenManager
                .Setup(manager => manager.GetUserSettings(userId))
                .Returns(new ModularHomeUserSettings { UserId = userId, EnabledSections = ["EndpointSection"] });
            m_homeScreenManager
                .Setup(manager => manager.GetSectionTypes())
                .Returns(new IHomeScreenSection[]
                {
                    new PluginDefinedSection("EndpointSection", "Endpoint")
                    {
                        OnGetResults = _ => new QueryResult<BaseItemDto>()
                    }
                });
            m_fixture.TranslationManagerMock
                .Setup(manager => manager.Translate(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<TranslationMetadata?>()))
                .Returns((string key, string language, string fallback, TranslationMetadata? metadata) => fallback);

            HomeScreenController controller = MakeController();

            ActionResult<QueryResult<HomeScreenSectionInfo>> result =
                controller.GetHomeScreenSections(userId, "en", 1, 10, null);

            Assert.NotNull(result.Value);
            HomeScreenSectionInfo info = Assert.Single(result.Value!.Items);
            Assert.Equal("EndpointSection", info.Section);
        }
        finally
        {
            config.SectionSettings = original;
        }
    }

    [Fact]
    public void GetSectionContent_invokes_the_matching_results_delegate()
    {
        Guid userId = Guid.NewGuid();
        m_homeScreenManager
            .Setup(manager => manager.InvokeResultsDelegate(
                "NextUp",
                It.Is<HomeScreenSectionPayload>(payload => payload.UserId == userId),
                It.IsAny<IQueryCollection>()))
            .Returns(new QueryResult<BaseItemDto>([new BaseItemDto { Name = "Next Up Item" }]));

        HomeScreenController controller = MakeController();

        QueryResult<BaseItemDto> result = controller.GetSectionContent("NextUp", userId, null, "en");

        Assert.Equal("Next Up Item", Assert.Single(result.Items).Name);
    }

    [Fact]
    public void RegisterSection_registers_delegate_that_posts_to_endpoint()
    {
        int port = new Uri(m_server.BaseUrl).Port;
        m_serverApplicationHost
            .SetupGet(host => host.HttpPort)
            .Returns(port);

        PluginDefinedSection? captured = null;
        m_homeScreenManager
            .Setup(manager => manager.RegisterResultsDelegate(It.IsAny<PluginDefinedSection>()))
            .Callback<PluginDefinedSection>(section => captured = section);

        HomeScreenController controller = MakeController();

        ActionResult result = controller.RegisterSection(new SectionRegisterPayload
        {
            Id = "controller-endpoint-section",
            DisplayText = "Controller Endpoint",
            ResultsEndpoint = "/sections/results"
        });

        Assert.IsType<OkResult>(result);
        Assert.NotNull(captured);

        QueryResult<BaseItemDto> results = captured!.GetResults(
            new HomeScreenSectionPayload { UserId = Guid.NewGuid() },
            new FakeQueryCollection());

        Assert.Equal("Endpoint Section Item", Assert.Single(results.Items).Name);
    }

    [Fact]
    public async Task MakeDiscoverRequest_forbids_anonymous_callers()
    {
        HomeScreenController controller = MakeController(userIdClaim: null);

        ActionResult result = await controller.MakeDiscoverRequest(
            m_userManager.Object,
            new DiscoverRequestPayload { MediaType = "movie", MediaId = 5 });

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task MakeDiscoverRequest_returns_bad_request_for_unknown_user()
    {
        Guid userId = Guid.NewGuid();
        m_userManager
            .Setup(manager => manager.GetUserById(userId))
            .Returns((User?)null);

        HomeScreenController controller = MakeController(userIdClaim: userId.ToString());

        ActionResult result = await controller.MakeDiscoverRequest(
            m_userManager.Object,
            new DiscoverRequestPayload { MediaType = "movie", MediaId = 5 });

        Assert.IsType<BadRequestResult>(result);
    }

    [Theory]
    [InlineData("movie")]
    [InlineData("tv")]
    public async Task MakeDiscoverRequest_forwards_to_jellyseerr(string mediaType)
    {
        Guid userId = Guid.NewGuid();
        User user = new("EndpointUser", "AuthProvider", "PasswordResetProvider");
        m_userManager
            .Setup(manager => manager.GetUserById(userId))
            .Returns(user);

        HomeScreenController controller = MakeController(userIdClaim: userId.ToString());

        ActionResult result = await controller.MakeDiscoverRequest(
            m_userManager.Object,
            new DiscoverRequestPayload { MediaType = mediaType, MediaId = 42 });

        ContentResult content = Assert.IsType<ContentResult>(result);
        Assert.Contains("55", content.Content, StringComparison.Ordinal);
        Assert.Contains(m_server.RequestsReceived, request => request.StartsWith("/api/v1/request", StringComparison.Ordinal));
    }
}
