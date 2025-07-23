using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Library;

namespace Jellyfin.Plugin.HomeScreenSections.Model.Dto
{
    
    
    
    public class HomeScreenSectionPayload
    {
        
        
        
        public Guid UserId { get; set; }

        
        
        
        
        public string? AdditionalData { get; set; }

        /// <summary>
        /// User-specific settings for modular home sections.
        /// </summary>
        public ModularHomeUserSettings? UserSettings { get; set; }
        
        /// <summary>
        /// Cache for plugin configuration values to avoid repeated lookups during a single request
        /// </summary>
        private readonly Dictionary<string, object?> _pluginConfigCache = new Dictionary<string, object?>();
        
        /// <summary>
        /// Gets the effective value for a plugin-defined configuration option,
        /// considering both admin configuration and user preferences.
        /// Uses caching to avoid repeated lookups during a single request.
        /// </summary>
        /// <param name="sectionId">The section ID to check.</param>
        /// <param name="configurationKey">The configuration key to retrieve.</param>
        /// <param name="defaultValue">The default value if no configuration is found.</param>
        /// <returns>The effective configuration value.</returns>
        public T GetEffectivePluginConfiguration<T>(string sectionId, string configurationKey, T defaultValue = default(T)!)
        {
            if (string.IsNullOrEmpty(sectionId) || string.IsNullOrEmpty(configurationKey))
                return defaultValue;
            
            // Create cache key combining section and config key
            var cacheKey = $"{sectionId}:{configurationKey}";
            
            // Check cache first
            if (_pluginConfigCache.TryGetValue(cacheKey, out var cachedValue))
            {
                try
                {
                    return (T)Convert.ChangeType(cachedValue, typeof(T))!;
                }
                catch
                {
                    return defaultValue;
                }
            }
            
            // Cache miss - perform lookup
            object? effectiveValue = null;
            
            var adminConfig = HomeScreenSectionsPlugin.Instance?.Configuration;
            var sectionSettings = adminConfig?.SectionSettings?.FirstOrDefault(s => s.SectionId == sectionId);
            
            if (sectionSettings != null)
            {
                // Check if this configuration option allows user override
                // For now, default to allowing override - this could be enhanced to check plugin definition
                var allowUserOverride = true;
                
                if (allowUserOverride && UserSettings != null)
                {
                    // Check user's settings first
                    var userSectionSettings = UserSettings.GetSectionSettings(sectionId);
                    if (userSectionSettings.PluginConfigurations.TryGetValue(configurationKey, out var userValue) && userValue != null)
                    {
                        effectiveValue = userValue;
                    }
                }
                
                // Fall back to admin's setting if no user override
                if (effectiveValue == null)
                {
                    var adminValue = sectionSettings.GetPluginConfiguration<T>(configurationKey, default);
                    if (adminValue != null && !adminValue.Equals(default(T)))
                    {
                        effectiveValue = adminValue;
                    }
                }
            }
            
            // Use default if no configuration found
            effectiveValue ??= defaultValue;
            
            // Cache the result
            _pluginConfigCache[cacheKey] = effectiveValue;
            
            // Convert and return
            try
            {
                return (T)Convert.ChangeType(effectiveValue, typeof(T))!;
            }
            catch
            {
                return defaultValue;
            }
        }
    }
}
