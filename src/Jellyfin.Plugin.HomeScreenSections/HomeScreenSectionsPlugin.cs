using Jellyfin.Plugin.HomeScreenSections.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.HomeScreenSections
{
    public class HomeScreenSectionsPlugin : BasePlugin<PluginConfiguration>, IPlugin, IHasPluginConfiguration, IHasWebPages
    {
        public override Guid Id => Guid.Parse("b8298e01-2697-407a-b44d-aa8dc795e850");

        public override string Name => "Home Screen Sections";

        public static HomeScreenSectionsPlugin Instance { get; private set; } = null!;

        internal IServerConfigurationManager ServerConfigurationManager { get; private set; }

        internal IServiceProvider ServiceProvider { get; set; }

        private readonly ILogger<HomeScreenSectionsPlugin> m_logger;

        /// <summary>
        /// Override Configuration property to deduplicate networks on load.
        /// </summary>
        public new PluginConfiguration Configuration
        {
            get
            {
                var config = base.Configuration;

                if (config.JellyseerrNetworks != null && config.JellyseerrNetworks.Count > 0)
                {
                    var uniqueNetworks = config.JellyseerrNetworks
                        .GroupBy(n => n.Id)
                        .Select(g =>
                        {
                            var enabled = g.FirstOrDefault(n => n.Enabled);
                            return enabled ?? g.First();
                        })
                        .ToList();

                    if (uniqueNetworks.Count != config.JellyseerrNetworks.Count)
                    {
                        m_logger.LogWarning($"Deduplicating networks on load: {config.JellyseerrNetworks.Count} -> {uniqueNetworks.Count}");
                        config.JellyseerrNetworks = uniqueNetworks;
                        base.UpdateConfiguration(config);
                    }
                }

                return config;
            }
        }

        public HomeScreenSectionsPlugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, IServerConfigurationManager serverConfigurationManager, IServiceProvider serviceProvider, ILogger<HomeScreenSectionsPlugin> logger) : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
            m_logger = logger;
            
            ServerConfigurationManager = serverConfigurationManager;
            ServiceProvider = serviceProvider;
        
            string homeScreenSectionsConfigDir = Path.Combine(applicationPaths.PluginConfigurationsPath, "Jellyfin.Plugin.HomeScreenSections");
            if (!Directory.Exists(homeScreenSectionsConfigDir))
            {
                Directory.CreateDirectory(homeScreenSectionsConfigDir);
            }
            
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
        /// Override UpdateConfiguration to preserve cache bust counter.
        /// </summary>
        /// <param name="configuration">The new configuration to save.</param>
        public override void UpdateConfiguration(BasePluginConfiguration configuration)
        {
            if (configuration is PluginConfiguration pluginConfig)
            {
                var currentConfig = base.Configuration;

                if (!currentConfig.DeveloperMode && pluginConfig.DeveloperMode)
                {
                    pluginConfig.CacheBustCounter = currentConfig.CacheBustCounter + 1;
                }
                else
                {
                    pluginConfig.CacheBustCounter = currentConfig.CacheBustCounter;
                }

                if (pluginConfig.JellyseerrNetworks != null && pluginConfig.JellyseerrNetworks.Count > 0)
                {
                    var uniqueNetworks = pluginConfig.JellyseerrNetworks
                        .GroupBy(n => n.Id)
                        .Select(g =>
                        {
                            var enabled = g.FirstOrDefault(n => n.Enabled);
                            return enabled ?? g.First();
                        })
                        .ToList();

                    if (uniqueNetworks.Count != pluginConfig.JellyseerrNetworks.Count)
                    {
                        m_logger.LogWarning($"Deduplicating networks: {pluginConfig.JellyseerrNetworks.Count} -> {uniqueNetworks.Count}");
                        pluginConfig.JellyseerrNetworks = uniqueNetworks;
                    }
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