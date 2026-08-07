using System.Reflection;
using System.Runtime.Loader;
using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Library;
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
        
        public override Guid Id => Guid.Parse("b8298e01-2697-407a-b44d-aa8dc795e850");

        public override string Name => "Home Screen Sections";

        public static HomeScreenSectionsPlugin Instance { get; private set; } = null!;
        
        internal IServiceProvider ServiceProvider { get; set; }
    
        public HomeScreenSectionsPlugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, IServerConfigurationManager serverConfigurationManager, IServiceProvider serviceProvider, IHomeScreenManager homeScreenManager, ITranslationManager translationManager) : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
            ServerConfigurationManager = serverConfigurationManager;
            ServiceProvider = serviceProvider;
            
            homeScreenManager.RegisterBuiltInResultsDelegates();

            string homeScreenSectionsConfigDir = Path.Combine(applicationPaths.PluginConfigurationsPath, "Jellyfin.Plugin.HomeScreenSections");
            Directory.CreateDirectory(homeScreenSectionsConfigDir);

            translationManager.Initialize();
            EnsurePluginPagesRegistration(applicationPaths.PluginConfigurationsPath);
        }

        private void EnsurePluginPagesRegistration(string pluginConfigurationsPath)
        {
            const int pluginPageConfigVersion = 1;
            string pluginPagesConfig = Path.Combine(pluginConfigurationsPath, "Jellyfin.Plugin.PluginPages", "config.json");

            JObject config = LoadOrCreatePluginPagesConfig(pluginPagesConfig);
            if (!config.ContainsKey("pages"))
            {
                config.Add("pages", new JArray());
            }

            JArray pages = config.Value<JArray>("pages")!;
            if (pages.FirstOrDefault(x =>
                    string.Equals(x.Value<string>("Id"), typeof(HomeScreenSectionsPlugin).Namespace, StringComparison.Ordinal)) is JObject hssPageConfig
                && (hssPageConfig.Value<int?>("Version") ?? 0) < pluginPageConfigVersion)
            {
                pages.Remove(hssPageConfig);
            }

            if (pages.Any(x => string.Equals(x.Value<string>("Id"), typeof(HomeScreenSectionsPlugin).Namespace, StringComparison.Ordinal)))
            {
                return;
            }

            pages.Add(CreatePluginPageEntry(pluginPageConfigVersion));
            File.WriteAllText(pluginPagesConfig, config.ToString(Formatting.Indented));
        }

        private static JObject LoadOrCreatePluginPagesConfig(string pluginPagesConfig)
        {
            if (!File.Exists(pluginPagesConfig))
            {
                new FileInfo(pluginPagesConfig).Directory?.Create();
                return new JObject();
            }

            return JObject.Parse(File.ReadAllText(pluginPagesConfig));
        }

        private JObject CreatePluginPageEntry(int pluginPageConfigVersion)
        {
            Assembly? pluginPagesAssembly = AssemblyLoadContext.All
                .SelectMany(x => x.Assemblies)
                .FirstOrDefault(x => x.FullName?.Contains("Jellyfin.Plugin.PluginPages", StringComparison.Ordinal) ?? false);

            Version earliestVersionWithSubUrls = new Version("2.4.1.0");
            bool supportsSubUrls = pluginPagesAssembly != null
                && pluginPagesAssembly.GetName().Version >= earliestVersionWithSubUrls;

            string rootUrl = ServerConfigurationManager.GetNetworkConfiguration().BaseUrl.TrimStart('/').Trim();
            if (!string.IsNullOrEmpty(rootUrl))
            {
                rootUrl = $"/{rootUrl}";
            }

            return new JObject
            {
                { "Id", typeof(HomeScreenSectionsPlugin).Namespace },
                { "Url", $"{(supportsSubUrls ? "" : rootUrl)}/ModularHomeViews/settings" },
                { "DisplayText", "Modular Home" },
                { "Icon", "ballot" },
                { "Version", pluginPageConfigVersion }
            };
        }

        public IEnumerable<PluginPageInfo> GetPages()
        {
            string? prefix = GetType().Namespace;

            yield return new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = $"{prefix}.Configuration.config.html",
                EnableInMainMenu = true
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
            }

            base.UpdateConfiguration(configuration);

            // Section order/enablement lives in this config — drop in-memory home section pages
            // so the next home load uses the new settings immediately.
            ClearUserSectionsDataCache();
        }

        /// <summary>
        /// Increment the cache bust counter and save configuration.
        /// </summary>
        public void BustCache()
        {
            var config = base.Configuration;
            config.CacheBustCounter++;
            base.UpdateConfiguration(config);
            ClearUserSectionsDataCache();
        }

        /// <summary>
        /// Clears the in-memory per-user home section page cache.
        /// </summary>
        public void ClearUserSectionsDataCache()
        {
            try
            {
                if (ServiceProvider?.GetService(typeof(Data.UserSectionsDataCache)) is Data.UserSectionsDataCache cache)
                {
                    cache.Clear();
                }
            }
            catch (InvalidOperationException)
            {
                // Cache clear is best-effort during config updates / early startup.
            }
        }

        /// <summary>
        /// Get the current plugin version.
        /// </summary>
        public string GetCurrentPluginVersion()
        {
            return base.Version?.ToString() ?? "0.0.0";
        }
    }
}