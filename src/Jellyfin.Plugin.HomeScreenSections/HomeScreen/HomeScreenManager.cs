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
        private Dictionary<string, IHomeScreenSection> _delegates = new(StringComparer.Ordinal);
        private Dictionary<Guid, bool> _userFeatureEnabledStates = [];

        private readonly IServiceProvider _serviceProvider;
        private readonly IApplicationPaths _applicationPaths;
        private readonly ILogger _logger;

        private const string SettingsFile = "ModularHomeSettings.json";

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="serviceProvider">Instance of the <see cref="IServiceProvider"/> interface.</param>
        /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
        public HomeScreenManager(IServiceProvider serviceProvider, IApplicationPaths applicationPaths, ILogger<HomeScreenManager> logger)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _applicationPaths = applicationPaths;

            string userFeatureEnabledPath = Path.Combine(_applicationPaths.PluginConfigurationsPath, typeof(HomeScreenSectionsPlugin).Namespace!, "userFeatureEnabled.json");
            if (File.Exists(userFeatureEnabledPath))
            {
                _userFeatureEnabledStates = JsonConvert.DeserializeObject<Dictionary<Guid, bool>>(File.ReadAllText(userFeatureEnabledPath)) ?? [];
            }
        }
        
        public void RegisterBuiltInResultsDelegates()
        {
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
            return _delegates.Values;
        }

        public IHomeScreenSection? GetSection(string sectionName)
        {
            return _delegates.GetValueOrDefault(sectionName);
        }

        /// <inheritdoc/>
        public QueryResult<BaseItemDto> InvokeResultsDelegate(string key, HomeScreenSectionPayload payload, IQueryCollection queryCollection)
        {
            if (_delegates.TryGetValue(key, out IHomeScreenSection? section))
            {
                return section.GetResults(payload, queryCollection);
            }

            return new QueryResult<BaseItemDto>([]);
        }

        /// <inheritdoc/>
        public void RegisterResultsDelegate<T>() where T : IHomeScreenSection
        {
            T handler = ActivatorUtilities.CreateInstance<T>(_serviceProvider);

            RegisterResultsDelegate(handler);
        }

        public void RegisterResultsDelegate<T>(T handler) where T : IHomeScreenSection
        {
            if (handler.Section != null)
            {
                _delegates[handler.Section] = handler;
            }
        }

        public void RegisterResultsDelegate(Type homeScreenSectionType)
        {
            IHomeScreenSection handler = (IHomeScreenSection)ActivatorUtilities.CreateInstance(_serviceProvider, homeScreenSectionType);

            if (handler.Section != null)
            {
                if (!_delegates.TryAdd(handler.Section, handler))
                {
                    throw new InvalidOperationException($"Section type '{handler.Section}' has already been registered to type '{_delegates[handler.Section].GetType().FullName}'.");
                }
            }
        }

        /// <inheritdoc/>
        public bool GetUserFeatureEnabled(Guid userId)
        {
            if (_userFeatureEnabledStates.TryGetValue(userId, out bool enabled))
            {
                return enabled;
            }

            _userFeatureEnabledStates[userId] = false;

            return false;
        }

        /// <inheritdoc/>
        public void SetUserFeatureEnabled(Guid userId, bool enabled)
        {
            _userFeatureEnabledStates[userId] = enabled;

            string userFeatureEnabledPath = Path.Combine(_applicationPaths.PluginConfigurationsPath, typeof(HomeScreenSectionsPlugin).Namespace!, "userFeatureEnabled.json");
            new FileInfo(userFeatureEnabledPath).Directory?.Create();
            File.WriteAllText(userFeatureEnabledPath, JObject.FromObject(_userFeatureEnabledStates).ToString(Formatting.Indented));
        }

        /// <inheritdoc/>
        public ModularHomeUserSettings? GetUserSettings(Guid userId)
        {
            string pluginSettings = Path.Combine(_applicationPaths.PluginConfigurationsPath, typeof(HomeScreenSectionsPlugin).Namespace!, SettingsFile);

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
            PluginLog.UpdatingUserSettings(_logger, userId);
            if (_logger.IsEnabled(LogLevel.Information))
            {
                string userSettingsJson = JsonConvert.SerializeObject(userSettings);
                PluginLog.UserSettingsJsonReceived(_logger, userSettingsJson);
            }
            
            string pluginSettings = Path.Combine(_applicationPaths.PluginConfigurationsPath, typeof(HomeScreenSectionsPlugin).Namespace!, SettingsFile);
            PluginLog.PluginSettingsFile(_logger, pluginSettings);
            
            FileInfo fInfo = new FileInfo(pluginSettings);
            
            PluginLog.CreatingSettingsDirectory(_logger, fInfo.Directory?.FullName);
            fInfo.Directory?.Create();

            JArray settings = new JArray();
            // Seed with the incoming settings so a first-ever save (no settings file yet) is
            // not written out as an empty array.
            List<ModularHomeUserSettings?> newSettings = [userSettings];

            PluginLog.CheckingExistingUserSettings(_logger, userId);
            if (File.Exists(pluginSettings))
            {
                PluginLog.UserSettingsFileExists(_logger);
                settings = JArray.Parse(File.ReadAllText(pluginSettings));
                
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    string settingsJson = settings.ToString(Formatting.None);
                    PluginLog.ParsedUserSettings(_logger, settingsJson);
                }

                newSettings = settings.Select(x => JsonConvert.DeserializeObject<ModularHomeUserSettings>(x.ToString())).ToList()!;
                
                PluginLog.RemovingExistingUserSettings(_logger, userId);
                newSettings.RemoveAll(x => x != null && x.UserId.Equals(userId));

                newSettings.Add(userSettings);

                settings.Clear();
            }

            PluginLog.AddingUserSettings(_logger, userId);
            foreach (ModularHomeUserSettings? userSetting in newSettings)
            {
                settings.Add(JObject.FromObject(userSetting ?? new ModularHomeUserSettings()));
            }

            PluginLog.WritingUserSettings(_logger, pluginSettings);
            File.WriteAllText(pluginSettings, settings.ToString(Formatting.Indented));

            if (_logger.IsEnabled(LogLevel.Information))
            {
                string writtenJson = File.ReadAllText(pluginSettings);
                PluginLog.WrittenSettingsContent(_logger, writtenJson);
            }
            
            PluginLog.UserSettingsUpdated(_logger);
            return true;
        }
    }
}
