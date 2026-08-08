using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Controllers;
using Jellyfin.Plugin.HomeScreenSections.Data;
using Jellyfin.Plugin.HomeScreenSections.JellyfinVersionSpecific;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Services;
using Jellyfin.Plugin.HomeScreenSections.Tests.Support;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.HomeScreenSections.Tests.Controllers;

[Collection("Plugin Instance")]
public class HomeScreenControllerTests
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

    public HomeScreenControllerTests(PluginFixture fixture)
    {
        m_fixture = fixture;
    }

    private HomeScreenController MakeController()
    {
        HomeScreenSectionService sectionService = new HomeScreenSectionService(
            m_homeScreenManager.Object,
            NullLogger<HomeScreenSectionsPlugin>.Instance,
            m_fixture.TranslationManagerMock.Object,
            new UserSectionsDataCache(),
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

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        return controller;
    }

    [Fact]
    public void GetHomeScreenConfiguration_returns_the_live_plugin_configuration()
    {
        ActionResult<PluginConfiguration> result = HomeScreenController.GetHomeScreenConfiguration();

        Assert.Same(HomeScreenSectionsPlugin.Instance.Configuration, result.Value);
    }

    [Fact]
    public void GetPluginScript_serves_embedded_javascript_with_cache_headers()
    {
        HomeScreenController controller = MakeController();

        ActionResult result = controller.GetPluginScript();

        FileStreamResult fileResult = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("application/javascript", fileResult.ContentType);
        Assert.Equal(
            $"public, max-age={HomeScreenSectionsPlugin.Instance.Configuration.CacheTimeoutSeconds}",
            controller.HttpContext.Response.Headers.CacheControl.ToString());
        Assert.False(string.IsNullOrEmpty(controller.HttpContext.Response.Headers.ETag.ToString()));
    }

    [Fact]
    public void GetPluginStylesheet_serves_embedded_css()
    {
        HomeScreenController controller = MakeController();

        ActionResult result = controller.GetPluginStylesheet();

        FileStreamResult fileResult = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("text/css", fileResult.ContentType);
    }

    [Fact]
    public void GetPluginScript_disables_caching_in_developer_mode()
    {
        PluginConfiguration config = HomeScreenSectionsPlugin.Instance.Configuration;
        bool original = config.DeveloperMode;
        config.DeveloperMode = true;
        try
        {
            HomeScreenController controller = MakeController();

            controller.GetPluginScript();

            Assert.Equal("no-cache, no-store, must-revalidate", controller.HttpContext.Response.Headers.CacheControl.ToString());
        }
        finally
        {
            config.DeveloperMode = original;
        }
    }

    [Fact]
    public void GetDiagnostics_reports_defaults_and_missing_integrations()
    {
        PluginConfiguration config = HomeScreenSectionsPlugin.Instance.Configuration;
        SectionSettings[] original = config.SectionSettings;
        bool originalEnabled = config.Enabled;
        config.SectionSettings = [];
        config.Enabled = false;
        try
        {
            HomeScreenController controller = MakeController();
            m_homeScreenManager
                .Setup(manager => manager.GetSectionTypes())
                .Returns(Array.Empty<IHomeScreenSection>());

            ActionResult result = controller.GetDiagnostics();

            OkObjectResult ok = Assert.IsType<OkObjectResult>(result);
            JObject payload = JObject.FromObject(ok.Value!);
            Assert.False(payload.Value<bool>("pluginEnabled"));

            string[] ids = payload["checks"]!.Select(check => check.Value<string>("id")!).ToArray();
            Assert.Contains("plugin-disabled", ids, StringComparer.Ordinal);
            Assert.Contains("no-section-settings", ids, StringComparer.Ordinal);
            Assert.Contains("sonarr", ids, StringComparer.Ordinal);
            Assert.Contains("radarr", ids, StringComparer.Ordinal);
            Assert.Contains("lidarr", ids, StringComparer.Ordinal);
            Assert.Contains("readarr", ids, StringComparer.Ordinal);
            Assert.Contains("jellyseerr", ids, StringComparer.Ordinal);
            Assert.Contains("movies-library", ids, StringComparer.Ordinal);
            Assert.Contains("tv-library", ids, StringComparer.Ordinal);
            Assert.Contains("registered-types", ids, StringComparer.Ordinal);
        }
        finally
        {
            config.SectionSettings = original;
            config.Enabled = originalEnabled;
        }
    }

    [Fact]
    public void GetDiagnostics_counts_enabled_sections_and_configured_services()
    {
        PluginConfiguration config = HomeScreenSectionsPlugin.Instance.Configuration;
        SectionSettings[] original = config.SectionSettings;
        bool originalEnabled = config.Enabled;
        string originalRadarrUrl = config.Radarr.Url ?? string.Empty;
        string originalRadarrKey = config.Radarr.ApiKey ?? string.Empty;
        config.Enabled = true;
        config.Radarr.Url = "http://radarr.test";
        config.Radarr.ApiKey = "key";
        config.SectionSettings =
        [
            new SectionSettings { SectionId = "On", Enabled = true },
            new SectionSettings { SectionId = "Off", Enabled = false }
        ];
        try
        {
            HomeScreenController controller = MakeController();
            m_homeScreenManager
                .Setup(manager => manager.GetSectionTypes())
                .Returns(Array.Empty<IHomeScreenSection>());

            ActionResult result = controller.GetDiagnostics();

            OkObjectResult ok = Assert.IsType<OkObjectResult>(result);
            JObject payload = JObject.FromObject(ok.Value!);
            Assert.True(payload.Value<bool>("pluginEnabled"));

            string[] ids = payload["checks"]!.Select(check => check.Value<string>("id")!).ToArray();
            Assert.DoesNotContain("plugin-disabled", ids, StringComparer.Ordinal);
            Assert.DoesNotContain("radarr", ids, StringComparer.Ordinal);
            Assert.Contains("enabled-count", ids, StringComparer.Ordinal);

            JObject enabledCount = payload["checks"]!
                .First(check => string.Equals(check.Value<string>("id"), "enabled-count", StringComparison.Ordinal))
                .Value<JObject>()!;
            Assert.Contains("1 of 2", enabledCount.Value<string>("message"), StringComparison.Ordinal);
        }
        finally
        {
            config.SectionSettings = original;
            config.Enabled = originalEnabled;
            config.Radarr.Url = originalRadarrUrl;
            config.Radarr.ApiKey = originalRadarrKey;
        }
    }

    [Fact]
    public void GetUserMeta_exposes_plugin_flags()
    {
        HomeScreenController controller = MakeController();

        ActionResult<object> result = controller.GetUserMeta();

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
        JObject meta = JObject.FromObject(ok.Value!);
        Assert.Equal(HomeScreenSectionsPlugin.Instance.Configuration.Enabled, meta.Value<bool>("Enabled"));
        Assert.Equal(HomeScreenSectionsPlugin.Instance.Configuration.AllowUserOverride, meta.Value<bool>("AllowUserOverride"));
        Assert.Equal(HomeScreenSectionsPlugin.Instance.Configuration.NumSectionsPerPage, meta.Value<int>("NumResultsPerPage"));
    }

    [Fact]
    public void GetCachedImage_returns_not_found_for_unknown_key()
    {
        HomeScreenController controller = MakeController();

        ActionResult result = controller.GetCachedImage("unknown-key");

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public void ClearImageCache_reports_scope_in_message()
    {
        HomeScreenController controller = MakeController();

        ActionResult expired = controller.ClearImageCache(clearAll: false);
        ActionResult all = controller.ClearImageCache(clearAll: true);

        OkObjectResult expiredOk = Assert.IsType<OkObjectResult>(expired);
        OkObjectResult allOk = Assert.IsType<OkObjectResult>(all);
        Assert.Contains("Expired", JObject.FromObject(expiredOk.Value!).Value<string>("message"), StringComparison.Ordinal);
        Assert.Contains("All", JObject.FromObject(allOk.Value!).Value<string>("message"), StringComparison.Ordinal);
    }

    [Fact]
    public void BustCache_returns_the_new_counter()
    {
        HomeScreenController controller = MakeController();
        int before = HomeScreenSectionsPlugin.Instance.Configuration.CacheBustCounter;

        ActionResult result = controller.BustCache();

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result);
        JObject payload = JObject.FromObject(ok.Value!);
        Assert.Equal(before + 1, payload.Value<int>("newCounter"));
        Assert.Equal(before + 1, HomeScreenSectionsPlugin.Instance.Configuration.CacheBustCounter);
    }

    [Fact]
    public void AppendIntegrationChecks_reports_only_unconfigured_services()
    {
        PluginConfiguration empty = new PluginConfiguration();
        List<object> checks = [];
        InvokeControllerStatic("AppendIntegrationChecks", empty, checks);
        Assert.Equal(5, checks.Count);

        PluginConfiguration configured = new PluginConfiguration
        {
            Sonarr = new ArrConfig { Url = "http://sonarr", ApiKey = "key" },
            Radarr = new ArrConfig { Url = "http://radarr", ApiKey = "key" },
            Lidarr = new ArrConfig { Url = "http://lidarr", ApiKey = "key" },
            Readarr = new ArrConfig { Url = "http://readarr", ApiKey = "key" },
            JellyseerrUrl = "http://jellyseerr",
            JellyseerrApiKey = "key"
        };
        List<object> noneChecks = [];
        InvokeControllerStatic("AppendIntegrationChecks", configured, noneChecks);
        Assert.Empty(noneChecks);
    }

    [Fact]
    public void AppendLibraryChecks_reports_missing_default_libraries()
    {
        PluginConfiguration config = new PluginConfiguration();
        List<object> checks = [];
        InvokeControllerStatic("AppendLibraryChecks", config, checks);
        Assert.Equal(2, checks.Count);

        config.DefaultMoviesLibraryId = "movies-id";
        config.DefaultTVShowsLibraryId = "tv-id";
        List<object> noneChecks = [];
        InvokeControllerStatic("AppendLibraryChecks", config, noneChecks);
        Assert.Empty(noneChecks);
    }

    [Fact]
    public void AppendPluginAndSectionChecks_covers_enabled_and_disabled_paths()
    {
        // No settings stored yet -> single no-section-settings notice.
        PluginConfiguration noSettings = new PluginConfiguration { Enabled = true, SectionSettings = [] };
        List<object> checks = [];
        InvokeControllerStatic("AppendPluginAndSectionChecks", noSettings, checks);
        Assert.Equal("no-section-settings", GetCheckId(Assert.Single(checks)));

        // All configured sections disabled.
        PluginConfiguration allDisabled = new PluginConfiguration
        {
            Enabled = true,
            SectionSettings = [new SectionSettings { SectionId = "A", Enabled = false }]
        };
        List<object> disabledChecks = [];
        InvokeControllerStatic("AppendPluginAndSectionChecks", allDisabled, disabledChecks);
        Assert.Equal(2, disabledChecks.Count);
        Assert.Contains(disabledChecks, c => string.Equals(GetCheckId(c), "all-sections-disabled", StringComparison.Ordinal));

        // Plugin disabled adds its own warning.
        PluginConfiguration pluginOff = new PluginConfiguration
        {
            Enabled = false,
            SectionSettings = [new SectionSettings { SectionId = "A", Enabled = true }]
        };
        List<object> offChecks = [];
        InvokeControllerStatic("AppendPluginAndSectionChecks", pluginOff, offChecks);
        Assert.Contains(offChecks, c => string.Equals(GetCheckId(c), "plugin-disabled", StringComparison.Ordinal));
    }

    private static object? InvokeControllerStatic(string name, params object?[] args)
    {
        System.Reflection.MethodInfo method = typeof(HomeScreenController)
            .GetMethod(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new InvalidOperationException($"Private static '{name}' not found on {nameof(HomeScreenController)}.");
        return method.Invoke(null, args);
    }

    private static string GetCheckId(object anonymousCheck)
    {
        return JObject.FromObject(anonymousCheck).Value<string>("id")!;
    }
}
