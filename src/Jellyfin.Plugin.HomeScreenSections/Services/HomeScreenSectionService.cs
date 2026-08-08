using System.Collections.Concurrent;
using System.Threading.Channels;
using Jellyfin.Extensions;
using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Data;
using Jellyfin.Plugin.HomeScreenSections.Helpers;
using Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections;
using Jellyfin.Plugin.HomeScreenSections.JellyfinVersionSpecific;
using Jellyfin.Plugin.HomeScreenSections.Library;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.HomeScreenSections.Services
{
    public class HomeScreenSectionService
    {
        private readonly IServerConfigurationManager _configurationManager;
        private readonly IHomeScreenManager _homeScreenManager;
        private readonly ILogger<HomeScreenSectionsPlugin> _logger;
        private readonly ITranslationManager _translationManager;
        private readonly UserSectionsDataCache _dataCache;
        private readonly IUserManager _userManager;
        private readonly ILibraryManager _libraryManager;
        private readonly IDtoService _dtoService;
        private readonly CollectionManagerProxy _collectionManagerProxy;
        private readonly IPlaylistManager _playlistManager;
    
        public HomeScreenSectionService(
            IHomeScreenManager homeScreenManager,
            ILogger<HomeScreenSectionsPlugin> logger,
            ITranslationManager translationManager,
            UserSectionsDataCache dataCache,
            IServerConfigurationManager configurationManager,
            IUserManager userManager,
            ILibraryManager libraryManager,
            IDtoService dtoService,
            CollectionManagerProxy collectionManagerProxy,
            IPlaylistManager playlistManager)
        {
            _homeScreenManager = homeScreenManager;
            _logger = logger;
            _translationManager = translationManager;
            _dataCache = dataCache;
            _configurationManager = configurationManager;
            _userManager = userManager;
            _libraryManager = libraryManager;
            _dtoService = dtoService;
            _collectionManagerProxy = collectionManagerProxy;
            _playlistManager = playlistManager;
        }

        public IReadOnlyList<HomeScreenSectionInfo>? GetCachedSectionsForUser(Guid userId, string? language, int page, int pageSize, Guid pageHash)
        {
            if (!_dataCache.Cache.TryGetValue(pageHash, out UserSectionsData? userSectionsData))
            {
                return null;
            }
            
            // Make sure that it's flagged as being used, even if we don't return anything here the page is still active
            // as we've received a request for it.
            userSectionsData.LastAccessed = DateTime.UtcNow;
            
            // Check if the userSectionsData has the data we're after
            int[] orderedKeys = userSectionsData.OrderedSections.Keys.OrderBy(x => x).ToArray();

            List<(IHomeScreenSection Section, int ConfiguredOrder)> sectionsToReturn = CollectCohesiveSections(userSectionsData, orderedKeys, out bool isComplete);
            
            sectionsToReturn = sectionsToReturn.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            if ((isComplete && userSectionsData.SectionsInProgress.IsEmpty) || sectionsToReturn.Count == pageSize)
            {
                return sectionsToReturn
                    .Select(x => SectionToInfo(x.Section, x.ConfiguredOrder, language, userId))
                    .ToList();
            }

            // Return nothing if we don't have the complete picture.
            return null;
        }

        public IReadOnlyList<HomeScreenSectionInfo>? MonitorLiveUpdatedSectionsForUser(Guid userId, string? language, int page, int? pageSize = null, Guid? pageHash = null)
        {
            if (pageHash == null)
            {
                pageHash = Guid.NewGuid();
                
                CacheSectionsForUser(userId, pageHash.Value);

                int totalSectionCount = _dataCache.Cache[pageHash.Value].OrderedSections.SelectMany(x => x.Value).Count();
                return GetCachedSectionsForUser(userId, language, 1, totalSectionCount, pageHash.Value);
            }

            EnsureCacheStarted(userId, pageHash.Value);
            WaitUntilCachePresent(pageHash.Value);
            WaitUntilCacheHasStartedWork(pageHash.Value);

            return WaitForPageSections(userId, language, page, pageSize, pageHash.Value);
        }
    
        public void CacheSectionsForUser(Guid userId, Guid? pageHash = null)
        {
            if (_dataCache.Cache.ContainsKey(pageHash ?? Guid.Empty))
            {
                return;
            }
            
            ModularHomeUserSettings? settings = _homeScreenManager.GetUserSettings(userId);

            List<IHomeScreenSection> sectionTypes = _homeScreenManager.GetSectionTypes().Where(x => settings?.EnabledSections.Contains(x.Section ?? string.Empty) ?? false).ToList();

            IGrouping<int, SectionSettings>[] groupedOrderedSections = BuildOrderedSectionGroups(settings);

            UserSectionsData? userSectionsData = pageHash != null
                ? InitializeUserSectionsData(userId, pageHash.Value, groupedOrderedSections)
                : null;
            
            Parallel.ForEach(groupedOrderedSections, orderedSections =>
            {
                PopulateOrderGroup(userId, sectionTypes, orderedSections, userSectionsData);
            });
        }

        /// <summary>
        /// Groups section settings by display order. Uses the user's SectionOrder when set;
        /// otherwise falls back to the admin OrderIndex grouping.
        /// </summary>
        private static IGrouping<int, SectionSettings>[] BuildOrderedSectionGroups(ModularHomeUserSettings? settings)
        {
            List<SectionSettings> adminSettings = HomeScreenSectionsPlugin.Instance.Configuration.SectionSettings.ToList();

            if (settings?.SectionOrder is { Count: > 0 } userOrder)
            {
                Dictionary<string, int> rank = new(StringComparer.Ordinal);
                for (int i = 0; i < userOrder.Count; i++)
                {
                    string id = userOrder[i];
                    if (!string.IsNullOrEmpty(id) && !rank.ContainsKey(id))
                    {
                        rank[id] = i;
                    }
                }

                int next = userOrder.Count;
                return adminSettings
                    .OrderBy(s => rank.TryGetValue(s.SectionId, out int r) ? r : next++)
                    .ThenBy(s => s.OrderIndex)
                    .Select((s, index) => (Settings: s, Order: index))
                    .GroupBy(x => x.Order, x => x.Settings)
                    .ToArray();
            }

            return adminSettings
                .OrderBy(x => x.OrderIndex)
                .GroupBy(x => x.OrderIndex)
                .ToArray();
        }

        private static List<(IHomeScreenSection Section, int ConfiguredOrder)> CollectCohesiveSections(
            UserSectionsData userSectionsData,
            int[] orderedKeys,
            out bool isComplete)
        {
            List<(IHomeScreenSection Section, int ConfiguredOrder)> sectionsToReturn = [];
            isComplete = true;

            for (int i = 0; i < orderedKeys.Length; i++)
            {
                int key = orderedKeys[i];
                int prevKey = i > 0 ? orderedKeys[i - 1] : orderedKeys[i] - 1;

                bool cohesive = (key - prevKey) == 1;
                if (prevKey > 0 && key - prevKey > 1)
                {
                    // If any of the ranges contain both the "key before" and "key after" then we can safely know this is cohesive.
                    if (userSectionsData.OrderIndicesWithoutSections.Any(x => x.Contains(key - 1) && x.Contains(prevKey + 1)))
                    {
                        cohesive = true;
                    }
                }

                if (cohesive)
                {
                    sectionsToReturn.AddRange(userSectionsData.OrderedSections[key].Select(x => (x, key)));
                }
                else
                {
                    isComplete = false;
                    break;
                }
            }

            return sectionsToReturn;
        }

        private void EnsureCacheStarted(Guid userId, Guid pageHash)
        {
            if (!_dataCache.Cache.ContainsKey(pageHash))
            {
                _ = Task.Run(() => CacheSectionsForUser(userId, pageHash));
            }
        }

        private void WaitUntilCachePresent(Guid pageHash)
        {
            while (!_dataCache.Cache.ContainsKey(pageHash))
            {
                Thread.Sleep(10);
            }
        }

        private void WaitUntilCacheHasStartedWork(Guid pageHash)
        {
            // If there's no data at all then we wait until its started.
            while (_dataCache.Cache[pageHash].SectionsInProgress.IsEmpty && _dataCache.Cache[pageHash].OrderedSections.IsEmpty)
            {
                Thread.Sleep(10);
            }
        }

        private IReadOnlyList<HomeScreenSectionInfo>? WaitForPageSections(Guid userId, string? language, int page, int? pageSize, Guid pageHash)
        {
            // We always wait from the start, if we hit a page that's already cached then we'll just return immediately.
            // If its still in progress then we'll wait for it to finish.
            UserSectionsData cache = _dataCache.Cache[pageHash];
            int lowestSectionIndex = Math.Min(
                !_dataCache.Cache[pageHash].OrderedSections.IsEmpty
                    ? _dataCache.Cache[pageHash].OrderedSections.Min(x => x.Key) 
                    : int.MaxValue,
                !_dataCache.Cache[pageHash].SectionsInProgress.IsEmpty
                    ? _dataCache.Cache[pageHash].SectionsInProgress.Min(x => x.Key) 
                    : int.MaxValue);

            for (int i = lowestSectionIndex; i <= cache.MaxOrderIndex; i++)
            {
                if (cache.OrderIndicesWithoutSections.Any(x => x.Contains(i)))
                {
                    continue;
                }

                while (cache.SectionsInProgress.ContainsKey(i))
                {
                    Thread.Sleep(10);
                }
                
                IReadOnlyList<HomeScreenSectionInfo>? sections = GetCachedSectionsForUser(userId, language, page, pageSize ?? cache.OrderedSections.SelectMany(x => x.Value).Count(), pageHash);
                if (sections != null)
                {
                    return sections;
                }
            }
            
            return null;
        }

        private UserSectionsData InitializeUserSectionsData(Guid userId, Guid pageHash, IGrouping<int, SectionSettings>[] groupedOrderedSections)
        {
            UserSectionsData userSectionsData = new UserSectionsData()
            {
                UserId = userId,
                MaxOrderIndex = groupedOrderedSections.Max(x => x.Key)
            };
            
            _dataCache.Cache.TryAdd(pageHash, userSectionsData);

            foreach (int orderIndex in groupedOrderedSections.Select(x => x.Key).OrderBy(x => x))
            {
                userSectionsData.SectionsInProgress.TryAdd(orderIndex, true);
            }

            FillOrderIndicesWithoutSections(userSectionsData);
            return userSectionsData;
        }

        private static void FillOrderIndicesWithoutSections(UserSectionsData userSectionsData)
        {
            int[] sectionIndices = userSectionsData.SectionsInProgress.Keys.OrderBy(x => x).ToArray();
            for (int i = 1; i < sectionIndices.Length; i++)
            {
                int prevIndex = sectionIndices[i - 1];
                int currentIndex = sectionIndices[i];

                if (currentIndex - prevIndex > 1)
                {
                    userSectionsData.OrderIndicesWithoutSections.Add(new IntRange()
                    {
                        Start = prevIndex + 1, 
                        End = currentIndex - 1
                    });
                }
            }
        }

        private void PopulateOrderGroup(
            Guid userId,
            List<IHomeScreenSection> sectionTypes,
            IGrouping<int, SectionSettings> orderedSections,
            UserSectionsData? userSectionsData)
        {
            ConcurrentBag<IHomeScreenSection?> tmpPluginSections = new ConcurrentBag<IHomeScreenSection?>(); // we want these randomly distributed among each other.

            Parallel.ForEach(orderedSections, sectionSettings =>
            {
                CreateSectionInstances(userId, sectionTypes, sectionSettings, tmpPluginSections);
            });

            List<IHomeScreenSection> sectionList = tmpPluginSections.Where(x => x != null).Select(x => x!).ToList();
            sectionList.Shuffle();

            if (userSectionsData != null)
            {
                userSectionsData.OrderedSections.TryAdd(orderedSections.Key, sectionList);
                userSectionsData.SectionsInProgress.Remove(orderedSections.Key, out _);
            }
        }

        private void CreateSectionInstances(
            Guid userId,
            List<IHomeScreenSection> sectionTypes,
            SectionSettings sectionSettings,
            ConcurrentBag<IHomeScreenSection?> tmpPluginSections)
        {
            IHomeScreenSection? sectionType =
                sectionTypes.FirstOrDefault(x => string.Equals(x.Section, sectionSettings.SectionId, StringComparison.Ordinal));

            if (sectionType == null)
            {
                return;
            }

            int instanceCount = 1;
            if (sectionType.Limit > 1)
            {
                Random rnd = new Random();
                instanceCount = rnd.Next(sectionSettings.LowerLimit, sectionSettings.UpperLimit);
            }

            try
            {
                IEnumerable<IHomeScreenSection> instances = sectionType.CreateInstances(userId, instanceCount);

                foreach (IHomeScreenSection sectionInstance in instances)
                {
                    tmpPluginSections.Add(sectionInstance);
                }
            }
            // Isolate section failures so one bad section cannot take down the whole home screen (#128).
            catch (Exception e) when (
                e is InvalidOperationException
                or ArgumentException
                or NullReferenceException
                or KeyNotFoundException
                or NotSupportedException
                or NotImplementedException
                or FormatException
                or TimeoutException
                or IOException
                or HttpRequestException
                or System.Reflection.TargetInvocationException
                or System.Text.Json.JsonException
                or Newtonsoft.Json.JsonException)
            {
                PluginLog.SectionInstanceError(_logger, e, userId, sectionType.Section);
            }
        }

        private HomeScreenSectionInfo SectionToInfo(IHomeScreenSection section, int configuredOrder, string? language, Guid userId)
        {
            HomeScreenSectionInfo info = section.AsInfo();

            info.OrderIndex = configuredOrder;
            info.ViewMode = HomeScreenSectionsPlugin.Instance.Configuration.SectionSettings.FirstOrDefault(y => string.Equals(y.SectionId, info.Section, StringComparison.Ordinal))?.ViewMode ?? info.ViewMode ?? SectionViewMode.Landscape;

            // When a section has no explicit title target, try resolving AdditionalData
            // as an item id, collection/playlist name, or genre name so the web client
            // can make the title open the full list. Failures stay null (no broken link).
            if (info.OriginalPayload == null && !string.IsNullOrWhiteSpace(info.AdditionalData))
            {
                info.OriginalPayload = TryResolveTitleLinkTarget(info.AdditionalData, userId);
            }
            
            if (info.DisplayText != null)
            {
                // Fallback to system default language if there's no language provided.
                string? translatedResult = _translationManager.Translate(info.Section!, language?.Trim() ?? _configurationManager.Configuration.UICulture, info.DisplayText, section.TranslationMetadata);

                info.DisplayText = translatedResult;
            }
            
            return info;
        }

        /// <summary>
        /// Resolves a section title link target from AdditionalData (item id, collection/playlist/genre name).
        /// Failures return null so the client can render a plain title instead of a broken link.
        /// </summary>
        private BaseItemDto? TryResolveTitleLinkTarget(string additionalData, Guid userId)
        {
            try
            {
                User? user = _userManager.GetUserById(userId);
                if (user == null)
                {
                    return null;
                }

                DtoOptions dtoOptions = CreateTitleLinkDtoOptions();
                return ResolveTitleLinkById(additionalData, user, dtoOptions)
                    ?? ResolveTitleLinkByName(additionalData, userId, user, dtoOptions);
            }
            catch (Exception ex) when (
                ex is InvalidOperationException
                or ArgumentException
                or NullReferenceException
                or KeyNotFoundException
                or NotSupportedException
                or FormatException
                or TimeoutException
                or IOException
                or HttpRequestException
                or System.Reflection.TargetInvocationException)
            {
                PluginLog.SectionTitleLinkResolveFailed(_logger, ex, additionalData, userId);
            }

            return null;
        }

        private static DtoOptions CreateTitleLinkDtoOptions()
        {
            return new DtoOptions
            {
                Fields = [ItemFields.PrimaryImageAspectRatio,
                    ItemFields.DisplayPreferencesId]
            };
        }

        private BaseItemDto? ResolveTitleLinkById(string additionalData, User user, DtoOptions dtoOptions)
        {
            if (!Guid.TryParse(additionalData, out Guid itemId))
            {
                return null;
            }

            BaseItem? byId = _libraryManager.GetItemById(itemId);
            return byId != null ? _dtoService.GetBaseItemDto(byId, dtoOptions, user) : null;
        }

        private BaseItemDto? ResolveTitleLinkByName(string additionalData, Guid userId, User user, DtoOptions dtoOptions)
        {
            BoxSet? collection = _collectionManagerProxy.GetCollections(user)
                .FirstOrDefault(x => string.Equals(x.Name, additionalData, StringComparison.OrdinalIgnoreCase));
            if (collection != null)
            {
                return _dtoService.GetBaseItemDto(collection, dtoOptions, user);
            }

            Playlist? playlist = _playlistManager.GetPlaylists(userId)
                .FirstOrDefault(x => string.Equals(x.Name, additionalData, StringComparison.OrdinalIgnoreCase));
            if (playlist != null)
            {
                return _dtoService.GetBaseItemDto(playlist, dtoOptions, user);
            }

            Genre? genre = _libraryManager.GetGenre(additionalData);
            return genre != null ? _dtoService.GetBaseItemDto(genre, dtoOptions, user) : null;
        }
    }

    public class UserHomeSections
    {
        public Guid PageHash { get; set; }
        public IList<HomeScreenSectionInfo> Sections { get; set; } = [];
    }
}
