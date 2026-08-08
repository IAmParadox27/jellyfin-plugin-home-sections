using System.Diagnostics;
using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections;
using Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections.Extra;
using Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections.Latest;
using Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections.Persons;
using Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections.RecentlyAdded;
using Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections.Upcoming;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Jellyfin.Plugin.HomeScreenSections.Services;
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
        private Dictionary<string, IHomeScreenSection> m_delegates = new(StringComparer.Ordinal);
        private Dictionary<Guid, bool> m_userFeatureEnabledStates = [];

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
                m_userFeatureEnabledStates = JsonConvert.DeserializeObject<Dictionary<Guid, bool>>(File.ReadAllText(userFeatureEnabledPath)) ?? [];
            }
        }
        
        public void RegisterBuiltInResultsDelegates()
        {
            // ponytail: table-driven — was 30 hand-written RegisterResultsDelegate<X>() lines
            Type[] sectionTypes =
            [
                typeof(MyMediaSection), typeof(ContinueWatchingSection), typeof(NextUpSection), typeof(ContinueWatchingNextUpSection),
                typeof(RecentlyAddedMoviesSection), typeof(RecentlyAddedShowsSection), typeof(RecentlyAddedAlbumsSection), typeof(RecentlyAddedArtistsSection), typeof(RecentlyAddedBooksSection), typeof(RecentlyAddedAudioBooksSection), typeof(RecentlyAddedMusicVideosSection),
                typeof(LatestMoviesSection), typeof(LatestShowsSection), typeof(LatestAlbumsSection), typeof(LatestBooksSection), typeof(LatestAudioBooksSection), typeof(LatestMusicVideoSection),
                typeof(BecauseYouWatchedSection), typeof(LiveTvSection), typeof(MyListSection), typeof(WatchAgainSection),
                typeof(DiscoverSection), typeof(DiscoverMoviesSection), typeof(DiscoverTvSection),
                typeof(UpcomingShowsSection), typeof(UpcomingMoviesSection), typeof(UpcomingMusicSection), typeof(UpcomingBooksSection),
                typeof(GenreSection), typeof(MyRequestsSection),
                typeof(FavoritesSection), typeof(RandomUnwatchedSection), typeof(TrendingSection), typeof(RecentlyPlayedSection), typeof(KidsSection), typeof(ComingSoonInLibrarySection), typeof(DecadeSection), typeof(StudioSection), typeof(PlaylistsSection), typeof(UnwatchedCollectionsSection),
            ];

            foreach (Type t in sectionTypes)
            {
                RegisterResultsDelegate(t);
            }
        }

        /// <inheritdoc/>
        public IEnumerable<IHomeScreenSection> GetSectionTypes()
        {
            return m_delegates.Values;
        }

        public IHomeScreenSection? GetSection(string sectionName)
        {
            return m_delegates.GetValueOrDefault(sectionName);
        }

        /// <inheritdoc/>
        public QueryResult<BaseItemDto> InvokeResultsDelegate(string key, HomeScreenSectionPayload payload, IQueryCollection queryCollection)
        {
            if (m_delegates.TryGetValue(key, out IHomeScreenSection? section))
            {
                return section.GetResults(payload, queryCollection);
            }

            return new QueryResult<BaseItemDto>([]);
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
                // Refuse duplicates instead of silently overwriting: an overwrite would let an
                // external RegisterSection call swap out a built-in handler (upstream #258).
                if (!m_delegates.TryAdd(handler.Section, handler))
                {
                    PluginLog.DuplicateSectionRegistration(m_logger, handler.Section, m_delegates[handler.Section].GetType().FullName);
                }
            }
        }

        public void RegisterResultsDelegate(Type homeScreenSectionType)
        {
            IHomeScreenSection handler = (IHomeScreenSection)ActivatorUtilities.CreateInstance(m_serviceProvider, homeScreenSectionType);

            if (handler.Section != null)
            {
                if (!m_delegates.TryAdd(handler.Section, handler))
                {
                    throw new InvalidOperationException($"Section type '{handler.Section}' has already been registered to type '{m_delegates[handler.Section].GetType().FullName}'.");
                }
            }
        }

        /// <inheritdoc/>
        public bool GetUserFeatureEnabled(Guid userId)
        {
            if (m_userFeatureEnabledStates.TryGetValue(userId, out bool enabled))
            {
                return enabled;
            }

            m_userFeatureEnabledStates[userId] = false;

            return false;
        }

        /// <inheritdoc/>
        public void SetUserFeatureEnabled(Guid userId, bool enabled)
        {
            m_userFeatureEnabledStates[userId] = enabled;

            string userFeatureEnabledPath = Path.Combine(m_applicationPaths.PluginConfigurationsPath, typeof(HomeScreenSectionsPlugin).Namespace!, "userFeatureEnabled.json");
            new FileInfo(userFeatureEnabledPath).Directory?.Create();
            File.WriteAllText(userFeatureEnabledPath, JObject.FromObject(m_userFeatureEnabledStates).ToString(Formatting.Indented));
        }

        /// <inheritdoc/>
        public ModularHomeUserSettings? GetUserSettings(Guid userId)
        {
            string pluginSettings = Path.Combine(m_applicationPaths.PluginConfigurationsPath, typeof(HomeScreenSectionsPlugin).Namespace!, c_settingsFile);

            IEnumerable<SectionSettings> adminLockedSections =
                HomeScreenSectionsPlugin.Instance.Configuration.SectionSettings.Where(x => !x.AllowUserOverride);
            IEnumerable<SectionSettings> defaultEnabledSections =
                HomeScreenSectionsPlugin.Instance.Configuration.SectionSettings.Where(x => x.Enabled);
            
            ModularHomeUserSettings? settings = new ModularHomeUserSettings
            {
                UserId = userId,
                LockedSections = adminLockedSections.Select(x => x.SectionId).ToList(),
                DefaultEnabledSections = defaultEnabledSections.Select(x => x.SectionId).ToList()
            };
            if (File.Exists(pluginSettings))
            {
                JArray settingsArray = JArray.Parse(File.ReadAllText(pluginSettings));

                if (settingsArray.Select(x => JsonConvert.DeserializeObject<ModularHomeUserSettings>(x.ToString())).Any(x => x != null && x.UserId.Equals(userId)))
                {
                    settings = settingsArray.Select(x => JsonConvert.DeserializeObject<ModularHomeUserSettings>(x.ToString())).First(x => x != null && x.UserId.Equals(userId));
                    if (settings != null && settings.SectionOrder == null)
                    {
                        settings.SectionOrder = [];
                    }
                }
            }

            // If there are none enabled by the user then add all the default enabled settings.
            if (settings?.EnabledSections.Count == 0)
            {
                foreach (string sectionId in HomeScreenSectionsPlugin.Instance.Configuration.SectionSettings
                             .Where(x => x.Enabled)
                             .Select(x => x.SectionId))
                {
                    settings.EnabledSections.Add(sectionId);
                }
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
            PluginLog.UpdatingUserSettings(m_logger, userId);
            if (m_logger.IsEnabled(LogLevel.Information))
            {
                string userSettingsJson = JsonConvert.SerializeObject(userSettings);
                PluginLog.UserSettingsJsonReceived(m_logger, userSettingsJson);
            }
            
            string pluginSettings = Path.Combine(m_applicationPaths.PluginConfigurationsPath, typeof(HomeScreenSectionsPlugin).Namespace!, c_settingsFile);
            PluginLog.PluginSettingsFile(m_logger, pluginSettings);
            
            FileInfo fInfo = new FileInfo(pluginSettings);
            
            PluginLog.CreatingSettingsDirectory(m_logger, fInfo.Directory?.FullName);
            fInfo.Directory?.Create();

            JArray settings = new JArray();
            // Seed with the incoming settings so a first-ever save (no settings file yet) is
            // not written out as an empty array.
            List<ModularHomeUserSettings?> newSettings = [userSettings];

            PluginLog.CheckingExistingUserSettings(m_logger, userId);
            if (File.Exists(pluginSettings))
            {
                PluginLog.UserSettingsFileExists(m_logger);
                settings = JArray.Parse(File.ReadAllText(pluginSettings));
                
                if (m_logger.IsEnabled(LogLevel.Information))
                {
                    string settingsJson = settings.ToString(Formatting.None);
                    PluginLog.ParsedUserSettings(m_logger, settingsJson);
                }

                newSettings = settings.Select(x => JsonConvert.DeserializeObject<ModularHomeUserSettings>(x.ToString())).ToList()!;
                
                PluginLog.RemovingExistingUserSettings(m_logger, userId);
                newSettings.RemoveAll(x => x != null && x.UserId.Equals(userId));

                newSettings.Add(userSettings);

                settings.Clear();
            }

            PluginLog.AddingUserSettings(m_logger, userId);
            foreach (ModularHomeUserSettings? userSetting in newSettings)
            {
                settings.Add(JObject.FromObject(userSetting ?? new ModularHomeUserSettings()));
            }

            PluginLog.WritingUserSettings(m_logger, pluginSettings);
            File.WriteAllText(pluginSettings, settings.ToString(Formatting.Indented));

            if (m_logger.IsEnabled(LogLevel.Information))
            {
                string writtenJson = File.ReadAllText(pluginSettings);
                PluginLog.WrittenSettingsContent(m_logger, writtenJson);
            }
            
            PluginLog.UserSettingsUpdated(m_logger);
            return true;
        }
    }
}
