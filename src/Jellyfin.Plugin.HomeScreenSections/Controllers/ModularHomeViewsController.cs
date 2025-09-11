using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Library;
using MediaBrowser.Model;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.HomeScreenSections.Controllers
{
    /// <summary>
    /// API controller for Modular Home plugin.
    /// </summary>
    [ApiController]
    [Route("[controller]")]
    public class ModularHomeViewsController : ControllerBase
    {
        private readonly ILogger<ModularHomeViewsController> m_logger;
        private readonly IHomeScreenManager m_homeScreenManager;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="logger">Instance of <see cref="ILogger"/> interface.</param>
        /// <param name="homeScreenManager">Instance of <see cref="IHomeScreenManager"/> interface.</param>
        public ModularHomeViewsController(ILogger<ModularHomeViewsController> logger, IHomeScreenManager homeScreenManager)
        {
            m_logger = logger;
            m_homeScreenManager = homeScreenManager;
        }

        /// <summary>
        /// Get the view for the plugin.
        /// </summary>
        /// <param name="viewName">The view identifier.</param>
        /// <returns>View.</returns>
        [HttpGet("{viewName}")]
        [Authorize]
        public ActionResult GetView([FromRoute] string viewName)
        {
            return ServeView(viewName);
        }

        /// <summary>
        /// Get the section types that are registered in Modular Home.
        /// </summary>
        /// <returns>Array of <see cref="HomeScreenSectionInfo"/>.</returns>
        [HttpGet("Sections")]
        [Authorize]
        public QueryResult<HomeScreenSectionInfo> GetSectionTypes()
        {
            // Todo add reading whether the section is enabled or disabled by the user.
            List<HomeScreenSectionInfo> items = new List<HomeScreenSectionInfo>();

            IEnumerable<IHomeScreenSection> sections = m_homeScreenManager.GetSectionTypes();

            foreach (IHomeScreenSection section in sections)
            {
                HomeScreenSectionInfo item = section.GetInfo();

                item.ViewMode ??= SectionViewMode.Landscape;
                
                items.Add(item);
            }

            return new QueryResult<HomeScreenSectionInfo>(null, items.Count, items);
        }

        /// <summary>
        /// Get the user settings for Modular Home.
        /// </summary>
        /// <param name="userId">The user ID.</param>
        /// <returns><see cref="ModularHomeUserSettings"/>.</returns>
        [HttpGet("UserSettings")]
        [Authorize]
        public ActionResult<ModularHomeUserSettings> GetUserSettings([FromQuery] Guid userId)
        {
            IEnumerable<SectionSettings> defaultEnabledSections =
                HomeScreenSectionsPlugin.Instance.Configuration.SectionSettings.Where(x => x.Enabled);
            
            return m_homeScreenManager.GetUserSettings(userId) ?? new ModularHomeUserSettings
            {
                UserId = userId,
                EnabledSections = defaultEnabledSections.Select(x => x.SectionId).ToList()
            };
        }

        /// <summary>
        /// Update the user settings for Modular Home.
        /// </summary>
        /// <param name="obj">Instance of <see cref="ModularHomeUserSettings" />.</param>
        /// <returns>Status.</returns>
        [HttpPost("UserSettings")]
        [Authorize]
        public ActionResult UpdateSettings([FromBody] ModularHomeUserSettings obj)
        {
            m_homeScreenManager.UpdateUserSettings(obj.UserId, obj);

            return Ok();
        }

        private ActionResult ServeView(string viewName)
        {
            if (HomeScreenSectionsPlugin.Instance == null)
            {
                return BadRequest("No plugin instance found");
            }

            IEnumerable<PluginPageInfo> pages = HomeScreenSectionsPlugin.Instance.GetViews();

            if (pages == null)
            {
                return NotFound("Pages is null or empty");
            }

            PluginPageInfo? view = pages.FirstOrDefault(pageInfo => pageInfo?.Name == viewName, null);

            if (view == null)
            {
                return NotFound("No matching view found");
            }

            Stream? stream = HomeScreenSectionsPlugin.Instance.GetType().Assembly.GetManifestResourceStream(view.EmbeddedResourcePath);

            if (stream == null)
            {
                m_logger.LogError("Failed to get resource {Resource}", view.EmbeddedResourcePath);
                return NotFound();
            }

            return File(stream, MimeTypes.GetMimeType(view.EmbeddedResourcePath));
        }
    }
}
