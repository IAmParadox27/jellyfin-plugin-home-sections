using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Helpers;
using Jellyfin.Plugin.HomeScreenSections.Library;
using MediaBrowser.Model;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

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

        private sealed class SectionPermissionContext
        {
            public SectionSettings AdminSection { get; init; } = null!;
            public IHomeScreenSection SectionType { get; init; } = null!;
            public Dictionary<string, bool> OverrideMap { get; init; } = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, PluginConfigurationOption> OptionDefs { get; init; } = new(StringComparer.OrdinalIgnoreCase);
            public bool EnableDisableGranted { get; init; }
            public bool GlobalAllowUserOverride { get; init; }
        }

        private Dictionary<string, SectionPermissionContext> BuildPermissionContexts()
        {
            var contexts = new Dictionary<string, SectionPermissionContext>(StringComparer.OrdinalIgnoreCase);
            var pluginConfig = HomeScreenSectionsPlugin.Instance?.Configuration;
            if (pluginConfig == null) return contexts;

            var sectionTypeMap = m_homeScreenManager.GetSectionTypes()
                .Where(s => !string.IsNullOrWhiteSpace(s.Section))
                .GroupBy(s => s.Section!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var adminSec in pluginConfig.SectionSettings ?? Enumerable.Empty<SectionSettings>())
            {
                if (!sectionTypeMap.TryGetValue(adminSec.SectionId, out var sectionType)) continue;

                var overrideMap = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                bool enableDisableGranted = false;

                // Build override map from PluginConfigurations
                foreach (var entry in adminSec.PluginConfigurations ?? Array.Empty<PluginConfigurationEntry>())
                {
                    if (!string.IsNullOrEmpty(entry.Key))
                    {
                        overrideMap[entry.Key] = entry.AllowUserOverride;
                        if (entry.Key.Equals("Enabled", StringComparison.OrdinalIgnoreCase))
                        {
                            enableDisableGranted = entry.AllowUserOverride;
                        }
                    }
                }

                var optionDefs = sectionType.GetConfigurationOptions()
                    .ToDictionary(o => o.Key, StringComparer.OrdinalIgnoreCase);

                contexts[adminSec.SectionId] = new SectionPermissionContext
                {
                    AdminSection = adminSec,
                    SectionType = sectionType,
                    OverrideMap = overrideMap,
                    EnableDisableGranted = enableDisableGranted,
                    OptionDefs = optionDefs,
                    GlobalAllowUserOverride = pluginConfig.AllowUserOverride
                };
            }
            return contexts;
        }

        private static bool IsOptionOverrideAllowed(SectionPermissionContext ctx, string key, PluginConfigurationOption optDef)
        {
            if (string.IsNullOrWhiteSpace(key)) return false;
            if (!optDef.AllowUserOverride) return false;
            
            return ctx.AdminSection.IsUserOverrideAllowedUnified(key);
        }

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
        [HttpGet("User/SectionTypes")]
        [HttpGet("Admin/SectionTypes")]
        [Authorize]
        public QueryResult<HomeScreenSectionInfo> GetSectionTypes()
        {
            // Todo add reading whether the section is enabled or disabled by the user.
            List<HomeScreenSectionInfo> items = new List<HomeScreenSectionInfo>();

            IEnumerable<IHomeScreenSection> sections = m_homeScreenManager.GetSectionTypes();
            
            // Check if this is a user-specific request to include permission data
            bool isUserRequest = HttpContext.Request.Path.Value?.Contains("/User/", StringComparison.OrdinalIgnoreCase) == true;
            Dictionary<string, SectionPermissionContext>? ctxMap = null;
            
            if (isUserRequest)
            {
                ctxMap = BuildPermissionContexts();
            }

            foreach (IHomeScreenSection section in sections)
            {
                HomeScreenSectionInfo item = section.GetInfo();

                item.ViewMode ??= SectionViewMode.Landscape;
                
                // For user requests, populate the AllowUserToggle and UserVisible properties
                if (isUserRequest && ctxMap != null && !string.IsNullOrEmpty(section.Section))
                {
                    if (ctxMap.TryGetValue(section.Section, out var ctx))
                    {
                        item.AllowUserToggle = ctx.EnableDisableGranted;
                        item.UserVisible = ctx.AdminSection.IsEnabledByAdmin() || ctx.GlobalAllowUserOverride;
                    }
                    else
                    {
                        // Section not in admin configuration - default behavior
                        item.AllowUserToggle = false;
                        item.UserVisible = true;
                    }
                }
                
                items.Add(item);
            }

            return new QueryResult<HomeScreenSectionInfo>(null, items.Count, items);
        }

        /// <summary>
        /// Get the user settings for Modular Home.
        /// </summary>
        /// <param name="userId">The user ID.</param>
        /// <returns><see cref="ModularHomeUserSettings"/>.</returns>
        [HttpGet("User/Settings")] 
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
        [HttpPost("User/Settings")] 
        [Authorize]
        public ActionResult UpdateSettings([FromBody] ModularHomeUserSettings obj)
        {
            if (obj == null) return BadRequest("Missing body");
            if (obj.UserId == Guid.Empty) return BadRequest("Invalid user id");

            var ctxMap = BuildPermissionContexts();
            // Build set of known section ids
            var knownSectionIds = new HashSet<string>(ctxMap.Keys, StringComparer.OrdinalIgnoreCase);

            // Keep only known
            obj.EnabledSections = obj.EnabledSections
                .Where(s => knownSectionIds.Contains(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Enforce forced sections (when enable/disable not granted)
            foreach (var (secId, ctx) in ctxMap)
            {
                bool shouldForce = !ctx.EnableDisableGranted;
                if (!shouldForce) continue;
                bool adminEnabled = ctx.AdminSection.IsEnabledByAdmin();
                bool userHas = obj.EnabledSections.Contains(secId);
                if (adminEnabled && !userHas)
                {
                    obj.EnabledSections.Add(secId);
                }
                else if (!adminEnabled && userHas)
                {
                    obj.EnabledSections.RemoveAll(s => s.Equals(secId, StringComparison.OrdinalIgnoreCase));
                }
            }

            var sanitizedSectionSettings = new List<UserSectionSettings>();
            foreach (var userSec in obj.SectionSettings ?? new List<UserSectionSettings>())
            {
                if (string.IsNullOrWhiteSpace(userSec.SectionId)) continue;
                if (!ctxMap.TryGetValue(userSec.SectionId, out var ctx)) continue;
                userSec.Enabled = obj.EnabledSections.Contains(userSec.SectionId);

                userSec.PluginConfigurations ??= new Dictionary<string, object?>();
                userSec.PluginConfigurations["Enabled"] = userSec.Enabled.ToString().ToLowerInvariant();

                var sanitizedOptions = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                sanitizedOptions["Enabled"] = userSec.PluginConfigurations["Enabled"];
                
                foreach (var kv in userSec.PluginConfigurations ?? new Dictionary<string, object?>())
                {
                    var key = kv.Key;
                    if (!ctx.OptionDefs.TryGetValue(key, out var optDef)) continue;
                    if (!IsOptionOverrideAllowed(ctx, key, optDef)) continue;

                    object? val = kv.Value;

                    try
                    {
                        switch (optDef.Type)
                        {
                            case PluginConfigurationType.Checkbox:
                                val = CoerceBool(val);
                                break;
                            case PluginConfigurationType.Dropdown:
                                val = CoerceDropdown(val, optDef);
                                break;
                            case PluginConfigurationType.NumberBox:
                                val = CoerceNumber(val, optDef);
                                break;
                            case PluginConfigurationType.TextBox:
                            default:
                                val = CoerceText(val, optDef);
                                break;
                        }
                        sanitizedOptions[key] = val;
                    }
                    catch
                    {
                        // Use admin-configured value as fallback instead of original default
                        var (adminConfiguredValue, _) = ctx.AdminSection.GetConfigWithPermission<object?>(key);
                        sanitizedOptions[key] = adminConfiguredValue ?? optDef.DefaultValue;
                    }
                }
                userSec.PluginConfigurations = sanitizedOptions;
                
                sanitizedSectionSettings.Add(userSec);
            }

            // Ensure each enabled section has an entry
            foreach (var enabledId in obj.EnabledSections)
            {
                if (sanitizedSectionSettings.Any(s => s.SectionId.Equals(enabledId, StringComparison.OrdinalIgnoreCase))) continue;
                sanitizedSectionSettings.Add(new UserSectionSettings
                {
                    SectionId = enabledId,
                    Enabled = true,
                    PluginConfigurations = new Dictionary<string, object?>
                    {
                        ["Enabled"] = "true"
                    }
                });
            }
            obj.SectionSettings = sanitizedSectionSettings;

            // Validate user settings before saving
            var validationHelper = new ConfigurationValidationHelper();
            var permissionContexts = ctxMap.ToDictionary(
                kvp => kvp.Key, 
                kvp => (object)kvp.Value,
                StringComparer.OrdinalIgnoreCase
            );
            var validationErrors = validationHelper.ValidateUserSettings(obj, permissionContexts);
            if (validationErrors.Any())
            {
                return BadRequest(new { errors = validationErrors });
            }

            m_homeScreenManager.UpdateUserSettings(obj.UserId, obj);
            return Ok();

            static bool CoerceBool(object? v)
            {
                if (v is bool b) return b;
                if (v == null) return false;
                var s = v.ToString()?.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(s)) return false;
                return s is "1" or "true" or "yes" or "on";
            }
            static object? CoerceDropdown(object? v, PluginConfigurationOption opt)
            {
                var allowed = opt.DropdownOptions ?? Array.Empty<string>();
                if (allowed.Length == 0) return opt.DefaultValue;
                var s = v?.ToString() ?? string.Empty;
                var match = allowed.FirstOrDefault(o => o.Equals(s, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
                return opt.DefaultValue ?? allowed[0];
            }
            static object? CoerceNumber(object? v, PluginConfigurationOption opt)
            {
                double? num = null;
                if (v is double d) num = d;
                else if (v is int i) num = i;
                else if (double.TryParse(v?.ToString(), out var parsed)) num = parsed;
                if (num == null) return opt.DefaultValue;
                if (opt.MinValue.HasValue && num < opt.MinValue) num = opt.MinValue.Value;
                if (opt.MaxValue.HasValue && num > opt.MaxValue) num = opt.MaxValue.Value;
                return num;
            }
            static object? CoerceText(object? v, PluginConfigurationOption opt)
            {
                var s = v?.ToString() ?? string.Empty;
                if (opt.MaxLength.HasValue && s.Length > opt.MaxLength.Value) s = s.Substring(0, opt.MaxLength.Value);
                if (opt.MinLength.HasValue && s.Length < opt.MinLength.Value) return opt.DefaultValue ?? string.Empty;
                var pattern = opt.Pattern;
                if (!string.IsNullOrWhiteSpace(pattern))
                {
                    try
                    {
                        if (!System.Text.RegularExpressions.Regex.IsMatch(s, pattern))
                            return opt.DefaultValue ?? string.Empty;
                    }
                    catch
                    {
                    }
                }
                return s;
            }
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
