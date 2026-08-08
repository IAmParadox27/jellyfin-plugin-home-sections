using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.RegularExpressions;
using Jellyfin.Extensions;
using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Helpers;
using Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using Jellyfin.Plugin.HomeScreenSections.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.HomeScreenSections.Controllers
{
    /// <summary>
    /// API controller for the Modular Home Screen.
    /// </summary>
    [ApiController]
    [Route("[controller]")]
    public class HomeScreenController : ControllerBase
    {
        private readonly IHomeScreenManager m_homeScreenManager;
        private readonly IDisplayPreferencesManager m_displayPreferencesManager;
        private readonly IServerApplicationHost m_serverApplicationHost;
        private readonly IApplicationPaths m_applicationPaths;
        private readonly HomeScreenSectionService m_homeScreenSectionService;
        private readonly ImageCacheService m_imageCacheService;

        public HomeScreenController(
            IHomeScreenManager homeScreenManager,
            IDisplayPreferencesManager displayPreferencesManager,
            IServerApplicationHost serverApplicationHost, 
            IApplicationPaths applicationPaths,
            HomeScreenSectionService homeScreenSectionService,
            ImageCacheService imageCacheService)
        {
            m_homeScreenManager = homeScreenManager;
            m_displayPreferencesManager = displayPreferencesManager;
            m_serverApplicationHost = serverApplicationHost;
            m_applicationPaths = applicationPaths;
            m_homeScreenSectionService = homeScreenSectionService;
            m_imageCacheService = imageCacheService;
        }

        /// <summary>
        /// Sets appropriate cache headers based on developer mode and cache bust counter.
        /// </summary>
        private void SetCacheHeaders()
        {
            var config = HomeScreenSectionsPlugin.Instance.Configuration;

            if (config.DeveloperMode)
            {
                // Developer mode: Force immediate cache invalidation
                Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
                Response.Headers["Pragma"] = "no-cache";
                Response.Headers["Expires"] = "0";
            }
            else
            {
                // Normal mode: Use configured cache timeout
                Response.Headers["Cache-Control"] = $"public, max-age={config.CacheTimeoutSeconds}";
            }

            Response.Headers["ETag"] = $"\"v{HomeScreenSectionsPlugin.Instance.Version}-c{config.CacheBustCounter}\"";
        }

        [HttpGet("home-screen-sections.js")]
        [Produces("application/javascript")]
        public ActionResult GetPluginScript()
        {
            Stream? stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(typeof(HomeScreenSectionsPlugin).Namespace +
                                           ".Inject.HomeScreenSections.js");

            if (stream == null)
            {
                return NotFound();
            }
            
            SetCacheHeaders();

            return File(stream, "application/javascript");
        }

        [HttpGet("home-screen-sections.css")]
        [Produces("text/css")]
        public ActionResult GetPluginStylesheet()
        {
            Stream? stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(typeof(HomeScreenSectionsPlugin).Namespace +
                                           ".Inject.HomeScreenSections.css");

            if (stream == null)
            {
                return NotFound();
            }
            
            SetCacheHeaders();

            return File(stream, "text/css");
        }

        [HttpGet("Configuration")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [Authorize(Roles = "Administrator")]
        public static ActionResult<PluginConfiguration> GetHomeScreenConfiguration()
        {
            return HomeScreenSectionsPlugin.Instance.Configuration;
        }
        
        [HttpPost("BustCache")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [Authorize(Roles = "Administrator")]
        public ActionResult BustCache()
        {
            HomeScreenSectionsPlugin.Instance.BustCache();
            var newCounter = HomeScreenSectionsPlugin.Instance.Configuration.CacheBustCounter;
            return Ok(new { newCounter });
        }

        /// <summary>
        /// Configuration health hints for empty/missing home sections (no user content scanning).
        /// </summary>
        [HttpGet("Diagnostics")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [Authorize(Roles = "Administrator")]
        public ActionResult GetDiagnostics()
        {
            PluginConfiguration config = HomeScreenSectionsPlugin.Instance.Configuration;
            List<object> checks = [];
            AppendPluginAndSectionChecks(config, checks);
            AppendIntegrationChecks(config, checks);
            AppendLibraryChecks(config, checks);
            checks.Add(new
            {
                id = "registered-types",
                severity = "info",
                message = $"{m_homeScreenManager.GetSectionTypes().Count()} section types are registered (built-in + plugins)."
            });

            return Ok(new
            {
                generatedAt = DateTime.UtcNow,
                pluginEnabled = config.Enabled,
                checks
            });
        }

        private static void AppendPluginAndSectionChecks(PluginConfiguration config, List<object> checks)
        {
            if (!config.Enabled)
            {
                checks.Add(new { id = "plugin-disabled", severity = "warning", message = "Home Screen Sections is disabled globally." });
            }

            if (config.SectionSettings == null || config.SectionSettings.Length == 0)
            {
                checks.Add(new
                {
                    id = "no-section-settings",
                    severity = "info",
                    message = "No section settings are stored yet; defaults will be used until you save the Section Settings tab."
                });
                return;
            }

            int enabledCount = config.SectionSettings.Count(s => s.Enabled);
            if (enabledCount == 0)
            {
                checks.Add(new { id = "all-sections-disabled", severity = "warning", message = "All configured sections are disabled in admin settings." });
            }

            checks.Add(new
            {
                id = "enabled-count",
                severity = "info",
                message = $"{enabledCount} of {config.SectionSettings.Length} configured sections are enabled."
            });
        }

        private static void AppendIntegrationChecks(PluginConfiguration config, List<object> checks)
        {
            if (string.IsNullOrWhiteSpace(config.Sonarr?.Url) || string.IsNullOrWhiteSpace(config.Sonarr?.ApiKey))
            {
                checks.Add(new { id = "sonarr", severity = "info", message = "Sonarr URL/API key not configured — Upcoming Shows will be empty." });
            }
            if (string.IsNullOrWhiteSpace(config.Radarr?.Url) || string.IsNullOrWhiteSpace(config.Radarr?.ApiKey))
            {
                checks.Add(new { id = "radarr", severity = "info", message = "Radarr URL/API key not configured — Upcoming Movies will be empty." });
            }
            if (string.IsNullOrWhiteSpace(config.Lidarr?.Url) || string.IsNullOrWhiteSpace(config.Lidarr?.ApiKey))
            {
                checks.Add(new { id = "lidarr", severity = "info", message = "Lidarr URL/API key not configured — Upcoming Music will be empty." });
            }
            if (string.IsNullOrWhiteSpace(config.Readarr?.Url) || string.IsNullOrWhiteSpace(config.Readarr?.ApiKey))
            {
                checks.Add(new { id = "readarr", severity = "info", message = "Readarr URL/API key not configured — Upcoming Books will be empty." });
            }
            if (string.IsNullOrWhiteSpace(config.JellyseerrUrl) || string.IsNullOrWhiteSpace(config.JellyseerrApiKey))
            {
                checks.Add(new { id = "jellyseerr", severity = "info", message = "Jellyseerr not configured — Discover / My Requests sections will be empty." });
            }
        }

        private static void AppendLibraryChecks(PluginConfiguration config, List<object> checks)
        {
            if (string.IsNullOrWhiteSpace(config.DefaultMoviesLibraryId))
            {
                checks.Add(new
                {
                    id = "movies-library",
                    severity = "info",
                    message = "No default movies library selected — movie section navigation may use the first available library."
                });
            }
            if (string.IsNullOrWhiteSpace(config.DefaultTVShowsLibraryId))
            {
                checks.Add(new
                {
                    id = "tv-library",
                    severity = "info",
                    message = "No default TV shows library selected — TV section navigation may use the first available library."
                });
            }
        }

        [HttpGet("CachedImage/{cacheKey}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public ActionResult GetCachedImage([FromRoute] string cacheKey)
        {
            (byte[]? data, string? contentType) = m_imageCacheService.GetCachedImage(cacheKey);
            var config = HomeScreenSectionsPlugin.Instance.Configuration;

            if (data == null || contentType == null)
            {
                return NotFound();
            }
            if (config.DeveloperMode)
            {
                Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
            }
            else
            {
                Response.Headers.CacheControl = $"public, max-age={config.CacheTimeoutSeconds}";
            }
            return File(data, contentType);
        }

        [HttpPost("ClearImageCache")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [Authorize(Roles = "Administrator")]
        public ActionResult ClearImageCache([FromQuery] bool clearAll = false)
        {
            if (clearAll)
            {
                m_imageCacheService.ClearAllCache();
                return Ok(new { message = "All cached images cleared" });
            }

            m_imageCacheService.ClearExpiredCache();
            return Ok(new { message = "Expired cached images cleared" });
        }

        [HttpGet("Meta")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [Authorize]
        public ActionResult<object> GetUserMeta()
        {
            var cfg = HomeScreenSectionsPlugin.Instance?.Configuration;
            if (cfg == null)
            {
                return Ok(new { Enabled = false, AllowUserOverride = false });
            }

            return Ok(new
            {
                Enabled = cfg.Enabled, 
                AllowUserOverride = cfg.AllowUserOverride, 
                PaginationEnabled = cfg.LazyLoadEnabled, 
                NumResultsPerPage = cfg.NumSectionsPerPage
            });
        }

        [HttpGet("Ready")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public ActionResult GetReady()
        {
            // Check plugin initialization
            if (HomeScreenSectionsPlugin.Instance?.Configuration == null)
            {
                return StatusCode(503, "Plugin not initialized");
            }

            // Check HomeScreenManager availability
            if (m_homeScreenManager == null)
            {
                return StatusCode(503, "HomeScreenManager not available");
            }

            // Check section types are registered
            var sectionTypes = m_homeScreenManager.GetSectionTypes();
            if (!sectionTypes.Any())
            {
                return StatusCode(503, "No section types registered");
            }

            // All good - ready for external registrations
            return Ok();
        }

        [HttpGet("Sections")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [Authorize]
        public ActionResult<QueryResult<HomeScreenSectionInfo>> GetHomeScreenSections(
            [FromQuery] Guid? userId,
            [FromQuery] string? language,
            [FromQuery] int? page = null,
            [FromQuery] int? numResultsPerPage = null,
            [FromQuery] Guid? pageHash = null)
        {
            IReadOnlyList<HomeScreenSectionInfo> sections = m_homeScreenSectionService.MonitorLiveUpdatedSectionsForUser(userId ?? Guid.Empty, language, 
                page ?? 1, numResultsPerPage, pageHash) ?? [];

            return new QueryResult<HomeScreenSectionInfo>(
                0,
                sections.Count,
                sections);
        }

        [HttpGet("Section/{sectionType}")]
        [Authorize]
        public QueryResult<BaseItemDto> GetSectionContent(
            [FromRoute] string sectionType,
            [FromQuery, Required] Guid userId,
            [FromQuery] string? additionalData,
            [FromQuery] string? language)
        {
            HomeScreenSectionPayload payload = new HomeScreenSectionPayload
            {
                UserId = userId,
                AdditionalData = additionalData
            };

            return m_homeScreenManager.InvokeResultsDelegate(sectionType, payload, Request.Query);
        }

        [HttpPost("RegisterSection")]
        public ActionResult RegisterSection([FromBody] SectionRegisterPayload payload)
        {
            m_homeScreenManager.RegisterResultsDelegate(new PluginDefinedSection(payload.Id, payload.DisplayText!, payload.Route, payload.AdditionalData)
            {
                OnGetResults = sectionPayload =>
                {
                    JObject jsonPayload = JObject.FromObject(sectionPayload);

                    string? publishedServerUrl = m_serverApplicationHost.GetType()
                        .GetProperty("PublishedServerUrl", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(m_serverApplicationHost) as string;

                    HttpClient client = HomeScreenSectionsPlugin.Instance.ServiceProvider.GetService<IHttpClientFactory>()?.CreateClient() ?? new HttpClient();
                    client.BaseAddress = new Uri(publishedServerUrl ?? $"http://localhost:{m_serverApplicationHost.HttpPort}");
                    
                    HttpResponseMessage responseMessage = client.PostAsync(payload.ResultsEndpoint, 
                        new StringContent(jsonPayload.ToString(Formatting.None), MediaTypeHeaderValue.Parse("application/json"))).GetAwaiter().GetResult();

                    return JsonConvert.DeserializeObject<QueryResult<BaseItemDto>>(responseMessage.Content.ReadAsStringAsync().GetAwaiter().GetResult()) ?? new QueryResult<BaseItemDto>();
                }
            });
            
            return Ok();
        }

        [HttpPost("DiscoverRequest")]
        [Authorize]
        public async Task<ActionResult> MakeDiscoverRequest([FromServices] IUserManager userManager, [FromBody] DiscoverRequestPayload payload)
        {
            string? userIdString = User.Claims.FirstOrDefault(x => x.Type.Equals("Jellyfin-UserId", StringComparison.OrdinalIgnoreCase))?.Value;
            Guid userId = string.IsNullOrEmpty(userIdString) ? Guid.Empty : Guid.Parse(userIdString);

            if (userId == Guid.Empty)
            {
                return Forbid();
            }
            
            User? user = userManager.GetUserById(userId);
            if (user == null)
            {
                return BadRequest();
            }

            string? jellyseerrUrl = HomeScreenSectionsPlugin.Instance.Configuration.JellyseerrUrl;

            if (jellyseerrUrl == null)
            {
                return BadRequest();
            }

            HttpClient client = HomeScreenSectionsPlugin.Instance.ServiceProvider.GetService<IHttpClientFactory>()?.CreateClient() ?? new HttpClient();
            client.BaseAddress = new Uri(jellyseerrUrl);
            client.DefaultRequestHeaders.Add("X-Api-Key", HomeScreenSectionsPlugin.Instance.Configuration.JellyseerrApiKey);
            
            HttpResponseMessage usersResponse = client.GetAsync($"/api/v1/user?q={Uri.EscapeDataString(user.Username)}").GetAwaiter().GetResult();
            string userResponseRaw = usersResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            int? jellyseerrUserId = JObject.Parse(userResponseRaw).Value<JArray>("results")!.OfType<JObject>().FirstOrDefault(x => string.Equals(x.Value<string>("jellyfinUsername"), user.Username, StringComparison.Ordinal))?.Value<int>("id");

            if (jellyseerrUserId == null)
            {
                return BadRequest();
            }
            
            client.DefaultRequestHeaders.Add("X-Api-User", jellyseerrUserId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));

            HttpResponseMessage requestResponse;
            if (string.Equals(payload.MediaType, "tv", StringComparison.Ordinal))
            {
                requestResponse = await client.PostAsync("/api/v1/request", JsonContent.Create(new JellyseerrTvShowRequestPayload
                {
                    MediaId = payload.MediaId,
                    MediaType = payload.MediaType,
                    Seasons = "all"
                }));
            }
            else
            {
                requestResponse = await client.PostAsync("/api/v1/request", JsonContent.Create(new JellyseerrRequestPayload
                {
                    MediaId = payload.MediaId,
                    MediaType = payload.MediaType
                }));
            }
            
            string responseContent = await requestResponse.Content.ReadAsStringAsync();
            string contentType = requestResponse.Content.Headers.ContentType?.MediaType ?? "application/json";
            
            return Content(responseContent, contentType);
        }
    }
}
