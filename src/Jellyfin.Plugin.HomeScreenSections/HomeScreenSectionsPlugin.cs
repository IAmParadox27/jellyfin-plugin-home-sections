using Jellyfin.Plugin.HomeScreenSections.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.HomeScreenSections
{
    public class HomeScreenSectionsPlugin : BasePlugin<PluginConfiguration>, IPlugin, IHasPluginConfiguration, IHasWebPages
    {
        internal IServerConfigurationManager ServerConfigurationManager { get; private set; }
        
        public override Guid Id => Guid.Parse("074fa1cc-5c7a-4203-bf96-1a33d36b7de7");

        public override string Name => "Home Screen Sections";

        public static HomeScreenSectionsPlugin Instance { get; private set; } = null!;
        
        internal IServiceProvider ServiceProvider { get; set; }
    
        public HomeScreenSectionsPlugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, IServerConfigurationManager serverConfigurationManager, IServiceProvider serviceProvider) : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
            
            ServerConfigurationManager = serverConfigurationManager;
            ServiceProvider = serviceProvider;
        
            string homeScreenSectionsConfigDir = Path.Combine(applicationPaths.PluginConfigurationsPath, "Jellyfin.Plugin.HomeScreenSections");
            if (!Directory.Exists(homeScreenSectionsConfigDir))
            {
                Directory.CreateDirectory(homeScreenSectionsConfigDir);
            }
            PerformMigrations();
            
            string pluginPagesConfig = Path.Combine(applicationPaths.PluginConfigurationsPath, "Jellyfin.Plugin.PluginPages", "config.json");
        
            JObject config = new JObject();
            if (!File.Exists(pluginPagesConfig))
            {
                FileInfo info = new FileInfo(pluginPagesConfig);
                info.Directory?.Create();
            }
            else
            {
                config = JObject.Parse(File.ReadAllText(pluginPagesConfig));
            }

            if (!config.ContainsKey("pages"))
            {
                config.Add("pages", new JArray());
            }

            if (!config.Value<JArray>("pages")!.Any(x => x.Value<string>("Id") == typeof(HomeScreenSectionsPlugin).Namespace))
            {
                string rootUrl = ServerConfigurationManager.GetNetworkConfiguration().BaseUrl.TrimStart('/').Trim();
                if (!string.IsNullOrEmpty(rootUrl))
                {
                    rootUrl = $"/{rootUrl}";
                }
                
                config.Value<JArray>("pages")!.Add(new JObject
                {
                    { "Id", typeof(HomeScreenSectionsPlugin).Namespace },
                    { "Url", $"{rootUrl}/ModularHomeViews/settings" },
                    { "DisplayText", "Modular Home" },
                    { "Icon", "ballot" }
                });
        
                File.WriteAllText(pluginPagesConfig, config.ToString(Formatting.Indented));
            }
        }

        public IEnumerable<PluginPageInfo> GetPages()
        {
            string? prefix = GetType().Namespace;

            yield return new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = $"{prefix}.Configuration.config.html"
            };
        }

        /// <summary>
        /// Get the views that the plugin serves.
        /// </summary>
        /// <returns>Array of <see cref="PluginPageInfo"/>.</returns>
        public IEnumerable<PluginPageInfo> GetViews()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = "settings",
                    EmbeddedResourcePath = $"{GetType().Namespace}.Config.settings.html"
                }
            };
        }
        
        /// <summary>
        /// Override UpdateConfiguration to preserve cache bust counter and config version.
        /// </summary>
        /// <param name="configuration">The new configuration to save</param>
        public override void UpdateConfiguration(BasePluginConfiguration configuration)
        {
            if (configuration is PluginConfiguration pluginConfig)
            {
                var currentConfig = base.Configuration;

                // Handle cache busting when developer mode is turned ON
                if (!currentConfig.DeveloperMode && pluginConfig.DeveloperMode)
                {
                    pluginConfig.CacheBustCounter = currentConfig.CacheBustCounter + 1;
                }
                else
                {
                    pluginConfig.CacheBustCounter = currentConfig.CacheBustCounter;
                }

                // Preserve ConfigVersion unless it's being explicitly updated
                if (string.IsNullOrEmpty(pluginConfig.ConfigVersion))
                {
                    pluginConfig.ConfigVersion = currentConfig.ConfigVersion;
                }
            }

            base.UpdateConfiguration(configuration);
        }

        /// <summary>
        /// Increment the cache bust counter and save configuration.
        /// </summary>
        public void BustCache()
        {
            var config = base.Configuration;
            config.CacheBustCounter++;
            base.UpdateConfiguration(config);
        }

        /// <summary>
        /// Get the current plugin version.
        /// </summary>
        public string GetCurrentPluginVersion()
        {
            return base.Version?.ToString() ?? "0.0.0";
        }

        /// <summary>
        /// Perform migrations based on configuration version.
        /// </summary>
        private void PerformMigrations()
        {
            try
            {
                var config = base.Configuration;
                var currentVersion = GetCurrentPluginVersion();
                var configVersion = config.ConfigVersion ?? "0.0.0";
                
                bool migrationNeeded = false;
                
                if (CompareVersions(configVersion, "2.3.0.0") < 0)
                {
                    migrationNeeded = PerformEnabledPropertyMigration();
                }
                
                if (migrationNeeded || string.IsNullOrEmpty(config.ConfigVersion))
                {
                    config.ConfigVersion = currentVersion;
                    base.SaveConfiguration();
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Migration: Convert legacy Enabled properties to plugin configuration.
        /// </summary>
        private bool PerformEnabledPropertyMigration()
        {
            var config = base.Configuration;
            bool migrationPerformed = false;

            foreach (var section in config.SectionSettings)
            {
                // Check if Enabled plugin configuration already exists
                var configList = section.PluginConfigurations?.ToList() ?? new List<PluginConfigurationEntry>();
                var enabledConfig = configList.FirstOrDefault(pc => pc.Key == "Enabled");
                
                if (enabledConfig != null)
                {
                    // Update synthetic Enabled config if it exists to match legacy value
                    var legacyValue = section.Enabled.ToString().ToLowerInvariant();
                    if (enabledConfig.Value != legacyValue || enabledConfig.Type != "boolean")
                    {
                        enabledConfig.Value = legacyValue;
                        enabledConfig.Type = "boolean";
                        enabledConfig.AllowUserOverride = section.AllowUserOverride;
                        section.PluginConfigurations = configList.ToArray();
                        migrationPerformed = true;
                    }
                }
                else
                {
                    // Create new Enabled plugin configuration
                    configList.Add(new PluginConfigurationEntry
                    {
                        Key = "Enabled",
                        Value = section.Enabled.ToString().ToLowerInvariant(),
                        Type = "boolean",
                        AllowUserOverride = section.AllowUserOverride
                    });
                    section.PluginConfigurations = configList.ToArray();
                    migrationPerformed = true;
                }
            }

            return migrationPerformed;
        }

        /// <summary>
        /// Compare two version strings. Returns negative if version1 < version2, positive if version1 > version2, and 0 if equal.
        /// </summary>
        private static int CompareVersions(string version1, string version2)
        {
            try
            {
                var v1 = new Version(version1);
                var v2 = new Version(version2);
                return v1.CompareTo(v2);
            }
            catch
            {
                return string.Compare(version1, version2, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}