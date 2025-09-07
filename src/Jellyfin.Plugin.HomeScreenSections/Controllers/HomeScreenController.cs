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

        public HomeScreenController(
            IHomeScreenManager homeScreenManager,
            IDisplayPreferencesManager displayPreferencesManager,
            IServerApplicationHost serverApplicationHost,
            IApplicationPaths applicationPaths)
        {
            m_homeScreenManager = homeScreenManager;
            m_displayPreferencesManager = displayPreferencesManager;
            m_serverApplicationHost = serverApplicationHost;
            m_applicationPaths = applicationPaths;
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
            
            return File(stream, "text/css");
        }

        [HttpGet("client/shared-utils.js")]
        [Produces("application/javascript")]
        [ResponseCache(Duration = 3600)]
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

        /// <summary>
        /// User-safe meta info (no per-section option values) for client settings UI.
        /// </summary>
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

        /// <summary>
        /// Returns 200 OK when ready for section registration, 503 when not ready.
        /// </summary>
        [HttpGet("Ready")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public ActionResult GetReady()
        {
            try
            {
                if (HomeScreenSectionsPlugin.Instance?.Configuration == null)
                    return StatusCode(503, "Plugin not initialized");
                    
                if (m_homeScreenManager == null)
                    return StatusCode(503, "HomeScreenManager not available");
                    
                var sectionTypes = m_homeScreenManager.GetSectionTypes();
                if (!sectionTypes.Any())
                    return StatusCode(503, "No section types registered");
                    
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
            string displayPreferencesId = "usersettings";
            Guid itemId = displayPreferencesId.GetMD5();

            DisplayPreferences displayPreferences = m_displayPreferencesManager.GetDisplayPreferences(userId ?? Guid.Empty, itemId, "emby");
            ModularHomeUserSettings? settings = m_homeScreenManager.GetUserSettings(userId ?? Guid.Empty);

            List<IHomeScreenSection> sectionTypes = m_homeScreenManager.GetSectionTypes().Where(x => settings?.EnabledSections.Contains(x.Section ?? string.Empty) ?? false).ToList();

            List<IHomeScreenSection> sectionInstances = new List<IHomeScreenSection>();

            List<string> homeSectionOrderTypes = new List<string>();
            if (HomeScreenSectionsPlugin.Instance.Configuration.AllowUserOverride)
            {
                foreach (HomeSection section in displayPreferences.HomeSections.OrderBy(x => x.Order))
                {
                    switch (section.Type)
                    {
                        case HomeSectionType.SmallLibraryTiles:
                            homeSectionOrderTypes.Add("MyMedia");
                            break;
                        case HomeSectionType.Resume:
                            homeSectionOrderTypes.Add("ContinueWatching");
                            break;
                        case HomeSectionType.LatestMedia:
                            homeSectionOrderTypes.Add("LatestMovies");
                            homeSectionOrderTypes.Add("LatestShows");
                            break;
                        case HomeSectionType.NextUp:
                            homeSectionOrderTypes.Add("NextUp");
                            break;
                    }
                }
            }

            foreach (string type in homeSectionOrderTypes)
            {
                IHomeScreenSection? sectionType = sectionTypes.FirstOrDefault(x => x.Section == type);

                if (sectionType != null)
                {
                    if (sectionType.Limit > 1)
                    {
                        SectionSettings? sectionSettings = HomeScreenSectionsPlugin.Instance.Configuration.SectionSettings.FirstOrDefault(x =>
                            x.SectionId == sectionType.Section);

                        Random rnd = new Random();
                        int instanceCount = rnd.Next(sectionSettings?.LowerLimit ?? 0, sectionSettings?.UpperLimit ?? sectionType.Limit ?? 1);

                        for (int i = 0; i < instanceCount; ++i)
                        {
                            sectionInstances.Add(sectionType.CreateInstance(userId, sectionInstances.Where(x => x.GetType() == sectionType.GetType())));
                        }
                    }
                    else if (sectionType.Limit == 1)
                    {
                        sectionInstances.Add(sectionType.CreateInstance(userId));
                    }
                }
            }

            sectionTypes.RemoveAll(x => homeSectionOrderTypes.Contains(x.Section ?? string.Empty));

            IEnumerable<IGrouping<int, SectionSettings>> groupedOrderedSections = HomeScreenSectionsPlugin.Instance.Configuration.SectionSettings
                .OrderBy(x => x.OrderIndex)
                .GroupBy(x => x.OrderIndex);

            foreach (IGrouping<int, SectionSettings> orderedSections in groupedOrderedSections)
            {
                List<IHomeScreenSection> tmpPluginSections = new List<IHomeScreenSection>(); // we want these randomly distributed among each other.
                
                foreach (SectionSettings sectionSettings in orderedSections)
                {
                    IHomeScreenSection? sectionType = sectionTypes.FirstOrDefault(x => x.Section == sectionSettings.SectionId);

                    if (sectionType != null)
                    {
                        if (sectionType.Limit > 1)
                        {
                            Random rnd = new Random();
                            int instanceCount = rnd.Next(sectionSettings?.LowerLimit ?? 0, sectionSettings?.UpperLimit ?? sectionType.Limit ?? 1);
                            
                            for (int i = 0; i < instanceCount; ++i)
                            {
                                IHomeScreenSection[] tmpSectionInstances = tmpPluginSections.Where(x => x?.GetType() == sectionType.GetType())
                                    .Concat(sectionInstances.Where(x => x.GetType() == sectionType.GetType())).ToArray();
                            
                                tmpPluginSections.Add(sectionType.CreateInstance(userId, tmpSectionInstances));
                            }
                        }
                        else if (sectionType.Limit == 1)
                        {
                            tmpPluginSections.Add(sectionType.CreateInstance(userId));
                        }
                    }
                }
                
                tmpPluginSections.Shuffle();
                
                sectionInstances.AddRange(tmpPluginSections);
            }
            
            List<HomeScreenSectionInfo> sections = sectionInstances.Where(x => x != null).Select(x =>
            {
                HomeScreenSectionInfo info = x.AsInfo();

                SectionSettings? sectionSettings = HomeScreenSectionsPlugin.Instance.Configuration.SectionSettings.FirstOrDefault(s => s.SectionId == info.Section);
                info.ViewMode = sectionSettings?.ViewMode ?? info.ViewMode ?? SectionViewMode.Landscape;
                string? displayTextToUse = null;

                if (sectionSettings != null && !string.IsNullOrEmpty(info.Section))
                {
                    var tempPayload = new HomeScreenSectionPayload
                    {
                        UserId = settings?.UserId ?? Guid.Empty,
                        UserSettings = settings
                    };
                    
                    string headerDisplay = tempPayload.GetEffectiveStringConfig(info.Section, "SectionHeaderDisplay", "ShowWithNavigation");
                    
                    if (headerDisplay == "Hide")
                    {
                        info.DisplayText = string.Empty;
                    }
                    else
                    {
                        displayTextToUse = tempPayload.GetEffectiveStringConfig(info.Section, "CustomDisplayText", "");
                        if (!string.IsNullOrWhiteSpace(displayTextToUse))
                        {
                            info.DisplayText = displayTextToUse;
                        }
                    }
                    
                    // Handle route button visibility
                    if (headerDisplay == "ShowWithoutNavigation" || headerDisplay == "Hide")
                    {
                        info.Route = null;
                    }
                }
                else if (!string.IsNullOrWhiteSpace(displayTextToUse))
                {
                    info.DisplayText = displayTextToUse;
                }

                if (language != "en" && !string.IsNullOrEmpty(language?.Trim()) &&
                    info.DisplayText != null)
                {
                    string? translatedResult = TranslationHelper.TranslateAsync(info.DisplayText, "en", language.Trim())
                        .GetAwaiter().GetResult();

                    info.DisplayText = translatedResult;
                }
                
                return info;
            }).ToList();

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
        public async Task<ActionResult> MakeDiscoverRequest([FromServices] IUserManager userManager, [FromBody] DiscoverRequestPayload payload)
        {
            User? user = userManager.GetUserById(payload.UserId);
            string? jellyseerrUrl = HomeScreenSectionsPlugin.Instance.Configuration.JellyseerrUrl;

            if (jellyseerrUrl == null)
            {
                return BadRequest();
            }
            
            HttpClient client = new HttpClient();
            client.BaseAddress = new Uri(jellyseerrUrl);
            client.DefaultRequestHeaders.Add("X-Api-Key", HomeScreenSectionsPlugin.Instance.Configuration.JellyseerrApiKey);
            
            HttpResponseMessage usersResponse = await client.GetAsync("/api/v1/user");
            string userResponseRaw = await usersResponse.Content.ReadAsStringAsync();
            int jellyseerrUserId = JObject.Parse(userResponseRaw).Value<JArray>("results").OfType<JObject>().FirstOrDefault(x => x.Value<string>("jellyfinUsername") == user.Username).Value<int>("id");
            
            client.DefaultRequestHeaders.Add("X-Api-User", jellyseerrUserId.ToString());

            HttpResponseMessage requestResponse = await client.PostAsync("/api/v1/request", JsonContent.Create(new JellyseerrRequestPayload()
            {
                MediaType = payload.MediaType,
                MediaId = payload.MediaId
            }));

            string responseContent = await requestResponse.Content.ReadAsStringAsync();
            
            return Content(responseContent, requestResponse.Content.Headers.ContentType.MediaType);
        }
    }
}
