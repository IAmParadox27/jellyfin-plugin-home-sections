using Jellyfin.Plugin.HomeScreenSections.Model.Dto;

namespace Jellyfin.Plugin.HomeScreenSections.Extensions
{
    /// <summary>
    /// Extension methods to simplify configuration access for external plugins
    /// </summary>
    public static class PluginConfigurationExtensions
    {
        /// <summary>
        /// Gets a boolean configuration value for the specified section and key
        /// </summary>
        /// <param name="payload">The section payload</param>
        /// <param name="sectionId">The section identifier</param>
        /// <param name="configKey">The configuration key</param>
        /// <param name="defaultValue">Default value if not found</param>
        /// <returns>The effective boolean configuration value</returns>
        public static bool GetBoolConfig(this HomeScreenSectionPayload payload, string sectionId, string configKey, bool defaultValue = false)
        {
            return payload.GetEffectivePluginConfiguration<bool>(sectionId, configKey, defaultValue);
        }

        /// <summary>
        /// Gets a string configuration value for the specified section and key
        /// </summary>
        /// <param name="payload">The section payload</param>
        /// <param name="sectionId">The section identifier</param>
        /// <param name="configKey">The configuration key</param>
        /// <param name="defaultValue">Default value if not found</param>
        /// <returns>The effective string configuration value</returns>
        public static string GetStringConfig(this HomeScreenSectionPayload payload, string sectionId, string configKey, string defaultValue = "")
        {
            return payload.GetEffectivePluginConfiguration<string>(sectionId, configKey, defaultValue);
        }

        /// <summary>
        /// Gets an integer configuration value for the specified section and key
        /// </summary>
        /// <param name="payload">The section payload</param>
        /// <param name="sectionId">The section identifier</param>
        /// <param name="configKey">The configuration key</param>
        /// <param name="defaultValue">Default value if not found</param>
        /// <returns>The effective integer configuration value</returns>
        public static int GetIntConfig(this HomeScreenSectionPayload payload, string sectionId, string configKey, int defaultValue = 0)
        {
            return payload.GetEffectivePluginConfiguration<int>(sectionId, configKey, defaultValue);
        }

        /// <summary>
        /// Gets a double configuration value for the specified section and key
        /// </summary>
        /// <param name="payload">The section payload</param>
        /// <param name="sectionId">The section identifier</param>
        /// <param name="configKey">The configuration key</param>
        /// <param name="defaultValue">Default value if not found</param>
        /// <returns>The effective double configuration value</returns>
        public static double GetDoubleConfig(this HomeScreenSectionPayload payload, string sectionId, string configKey, double defaultValue = 0.0)
        {
            return payload.GetEffectivePluginConfiguration<double>(sectionId, configKey, defaultValue);
        }

        /// <summary>
        /// Gets a decimal configuration value for the specified section and key
        /// </summary>
        /// <param name="payload">The section payload</param>
        /// <param name="sectionId">The section identifier</param>
        /// <param name="configKey">The configuration key</param>
        /// <param name="defaultValue">Default value if not found</param>
        /// <returns>The effective decimal configuration value</returns>
        public static decimal GetDecimalConfig(this HomeScreenSectionPayload payload, string sectionId, string configKey, decimal defaultValue = 0.0m)
        {
            return payload.GetEffectivePluginConfiguration<decimal>(sectionId, configKey, defaultValue);
        }
    }
}
