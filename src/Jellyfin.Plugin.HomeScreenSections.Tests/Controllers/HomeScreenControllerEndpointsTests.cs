using System.Reflection;
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
    private readonly PluginFixture _fixture;
    private readonly Mock<IHomeScreenManager> _homeScreenManager = new();
    private readonly Mock<IDisplayPreferencesManager> _displayPreferencesManager = new();
    private readonly Mock<IServerApplicationHost> _serverApplicationHost = new();
    private readonly Mock<IServerConfigurationManager> _serverConfigurationManager = new();
    private readonly Mock<IUserManager> _userManager = new();
    private readonly Mock<ILibraryManager> _libraryManager = new();
    private readonly Mock<IDtoService> _dtoService = new();
    private readonly Mock<ICollectionManager> _collectionManager = new();
    private readonly Mock<IPlaylistManager> _playlistManager = new();
    private readonly UserSectionsDataCache _dataCache = new();
    private readonly JellyseerrFakeServer _server;

    private string? _originalJellyseerrUrl;
    private string? _originalApiKey;

    public HomeScreenControllerEndpointsTests(PluginFixture fixture)
    {
        _fixture = fixture;
        _server = JellyseerrFakeServer.Start(Respond);

        _serverConfigurationManager
            .Setup(manager => manager.Configuration)
            .Returns(new MediaBrowser.Model.Configuration.ServerConfiguration());

        PluginConfiguration config = HomeScreenSectionsPlugin.Instance.Configuration;
        _originalJellyseerrUrl = config.JellyseerrUrl;
        _originalApiKey = config.JellyseerrApiKey;
        config.JellyseerrUrl = _server.BaseUrl;
        config.JellyseerrApiKey = "test-key";
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        PluginConfiguration config = HomeScreenSectionsPlugin.Instance.Configuration;
        config.JellyseerrUrl = _originalJellyseerrUrl;
        config.JellyseerrApiKey = _originalApiKey;
        _server.Dispose();
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
            _homeScreenManager.Object,
            NullLogger<HomeScreenSectionsPlugin>.Instance,
            _fixture.TranslationManagerMock.Object,
            _dataCache,
            _serverConfigurationManager.Object,
            _userManager.Object,
            _libraryManager.Object,
            _dtoService.Object,
            new CollectionManagerProxy(_collectionManager.Object),
            _playlistManager.Object);

        ImageCacheService imageCacheService = new ImageCacheService(
            NullLogger<ImageCacheService>.Instance,
            _fixture.Paths,
            new HttpClient(FakeHttpMessageHandler.RespondingWithStatus(System.Net.HttpStatusCode.NotFound)));

        HomeScreenController controller = new HomeScreenController(
            _homeScreenManager.Object,
            _displayPreferencesManager.Object,
            _serverApplicationHost.Object,
            _fixture.Paths,
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
        _homeScreenManager
            .Setup(manager => manager.GetSectionTypes())
            .Returns(Array.Empty<IHomeScreenSection>());

        ActionResult result = MakeController().GetReady();

        ObjectResult status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(503, status.StatusCode);
    }

    [Fact]
    public void GetReady_returns_ok_when_sections_registered()
    {
        _homeScreenManager
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
            _homeScreenManager
                .Setup(manager => manager.GetUserSettings(userId))
                .Returns(new ModularHomeUserSettings { UserId = userId, EnabledSections = ["EndpointSection"] });
            _homeScreenManager
                .Setup(manager => manager.GetSectionTypes())
                .Returns(new IHomeScreenSection[]
                {
                    new PluginDefinedSection("EndpointSection", "Endpoint")
                    {
                        OnGetResults = _ => new QueryResult<BaseItemDto>()
                    }
                });
            _fixture.TranslationManagerMock
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
        _homeScreenManager
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
        int port = new Uri(_server.BaseUrl).Port;
        _serverApplicationHost
            .SetupGet(host => host.HttpPort)
            .Returns(port);

        PluginDefinedSection? captured = null;
        _homeScreenManager
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
    public void RegisterSection_returns_conflict_when_section_id_already_registered()
    {
        // Regression for upstream #258: re-registering an existing section id must not
        // silently replace the handler (e.g. a built-in like ContinueWatching).
        _homeScreenManager
            .Setup(manager => manager.GetSection("NextUp"))
            .Returns(new PluginDefinedSection("NextUp", "Existing")
            {
                OnGetResults = _ => new QueryResult<BaseItemDto>()
            });

        HomeScreenController controller = MakeController();

        ActionResult result = controller.RegisterSection(new SectionRegisterPayload
        {
            Id = "NextUp",
            DisplayText = "Impostor",
            ResultsEndpoint = "/sections/results"
        });

        Assert.IsType<ConflictResult>(result);
        _homeScreenManager.Verify(
            manager => manager.RegisterResultsDelegate(It.IsAny<PluginDefinedSection>()),
            Times.Never());
    }

    [Fact]
    public void RegisterSection_requires_administrator_authorization()
    {
        // Regression for upstream #258: the endpoint used to be completely unauthenticated.
        MethodInfo method = typeof(HomeScreenController)
            .GetMethod(nameof(HomeScreenController.RegisterSection))
            ?? throw new InvalidOperationException("RegisterSection action not found.");

        Microsoft.AspNetCore.Authorization.AuthorizeAttribute? attribute = method
            .GetCustomAttribute<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal("Administrator", attribute!.Roles);
    }

    [Fact]
    public async Task MakeDiscoverRequest_forbids_anonymous_callers()
    {
        HomeScreenController controller = MakeController(userIdClaim: null);

        ActionResult result = await controller.MakeDiscoverRequest(
            _userManager.Object,
            new DiscoverRequestPayload { MediaType = "movie", MediaId = 5 });

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task MakeDiscoverRequest_returns_bad_request_for_unknown_user()
    {
        Guid userId = Guid.NewGuid();
        _userManager
            .Setup(manager => manager.GetUserById(userId))
            .Returns((User?)null);

        HomeScreenController controller = MakeController(userIdClaim: userId.ToString());

        ActionResult result = await controller.MakeDiscoverRequest(
            _userManager.Object,
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
        _userManager
            .Setup(manager => manager.GetUserById(userId))
            .Returns(user);

        HomeScreenController controller = MakeController(userIdClaim: userId.ToString());

        ActionResult result = await controller.MakeDiscoverRequest(
            _userManager.Object,
            new DiscoverRequestPayload { MediaType = mediaType, MediaId = 42 });

        ContentResult content = Assert.IsType<ContentResult>(result);
        Assert.Contains("55", content.Content, StringComparison.Ordinal);
        Assert.Contains(_server.RequestsReceived, request => request.StartsWith("/api/v1/request", StringComparison.Ordinal));
    }
}
