using System.Diagnostics;
using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Helpers;
using Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen
{
    /// <summary>
    /// Manager for the Modular Home Screen.
    /// </summary>
    public class HomeScreenManager : IHomeScreenManager
    {
        private Dictionary<string, IHomeScreenSection> m_delegates = new Dictionary<string, IHomeScreenSection>();
        private Dictionary<Guid, bool> m_userFeatureEnabledStates = new Dictionary<Guid, bool>();

        private readonly IServiceProvider m_serviceProvider;
        private readonly IApplicationPaths m_applicationPaths;
        private readonly ILogger m_logger;

        private const string c_settingsFile = "ModularHomeSettings.json";

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="serviceProvider">Instance of the <see cref="IServiceProvider"/> interface.</param>
        /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
        public HomeScreenManager(IServiceProvider serviceProvider, IApplicationPaths applicationPaths, ILogger<HomeScreenManager> logger)
        {
            m_logger = logger;
            m_serviceProvider = serviceProvider;
            m_applicationPaths = applicationPaths;

            string userFeatureEnabledPath = Path.Combine(m_applicationPaths.PluginConfigurationsPath, typeof(HomeScreenSectionsPlugin).Namespace!, "userFeatureEnabled.json");
            if (File.Exists(userFeatureEnabledPath))
            {
                m_userFeatureEnabledStates = JsonConvert.DeserializeObject<Dictionary<Guid, bool>>(File.ReadAllText(userFeatureEnabledPath)) ?? new Dictionary<Guid, bool>();
            }

            RegisterResultsDelegate<MyMediaSection>();
            RegisterResultsDelegate<ContinueWatchingSection>();
            RegisterResultsDelegate<NextUpSection>();
            RegisterResultsDelegate<RecentlyAddedMoviesSection>();
            RegisterResultsDelegate<RecentlyAddedShowsSection>();
            RegisterResultsDelegate<RecentlyAddedAlbumsSection>();
            RegisterResultsDelegate<RecentlyAddedArtistsSection>();
            RegisterResultsDelegate<RecentlyAddedBooksSection>();
            RegisterResultsDelegate<RecentlyAddedAudioBooksSection>();
            RegisterResultsDelegate<LatestMoviesSection>();
            RegisterResultsDelegate<LatestShowsSection>();
            RegisterResultsDelegate<LatestAlbumsSection>();
            RegisterResultsDelegate<LatestBooksSection>();
            RegisterResultsDelegate<LatestAudioBooksSection>();
            RegisterResultsDelegate<BecauseYouWatchedSection>();
            RegisterResultsDelegate<LiveTvSection>();
            RegisterResultsDelegate<MyListSection>();
            RegisterResultsDelegate<WatchAgainSection>();
            
            RegisterResultsDelegate<DiscoverSection>();
            RegisterResultsDelegate<DiscoverMoviesSection>();
            RegisterResultsDelegate<DiscoverTvSection>();
            
            RegisterResultsDelegate<UpcomingShowsSection>();
            RegisterResultsDelegate<UpcomingMoviesSection>();
            RegisterResultsDelegate<UpcomingMusicSection>();
            RegisterResultsDelegate<UpcomingBooksSection>();
            
            RegisterResultsDelegate<GenreSection>();
            // Removed from public access while its still in dev.
            //RegisterResultsDelegate<TopTenSection>();
        }

        /// <inheritdoc/>
        public IEnumerable<IHomeScreenSection> GetSectionTypes()
        {
            return m_delegates.Values;
        }

        /// <inheritdoc/>
        public QueryResult<BaseItemDto> InvokeResultsDelegate(string key, HomeScreenSectionPayload payload, IQueryCollection queryCollection)
        {
            if (m_delegates.ContainsKey(key))
            {
                return m_delegates[key].GetResults(payload, queryCollection);
            }

            return new QueryResult<BaseItemDto>(Array.Empty<BaseItemDto>());
        }

        /// <inheritdoc/>
        public void RegisterResultsDelegate<T>() where T : IHomeScreenSection
        {
            T handler = ActivatorUtilities.CreateInstance<T>(m_serviceProvider);

            RegisterResultsDelegate(handler);
        }

        public void RegisterResultsDelegate<T>(T handler) where T : IHomeScreenSection
        {
            if (handler.Section != null)
            {
                var enhancedHandler = new SyntheticOptionInjector(handler);
                m_delegates[handler.Section] = enhancedHandler;
            }
        }

        public void RegisterResultsDelegate(Type homeScreenSectionType)
        {
            IHomeScreenSection handler = (IHomeScreenSection)ActivatorUtilities.CreateInstance(m_serviceProvider, homeScreenSectionType);

            if (handler.Section != null)
            {
                if (!m_delegates.ContainsKey(handler.Section))
                {
                    var enhancedHandler = new SyntheticOptionInjector(handler);
                    m_delegates.Add(handler.Section, enhancedHandler);
                }
                else
                {
                    throw new Exception($"Section type '{handler.Section}' has already been registered to type '{m_delegates[handler.Section].GetType().FullName}'.");
                }
            }
        }

        /// <inheritdoc/>
        public bool GetUserFeatureEnabled(Guid userId)
        {
            if (m_userFeatureEnabledStates.ContainsKey(userId))
            {
                return m_userFeatureEnabledStates[userId];
            }

            m_userFeatureEnabledStates.Add(userId, false);

            return false;
        }

        /// <inheritdoc/>
        public void SetUserFeatureEnabled(Guid userId, bool enabled)
        {
            if (!m_userFeatureEnabledStates.ContainsKey(userId))
            {
                m_userFeatureEnabledStates.Add(userId, enabled);
            }

            m_userFeatureEnabledStates[userId] = enabled;

            string userFeatureEnabledPath = Path.Combine(m_applicationPaths.PluginConfigurationsPath, typeof(HomeScreenSectionsPlugin).Namespace!, "userFeatureEnabled.json");
            new FileInfo(userFeatureEnabledPath).Directory?.Create();
            File.WriteAllText(userFeatureEnabledPath, JObject.FromObject(m_userFeatureEnabledStates).ToString(Formatting.Indented));
        }

        /// <inheritdoc/>
        public ModularHomeUserSettings? GetUserSettings(Guid userId)
        {
            string pluginSettings = Path.Combine(m_applicationPaths.PluginConfigurationsPath, typeof(HomeScreenSectionsPlugin).Namespace!, c_settingsFile);

            ModularHomeUserSettings? settings = new ModularHomeUserSettings
            {
                UserId = userId
            };
            if (File.Exists(pluginSettings))
            {
                JArray settingsArray = JArray.Parse(File.ReadAllText(pluginSettings));

                if (settingsArray.Select(x => JsonConvert.DeserializeObject<ModularHomeUserSettings>(x.ToString())).Any(x => x != null && x.UserId.Equals(userId)))
                {
                    settings = settingsArray.Select(x => JsonConvert.DeserializeObject<ModularHomeUserSettings>(x.ToString())).First(x => x != null && x.UserId.Equals(userId));
                }
            }

            // If there are none enabled by the user then add all the default enabled settings.
            if (settings?.EnabledSections.Count == 0)
            {
                settings.EnabledSections.AddRange(HomeScreenSectionsPlugin.Instance.Configuration.SectionSettings.Where(x => x.Enabled).Select(x => x.SectionId));
            }

            if (settings != null)
            {
                // Get forced sections using unified configuration system
                IEnumerable<SectionSettings> forcedSectionSettings = HomeScreenSectionsPlugin.Instance.Configuration.SectionSettings.Where(x => 
                    !x.IsUserOverrideAllowedUnified("Enabled"));

                foreach (SectionSettings sectionSettings in forcedSectionSettings)
                {
                    if (sectionSettings.IsEnabledByAdmin() && !settings.EnabledSections.Contains(sectionSettings.SectionId))
                    {
                        settings.EnabledSections.Add(sectionSettings.SectionId);
                    }
                    else if (!sectionSettings.IsEnabledByAdmin() && settings.EnabledSections.Contains(sectionSettings.SectionId))
                    {
                        settings.EnabledSections.Remove(sectionSettings.SectionId);
                    }
                }
            }

            // Sync SectionSettings from EnabledSections
            settings?.SyncSectionSettings();
            
            return settings;
        }

        /// <inheritdoc/>
        public bool UpdateUserSettings(Guid userId, ModularHomeUserSettings userSettings)
        {
            m_logger.LogInformation($"Updating user settings for user {userId}");
            m_logger.LogInformation($"Json of user settings received from browser: {JsonConvert.SerializeObject(userSettings)}");
            
            string pluginSettings = Path.Combine(m_applicationPaths.PluginConfigurationsPath, typeof(HomeScreenSectionsPlugin).Namespace!, c_settingsFile);
            m_logger.LogInformation($"Plugin settings file: {pluginSettings}");
            
            FileInfo fInfo = new FileInfo(pluginSettings);
            
            m_logger.LogInformation($"Creating directory: '{fInfo.Directory?.FullName}' if it doesn't exist.");
            fInfo.Directory?.Create();

            JArray settings = new JArray();
            List<ModularHomeUserSettings?> newSettings = new List<ModularHomeUserSettings?>();

            m_logger.LogInformation($"Checking if user settings already exist for user {userId} and reading it if so.");
            if (File.Exists(pluginSettings))
            {
                m_logger.LogInformation($"User settings file exists.");
                settings = JArray.Parse(File.ReadAllText(pluginSettings));
                
                m_logger.LogInformation($"Parsed user settings: {settings.ToString(Formatting.None)}");
                newSettings = settings.Select(x => JsonConvert.DeserializeObject<ModularHomeUserSettings>(x.ToString())).ToList()!;
                
                m_logger.LogInformation($"Removing all existing user settings for user {userId} and adding the new one.");
                newSettings.RemoveAll(x => x != null && x.UserId.Equals(userId));

                newSettings.Add(userSettings);

                settings.Clear();
            }

            m_logger.LogInformation($"Adding user settings for user {userId} to the settings array.");
            foreach (ModularHomeUserSettings? userSetting in newSettings)
            {
                settings.Add(JObject.FromObject(userSetting ?? new ModularHomeUserSettings()));
            }

            m_logger.LogInformation($"Writing user settings to file: {pluginSettings}");
            File.WriteAllText(pluginSettings, settings.ToString(Formatting.Indented));

            m_logger.LogInformation($"Content of written settings json: {File.ReadAllText(pluginSettings)}");
            
            m_logger.LogInformation($"User settings updated.");
            return true;
        }

        /// <summary>
        /// Private nested class that injects synthetic configuration options like CustomDisplayText and Enabled into any section.
        /// This ensures all sections have these standard options available organically at registration time.
        /// </summary>
        internal class SyntheticOptionInjector : IHomeScreenSection
        {
            private readonly IHomeScreenSection m_innerSection;
            private readonly List<PluginConfigurationOption>? m_enhancedConfigurationOptions;

            public SyntheticOptionInjector(IHomeScreenSection innerSection)
            {
                m_innerSection = innerSection ?? throw new ArgumentNullException(nameof(innerSection));
                DisplayText = m_innerSection.DisplayText;
                AdditionalData = m_innerSection.AdditionalData;
                m_enhancedConfigurationOptions = CreateEnhancedConfigurationOptions();
            }

            public string? Section => m_innerSection.Section;
            public string? DisplayText { get; set; }
            public int? Limit => m_innerSection.Limit;
            public string? Route => m_innerSection.Route;
            public string? AdditionalData { get; set; }
            public object? OriginalPayload => m_innerSection.OriginalPayload;
            
            public QueryResult<BaseItemDto> GetResults(HomeScreenSectionPayload payload, IQueryCollection queryCollection)
            {
                return m_innerSection.GetResults(payload, queryCollection);
            }

            public IHomeScreenSection CreateInstance(Guid? userId, IEnumerable<IHomeScreenSection>? otherInstances = null)
            {
                var innerInstance = m_innerSection.CreateInstance(userId, otherInstances);
                if (innerInstance == null)
                {
                    return null;
                }
                return new SyntheticOptionInjector(innerInstance);
            }

            public HomeScreenSectionInfo GetInfo()
            {
                return m_innerSection.GetInfo();
            }

            /// <summary>
            /// Returns configuration options with synthetic options injected.
            /// This is where the injection happens - synthetic options are added organically.
            /// </summary>
            public IEnumerable<PluginConfigurationOption>? GetConfigurationOptions()
            {
                return m_enhancedConfigurationOptions ?? new List<PluginConfigurationOption>();
            }

            /// <summary>
            /// Creates the configuration options by combining intrinsic options with synthetic ones.
            /// Synthetic options are only added if they don't already exist.
            /// </summary>
            private List<PluginConfigurationOption> CreateEnhancedConfigurationOptions()
            {
                var originalOptions = m_innerSection.GetConfigurationOptions()?.ToList() ?? new List<PluginConfigurationOption>();

                bool enabledByDefault = true;
                var sectionInfo = m_innerSection.GetInfo();
                if (sectionInfo.EnableByDefault.HasValue)
                {
                    enabledByDefault = sectionInfo.EnableByDefault.Value;
                }

                // Create synthetic options with section-specific defaults
                var syntheticOptions = CreateSyntheticOptions(m_innerSection.DisplayText, enabledByDefault);

                foreach (var syntheticOption in syntheticOptions)
                {
                    if (!originalOptions.Any(o => string.Equals(o.Key, syntheticOption.Key, StringComparison.OrdinalIgnoreCase)))
                    {
                        if (!m_innerSection.SupportsEnableRewatching && string.Equals(syntheticOption.Key, "EnableRewatching", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        
                        originalOptions.Insert(0, syntheticOption); // Insert at beginning for consistency
                    }
                }

                return originalOptions;
            }

            /// <summary>
            /// Creates the standard synthetic options that all sections should have.
            /// </summary>
            /// <param name="sectionDisplayText">The section's default display text to use as CustomDisplayText default</param>
            /// <param name="enabledByDefault">Whether the section should be enabled by default</param>
            private static List<PluginConfigurationOption> CreateSyntheticOptions(string? sectionDisplayText = null, bool enabledByDefault = true)
            {
                return new List<PluginConfigurationOption>
                {
                    new PluginConfigurationOption
                    {
                        Key = "SectionHeaderDisplay",
                        Name = "Section Header Display",
                        Description = "Control how the section header is displayed",
                        Type = PluginConfigurationType.Dropdown,
                        AllowUserOverride = true,
                        DefaultValue = "ShowWithNavigation",
                        IsAdvanced = true,
                        DropdownOptions = new[] { "ShowWithNavigation", "ShowWithoutNavigation", "Hide" },
                        DropdownLabels = new[] { "Show with Navigation", "Show without Navigation", "Hide" }
                    },
                    new PluginConfigurationOption
                    {
                        Key = "CustomDisplayText",
                        Name = "Custom Display Name",
                        Description = "Display a custom name for this section",
                        Type = PluginConfigurationType.TextBox,
                        AllowUserOverride = true,
                        DefaultValue = sectionDisplayText ?? "",
                        IsAdvanced = true,
                        MaxLength = 64
                    },
                    new PluginConfigurationOption
                    {
                        Key = "Enabled",
                        Name = "Enabled",
                        Description = "Enable this section on your home screen",
                        Type = PluginConfigurationType.Checkbox,
                        AllowUserOverride = true,
                        DefaultValue = enabledByDefault,
                        IsAdvanced = false
                    },
                    new PluginConfigurationOption
                    {
                        Key = "EnableRewatching",
                        Name = "Enable Rewatching",
                        Description = "Enable showing already watched episodes",
                        Type = PluginConfigurationType.Checkbox,
                        AllowUserOverride = true,
                        DefaultValue = false,
                        IsAdvanced = false
                    }
                };
            }

            public Type GetSectionType() => m_innerSection.GetSectionType();
        }
    }
}
