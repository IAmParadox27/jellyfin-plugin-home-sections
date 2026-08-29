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
        
        internal Dictionary<string, bool> CollectionFolderMixedStatus { get; set; } = new Dictionary<string, bool>();
        
        private IApplicationPaths m_applicationPaths;
        
        public HomeScreenSectionsPlugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, IServerConfigurationManager serverConfigurationManager, IServiceProvider serviceProvider, IHomeScreenManager homeScreenManager, ITranslationManager translationManager) : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
            
            ServerConfigurationManager = serverConfigurationManager;
            ServiceProvider = serviceProvider;
            m_applicationPaths = applicationPaths;
            
            homeScreenManager.RegisterBuiltInResultsDelegates();
        
            string homeScreenSectionsConfigDir = Path.Combine(applicationPaths.PluginConfigurationsPath, "Jellyfin.Plugin.HomeScreenSections");
            if (!Directory.Exists(homeScreenSectionsConfigDir))
            {
                Directory.CreateDirectory(homeScreenSectionsConfigDir);
            }
        
            translationManager.Initialize();
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
    }
}