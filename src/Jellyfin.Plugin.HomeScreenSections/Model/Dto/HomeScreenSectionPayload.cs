using Jellyfin.Plugin.HomeScreenSections.Configuration;

namespace Jellyfin.Plugin.HomeScreenSections.Model.Dto
{
    public class HomeScreenSectionPayload
    {
        public Guid UserId { get; set; }

        public string? AdditionalData { get; set; }
        
        private static readonly Dictionary<string, object?> m_pluginConfigCache = new Dictionary<string, object?>();

        public static T GetEffectiveConfig<T>(string sectionId, string configurationKey, T defaultValue = default(T)!)
        {
            if (string.IsNullOrEmpty(sectionId) || string.IsNullOrEmpty(configurationKey))
            {
                return defaultValue;
            }

            PluginConfiguration? adminConfig = HomeScreenSectionsPlugin.Instance?.Configuration;
            SectionSettings? sectionSettings = adminConfig?.SectionSettings?.FirstOrDefault(s => s.SectionId == sectionId);

            string cacheKey = $"{sectionId}:{configurationKey}";

            if (m_pluginConfigCache.TryGetValue(cacheKey, out object? cachedValue))
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
                if (effectiveValue == null)
                {
                    var adminValue = sectionSettings.GetAdminConfig<T>(configurationKey, defaultValue);
                    effectiveValue = adminValue;
                }
            }

            effectiveValue ??= defaultValue;

            m_pluginConfigCache[cacheKey] = effectiveValue;

            try
            {
                return (T)Convert.ChangeType(effectiveValue, typeof(T))!;
            }
            catch
            {
                return defaultValue;
            }
        }

        public static bool GetEffectiveBoolConfig(string sectionId, string configKey, bool defaultValue = false)
        {
            return HomeScreenSectionPayload.GetEffectiveConfig<bool>(sectionId, configKey, defaultValue);
        }

        public static string GetEffectiveStringConfig(string sectionId, string configKey, string defaultValue = "")
        {
            return HomeScreenSectionPayload.GetEffectiveConfig<string>(sectionId, configKey, defaultValue);
        }

        public static int GetEffectiveIntConfig(string sectionId, string configKey, int defaultValue = 0)
        {
            return HomeScreenSectionPayload.GetEffectiveConfig<int>(sectionId, configKey, defaultValue);
        }

        public static double GetEffectiveDoubleConfig(string sectionId, string configKey, double defaultValue = 0.0)
        {
            return HomeScreenSectionPayload.GetEffectiveConfig<double>(sectionId, configKey, defaultValue);
        }

        public static decimal GetEffectiveDecimalConfig(string sectionId, string configKey, decimal defaultValue = 0.0m)
        {
            return HomeScreenSectionPayload.GetEffectiveConfig<decimal>(sectionId, configKey, defaultValue);
        }
    }
}
