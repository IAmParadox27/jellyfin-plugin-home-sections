using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
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
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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

        [HttpGet("Configuration")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public ActionResult<PluginConfiguration> GetHomeScreenConfiguration()
        {
            return HomeScreenSectionsPlugin.Instance.Configuration;
        }
        
        [HttpGet("Sections")]
        [ProducesResponseType(StatusCodes.Status200OK)]
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

                info.ViewMode = HomeScreenSectionsPlugin.Instance.Configuration.SectionSettings.FirstOrDefault(x => x.SectionId == info.Section)?.ViewMode ?? info.ViewMode ?? SectionViewMode.Landscape;
                
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

        [HttpGet("Section/{sectionType}")]
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
                
                    HttpClient client = new HttpClient();
                    client.BaseAddress = new Uri(publishedServerUrl ?? $"http://localhost:{m_serverApplicationHost.HttpPort}");
                    
                    HttpResponseMessage responseMessage = client.PostAsync(payload.ResultsEndpoint, 
                        new StringContent(jsonPayload.ToString(Formatting.None), MediaTypeHeaderValue.Parse("application/json"))).GetAwaiter().GetResult();

                    return JsonConvert.DeserializeObject<QueryResult<BaseItemDto>>(responseMessage.Content.ReadAsStringAsync().GetAwaiter().GetResult()) ?? new QueryResult<BaseItemDto>();
                }
            });
            
            return Ok();
        }
    }
}
