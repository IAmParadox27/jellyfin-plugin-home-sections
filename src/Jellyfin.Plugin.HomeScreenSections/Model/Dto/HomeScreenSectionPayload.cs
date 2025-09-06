using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Library;

namespace Jellyfin.Plugin.HomeScreenSections.Model.Dto
{
    


    public class HomeScreenSectionPayload
    {



        public Guid UserId { get; set; }

        

        public string? AdditionalData { get; set; }

        public ModularHomeUserSettings? UserSettings { get; set; }

        private readonly Dictionary<string, object?> _pluginConfigCache = new Dictionary<string, object?>();

        /// <summary>
        /// Gets the effective configuration value with user override precedence.
        /// Uses caching to avoid repeated lookups during a single request.
        /// </summary>
        public T GetEffectiveConfig<T>(string sectionId, string configurationKey, T defaultValue = default(T)!)
        {
            if (string.IsNullOrEmpty(sectionId) || string.IsNullOrEmpty(configurationKey))
                return defaultValue;

            var adminConfig = HomeScreenSectionsPlugin.Instance?.Configuration;
            var sectionSettings = adminConfig?.SectionSettings?.FirstOrDefault(s => s.SectionId == sectionId);

            var allowUserOverride = sectionSettings?.IsUserOverrideAllowedUnified(configurationKey) ?? false;

            var cacheKey = $"{sectionId}:{configurationKey}:{allowUserOverride}";

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

            object? effectiveValue = null;

            if (sectionSettings != null)
            {
                if (allowUserOverride && UserSettings?.SectionSettings != null)
                {
                    var userSectionSettings = UserSettings.SectionSettings
                        .FirstOrDefault(s => string.Equals(s.SectionId, sectionId, StringComparison.OrdinalIgnoreCase));
                    if (userSectionSettings?.PluginConfigurations.TryGetValue(configurationKey, out var userValue) == true && userValue != null)
                    {
                        effectiveValue = userValue;
                    }
                }

                if (effectiveValue == null)
                {
                    var adminValue = sectionSettings.GetAdminConfig<T>(configurationKey, defaultValue);
                    effectiveValue = adminValue;
                }
            }

            effectiveValue ??= defaultValue;

            _pluginConfigCache[cacheKey] = effectiveValue;

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
