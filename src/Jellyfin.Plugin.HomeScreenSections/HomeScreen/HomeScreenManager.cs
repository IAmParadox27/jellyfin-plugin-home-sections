using System.Diagnostics;
using Jellyfin.Plugin.HomeScreenSections.Configuration;
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
            RegisterResultsDelegate<LatestMoviesSection>();
            RegisterResultsDelegate<LatestShowsSection>();
            RegisterResultsDelegate<LatestAlbumsSection>();
            RegisterResultsDelegate<BecauseYouWatchedSection>();
            RegisterResultsDelegate<LiveTvSection>();
            RegisterResultsDelegate<MyListSection>();
            RegisterResultsDelegate<WatchAgainSection>();
            
            RegisterResultsDelegate<DiscoverSection>();
            RegisterResultsDelegate<DiscoverMoviesSection>();
            RegisterResultsDelegate<DiscoverTvSection>();
            
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
                m_delegates[handler.Section] = handler;
            }
        }

        public void RegisterResultsDelegate(Type homeScreenSectionType)
        {
            IHomeScreenSection handler = (IHomeScreenSection)ActivatorUtilities.CreateInstance(m_serviceProvider, homeScreenSectionType);

            if (handler.Section != null)
            {
                if (!m_delegates.ContainsKey(handler.Section))
                {
                    m_delegates.Add(handler.Section, handler);
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
                IEnumerable<SectionSettings> forcedSectionSettings = HomeScreenSectionsPlugin.Instance.Configuration.SectionSettings.Where(x => !x.AllowUserOverride);

                foreach (SectionSettings sectionSettings in forcedSectionSettings)
                {
                    if (sectionSettings.Enabled && !settings.EnabledSections.Contains(sectionSettings.SectionId))
                    {
                        settings.EnabledSections.Add(sectionSettings.SectionId);
                    }
                    else if (!sectionSettings.Enabled && settings.EnabledSections.Contains(sectionSettings.SectionId))
                    {
                        settings.EnabledSections.Remove(sectionSettings.SectionId);
                    }
                }
            }
            
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
    }
}
