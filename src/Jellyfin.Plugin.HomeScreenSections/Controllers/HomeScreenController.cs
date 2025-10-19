using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.RegularExpressions;
using Jellyfin.Extensions;
using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Helpers;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model;
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

        public HomeScreenController(
            IHomeScreenManager homeScreenManager,
            IDisplayPreferencesManager displayPreferencesManager,
            IServerApplicationHost serverApplicationHost,
            IApplicationPaths applicationPaths,
            HomeScreenSectionService homeScreenSectionService)
        {
            m_homeScreenManager = homeScreenManager;
            m_displayPreferencesManager = displayPreferencesManager;
            m_serverApplicationHost = serverApplicationHost;
            m_applicationPaths = applicationPaths;
            m_homeScreenSectionService = homeScreenSectionService;
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

        [HttpGet("client/shared-utils.js")]
        [Produces("application/javascript")]
        public ActionResult GetSharedUtils()
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                string? resName = asm.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith("Configuration.shared-utils.js", StringComparison.OrdinalIgnoreCase));
                if (resName == null)
                {
                    return NotFound();
                }
                Stream? stream = asm.GetManifestResourceStream(resName);
                if (stream == null) return NotFound();

                SetCacheHeaders();

                return File(stream, "application/javascript");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Failed to load shared utils: {ex.Message}");
            }
        }

        [HttpGet("Configuration")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [Authorize(Roles = "Administrator")]
        public ActionResult<PluginConfiguration> GetHomeScreenConfiguration()
        {
            try
            {
                var config = HomeScreenSectionsPlugin.Instance.Configuration;
                return config;
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error loading configuration: {ex.Message}");
            }
        }

        [HttpPost("Configuration")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [Authorize(Roles = "Administrator")]
        public ActionResult UpdateHomeScreenConfiguration([FromBody] PluginConfiguration configuration)
        {
            try
            {
                if (configuration == null)
                {
                    return BadRequest("Configuration cannot be null");
                }

                // Validate configuration before saving
                var validationHelper = new ConfigurationValidationHelper();
                var validationErrors = validationHelper.ValidateAdminSettings(configuration, m_homeScreenManager);
                if (validationErrors.Any())
                {
                    return BadRequest(new { errors = validationErrors });
                }

                HomeScreenSectionsPlugin.Instance.UpdateConfiguration(configuration);
                return Ok();
            }
            catch (Exception ex)
            {
                return BadRequest($"Error updating configuration: {ex.Message}");
            }
        }

        [HttpPost("BustCache")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [Authorize(Roles = "Administrator")]
        public ActionResult BustCache()
        {
            try
            {
                HomeScreenSectionsPlugin.Instance.BustCache();
                var newCounter = HomeScreenSectionsPlugin.Instance.Configuration.CacheBustCounter;
                return Ok(new { newCounter });
            }
            catch (Exception ex)
            {
                return BadRequest($"Error busting cache: {ex.Message}");
            }
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

            return Ok(new { Enabled = cfg.Enabled, AllowUserOverride = cfg.AllowUserOverride });
        }

        [HttpGet("Ready")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public ActionResult GetReady()
        {
            try
            {
                // Check plugin initialization
                if (HomeScreenSectionsPlugin.Instance?.Configuration == null)
                    return StatusCode(503, "Plugin not initialized");

                // Check HomeScreenManager availability
                if (m_homeScreenManager == null)
                    return StatusCode(503, "HomeScreenManager not available");

                // Check section types are registered
                var sectionTypes = m_homeScreenManager.GetSectionTypes();
                if (!sectionTypes.Any())
                    return StatusCode(503, "No section types registered");

                // All good - ready for external registrations
                return Ok();
            }
            catch (Exception ex)
            {
                return StatusCode(503, $"Plugin error: {ex.Message}");
            }
        }

        [HttpGet("Sections")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [Authorize]
        public ActionResult<QueryResult<HomeScreenSectionInfo>> GetHomeScreenSections(
            [FromQuery] Guid? userId,
            [FromQuery] string? language)
        {
            List<HomeScreenSectionInfo> sections = m_homeScreenSectionService.GetSectionsForUser(userId ?? Guid.Empty, language);

            return new QueryResult<HomeScreenSectionInfo>(
                0,
                sections.Count,
                sections);
        }

        [HttpGet("Admin/Section/{sectionType}")]
        [Authorize(Roles = "Administrator")]
        public ActionResult<List<PluginConfigurationOption>> GetAdminSectionConfigurationOptions(
            [FromRoute] string sectionType)
        {
            return GetAdminConfigurationOptions(sectionType);
        }

        [HttpGet("User/Section/{sectionType}")]
        [Authorize]
        public ActionResult<List<PluginConfigurationOption>> GetUserSectionConfigurationOptions(
            [FromRoute] string sectionType)
        {
            return GetUserConfigurationOptions(sectionType);
        }

        private ActionResult<List<PluginConfigurationOption>> GetAdminConfigurationOptions(string sectionType)
        {
            var section = m_homeScreenManager.GetSectionTypes()
                .FirstOrDefault(s => s.Section?.Equals(sectionType, StringComparison.OrdinalIgnoreCase) == true);
            
            if (section == null)
            {
                return NotFound("Unknown section type: " + sectionType);
            }
            
            PluginConfigurationOption[]? intrinsicConfigurationOptions = section.GetConfigurationOptions()?.ToArray();
            if (intrinsicConfigurationOptions == null)
            {
                intrinsicConfigurationOptions = Array.Empty<PluginConfigurationOption>();
            }

            List<PluginConfigurationOption> configOptionsList = intrinsicConfigurationOptions.ToList();

            PluginConfiguration pluginConfig = HomeScreenSectionsPlugin.Instance.Configuration;
            SectionSettings? currentSectionSettings = pluginConfig.SectionSettings?.FirstOrDefault(s => string.Equals(s.SectionId, sectionType, StringComparison.OrdinalIgnoreCase));

            Dictionary<string, bool> perOptionOverrideMap = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            if (currentSectionSettings?.PluginConfigurations != null)
            {
                foreach (var entry in currentSectionSettings.PluginConfigurations)
                {
                    if (!string.IsNullOrEmpty(entry.Key))
                    {
                        perOptionOverrideMap[entry.Key] = entry.AllowUserOverride;
                    }
                }
            }

            bool changed = false;
            foreach (string key in configOptionsList.Select(o => o.Key).Distinct())
            {
                if (!perOptionOverrideMap.ContainsKey(key))
                {
                    if (currentSectionSettings == null)
                    {
                        currentSectionSettings = new SectionSettings { SectionId = sectionType };
                        List<SectionSettings> sectionSettingsList = pluginConfig.SectionSettings?.ToList() ?? new List<SectionSettings>();
                        sectionSettingsList.Add(currentSectionSettings);
                        pluginConfig.SectionSettings = sectionSettingsList.ToArray();
                    }

                    currentSectionSettings.SetAdminConfigWithPermission<object?>(key, null, allowUserOverride: false);
                    perOptionOverrideMap[key] = false;
                    changed = true;
                }
            }
            
            if (changed)
            {
                HomeScreenSectionsPlugin.Instance.UpdateConfiguration(pluginConfig);
            }

            return Ok(configOptionsList);
        }

        private ActionResult<List<PluginConfigurationOption>> GetUserConfigurationOptions(string sectionType)
        {
            var section = m_homeScreenManager.GetSectionTypes()
                .FirstOrDefault(s => s.Section?.Equals(sectionType, StringComparison.OrdinalIgnoreCase) == true);
            
            if (section == null)
            {
                return NotFound("Unknown section type: " + sectionType);
            }
            
            PluginConfigurationOption[]? intrinsicConfigurationOptions = section.GetConfigurationOptions()?.ToArray();
            if (intrinsicConfigurationOptions == null)
            {
                intrinsicConfigurationOptions = Array.Empty<PluginConfigurationOption>();
            }

            List<PluginConfigurationOption> configOptionsList = intrinsicConfigurationOptions.ToList();

            PluginConfiguration pluginConfig = HomeScreenSectionsPlugin.Instance.Configuration;
            SectionSettings? currentSectionSettings = pluginConfig.SectionSettings?.FirstOrDefault(s => string.Equals(s.SectionId, sectionType, StringComparison.OrdinalIgnoreCase));

            Dictionary<string, bool> perOptionOverrideMap = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            if (currentSectionSettings?.PluginConfigurations != null)
            {
                foreach (var entry in currentSectionSettings.PluginConfigurations)
                {
                    if (!string.IsNullOrEmpty(entry.Key))
                    {
                        perOptionOverrideMap[entry.Key] = entry.AllowUserOverride;
                    }
                }
            }

            if (currentSectionSettings?.AllowUserOverride != true)
            {
                configOptionsList = new List<PluginConfigurationOption>();
            }
            else
            {
                configOptionsList = configOptionsList.Where(o => o.AllowUserOverride && perOptionOverrideMap.TryGetValue(o.Key, out var allow) && allow)
                    .Select(o =>
                    {
                        var adminConfiguredValue = currentSectionSettings?.GetAdminConfig<object>(o.Key, o.DefaultValue);

                        return new PluginConfigurationOption
                        {
                            Key = o.Key,
                            Name = o.Name,
                            Description = o.Description,
                            Type = o.Type,
                            AllowUserOverride = o.AllowUserOverride,
                            IsAdvanced = o.IsAdvanced,
                            Required = o.Required,
                            DefaultValue = adminConfiguredValue ?? o.DefaultValue,
                            DropdownOptions = o.DropdownOptions,
                            DropdownLabels = o.DropdownLabels,
                            Placeholder = o.Placeholder,
                            MinLength = o.MinLength,
                            MaxLength = o.MaxLength,
                            MinValue = o.MinValue,
                            MaxValue = o.MaxValue,
                            Step = o.Step,
                            Pattern = o.Pattern,
                            ValidationMessage = o.ValidationMessage
                        };
                    }).ToList();
            }

            var normalizedOptions = configOptionsList.Select(NormalizePluginConfigurationOption).ToList();

            return Ok(normalizedOptions);
        }

        /// <summary>
        /// Normalizes PluginConfigurationOption dropdowns to ensure consistent format.
        /// </summary>
        private static PluginConfigurationOption NormalizePluginConfigurationOption(PluginConfigurationOption option)
        {
            if (option?.Type != PluginConfigurationType.Dropdown) 
                return option;
            
            option.DropdownOptions ??= Array.Empty<string>();
            
            if (option.DropdownLabels == null || option.DropdownLabels.Length != option.DropdownOptions.Length)
            {
                option.DropdownLabels = option.DropdownOptions;
            }
            
            return option;
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
                AdditionalData = additionalData,
                UserSettings = m_homeScreenManager.GetUserSettings(userId)
            };

            return m_homeScreenManager.InvokeResultsDelegate(sectionType, payload, Request.Query);
        }

        [HttpPost("RegisterSection")]
        public ActionResult RegisterSection([FromBody] SectionRegisterPayload payload)
        {
            if (string.IsNullOrEmpty(payload.DisplayText))
            {
                return BadRequest("Section registration requires DisplayText");
            }

            if (string.IsNullOrEmpty(payload.ResultsEndpoint))
            {
                return BadRequest("Section registration requires ResultsEndpoint");
            }

            if (payload.Info?.VersionControl != null)
            {
                var vc = payload.Info.VersionControl;
                
                vc.RepositoryUrl = null;
                vc.IssuesUrl = null;
                
                var (repositoryUrl, issuesUrl) = SectionInfoHelper.BuildVcsUrls(
                    vc.Platform, 
                    vc.Username, 
                    vc.Repository, 
                    vc.IncludeIssuesLink);
                
                vc.RepositoryUrl = repositoryUrl;
                vc.IssuesUrl = issuesUrl;
            }
            
            if (payload.Info != null)
            {
                payload.Info.FeatureRequestUrl = null;
                
                if (payload.Info.VersionControl?.RepositoryUrl != null)
                {
                    payload.Info.FeatureRequestUrl = SectionInfoHelper.BuildFeatureRequestUrl(
                        payload.Info.VersionControl.FeatureRequestTag);
                }
            }

            var section = new PluginDefinedSection(
                payload.Id, 
                payload.DisplayText!, 
                payload.Info, 
                payload.Route, 
                payload.AdditionalData, 
                payload.ConfigurationOptions, 
                payload.EnableByDefault)
            {
                OnGetResults = sectionPayload =>
                {
                    JObject jsonPayload = JObject.FromObject(sectionPayload);

                    string? publishedServerUrl = m_serverApplicationHost.GetType()
                        .GetProperty("PublishedServerUrl", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(m_serverApplicationHost) as string;
                
                    HttpClient client = new HttpClient();
                    client.BaseAddress = new Uri(publishedServerUrl ?? $"http://localhost:{m_serverApplicationHost.HttpPort}");
                    
                    HttpResponseMessage responseMessage = client.PostAsync(payload.ResultsEndpoint, 
                        new StringContent(jsonPayload.ToString(Formatting.None), MediaTypeHeaderValue.Parse("application/json"))).GetAwaiter().GetResult();

                    return JsonConvert.DeserializeObject<QueryResult<BaseItemDto>>(responseMessage.Content.ReadAsStringAsync().GetAwaiter().GetResult()) ?? new QueryResult<BaseItemDto>();
                }
            };

            m_homeScreenManager.RegisterResultsDelegate(section);
            
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
            string? jellyseerrUrl = HomeScreenSectionsPlugin.Instance.Configuration.JellyseerrUrl;

            if (jellyseerrUrl == null)
            {
                return BadRequest();
            }
            
            HttpClient client = new HttpClient();
            client.BaseAddress = new Uri(jellyseerrUrl);
            client.DefaultRequestHeaders.Add("X-Api-Key", HomeScreenSectionsPlugin.Instance.Configuration.JellyseerrApiKey);
            
            HttpResponseMessage usersResponse = client.GetAsync($"/api/v1/user?q={user.Username}").GetAwaiter().GetResult();
            string userResponseRaw = usersResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            int? jellyseerrUserId = JObject.Parse(userResponseRaw).Value<JArray>("results")!.OfType<JObject>().FirstOrDefault(x => x.Value<string>("jellyfinUsername") == user.Username)?.Value<int>("id");

            if (jellyseerrUserId == null)
            {
                return BadRequest();
            }
            
            client.DefaultRequestHeaders.Add("X-Api-User", jellyseerrUserId.ToString());

            HttpResponseMessage requestResponse;
            if (payload.MediaType == "tv")
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
            
            return Content(responseContent, requestResponse.Content.Headers.ContentType.MediaType);
        }
    }
}
