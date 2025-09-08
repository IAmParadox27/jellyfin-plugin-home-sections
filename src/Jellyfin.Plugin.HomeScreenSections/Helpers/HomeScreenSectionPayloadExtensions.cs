using Jellyfin.Plugin.HomeScreenSections.Model.Dto;

namespace Jellyfin.Plugin.HomeScreenSections.Helpers
{
    /// <summary>
    /// Extension methods to simplify configuration access for external plugins
    /// </summary>
    public static class HomeScreenSectionPayloadExtensions
    {
        /// <summary>
        /// Gets the effective boolean configuration value for the specified section and key
        /// </summary>
        /// <param name="payload">The section payload</param>
        /// <param name="sectionId">The section identifier</param>
        /// <param name="configKey">The configuration key</param>
        /// <param name="defaultValue">Default value if not found</param>
        /// <returns>The effective boolean configuration value</returns>
        public static bool GetEffectiveBoolConfig(this HomeScreenSectionPayload payload, string sectionId, string configKey, bool defaultValue = false)
        {
            return payload.GetEffectiveConfig<bool>(sectionId, configKey, defaultValue);
        }

        /// <summary>
        /// Gets the effective string configuration value for the specified section and key
        /// </summary>
        /// <param name="payload">The section payload</param>
        /// <param name="sectionId">The section identifier</param>
        /// <param name="configKey">The configuration key</param>
        /// <param name="defaultValue">Default value if not found</param>
        /// <returns>The effective string configuration value</returns>
        public static string GetEffectiveStringConfig(this HomeScreenSectionPayload payload, string sectionId, string configKey, string defaultValue = "")
        {
            return payload.GetEffectiveConfig<string>(sectionId, configKey, defaultValue);
        }

        /// <summary>
        /// Gets the effective integer configuration value for the specified section and key
        /// </summary>
        /// <param name="payload">The section payload</param>
        /// <param name="sectionId">The section identifier</param>
        /// <param name="configKey">The configuration key</param>
        /// <param name="defaultValue">Default value if not found</param>
        /// <returns>The effective integer configuration value</returns>
        public static int GetEffectiveIntConfig(this HomeScreenSectionPayload payload, string sectionId, string configKey, int defaultValue = 0)
        {
            return payload.GetEffectiveConfig<int>(sectionId, configKey, defaultValue);
        }

        /// <summary>
        /// Gets the effective double configuration value for the specified section and key
        /// </summary>
        /// <param name="payload">The section payload</param>
        /// <param name="sectionId">The section identifier</param>
        /// <param name="configKey">The configuration key</param>
        /// <param name="defaultValue">Default value if not found</param>
        /// <returns>The effective double configuration value</returns>
        public static double GetEffectiveDoubleConfig(this HomeScreenSectionPayload payload, string sectionId, string configKey, double defaultValue = 0.0)
        {
            return payload.GetEffectiveConfig<double>(sectionId, configKey, defaultValue);
        }

        /// <summary>
        /// Gets the effective decimal configuration value for the specified section and key
        /// </summary>
        /// <param name="payload">The section payload</param>
        /// <param name="sectionId">The section identifier</param>
        /// <param name="configKey">The configuration key</param>
        /// <param name="defaultValue">Default value if not found</param>
        /// <returns>The effective decimal configuration value</returns>
        public static decimal GetEffectiveDecimalConfig(this HomeScreenSectionPayload payload, string sectionId, string configKey, decimal defaultValue = 0.0m)
        {
            return payload.GetEffectiveConfig<decimal>(sectionId, configKey, defaultValue);
        }
    }
}
