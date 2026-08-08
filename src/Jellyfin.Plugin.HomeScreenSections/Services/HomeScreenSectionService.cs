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
        private readonly IServerConfigurationManager m_configurationManager;
        private readonly IHomeScreenManager m_homeScreenManager;
        private readonly ILogger<HomeScreenSectionsPlugin> m_logger;
        private readonly ITranslationManager m_translationManager;
        private readonly UserSectionsDataCache m_dataCache;
        private readonly IUserManager m_userManager;
        private readonly ILibraryManager m_libraryManager;
        private readonly IDtoService m_dtoService;
        private readonly CollectionManagerProxy m_collectionManagerProxy;
        private readonly IPlaylistManager m_playlistManager;

        // Tracks background page-build tasks so waiters can fail fast on faults
        // instead of busy-waiting for a cache entry that will never appear.
        private readonly ConcurrentDictionary<Guid, Task> m_buildTasks = new();
    
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
            m_homeScreenManager = homeScreenManager;
            m_logger = logger;
            m_translationManager = translationManager;
            m_dataCache = dataCache;
            m_configurationManager = configurationManager;
            m_userManager = userManager;
            m_libraryManager = libraryManager;
            m_dtoService = dtoService;
            m_collectionManagerProxy = collectionManagerProxy;
            m_playlistManager = playlistManager;
        }

        public IReadOnlyList<HomeScreenSectionInfo>? GetCachedSectionsForUser(Guid userId, string? language, int page, int pageSize, Guid pageHash)
        {
            if (!m_dataCache.Cache.TryGetValue(pageHash, out UserSectionsData? userSectionsData))
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

                int totalSectionCount = m_dataCache.Cache[pageHash.Value].OrderedSections.SelectMany(x => x.Value).Count();
                return GetCachedSectionsForUser(userId, language, 1, totalSectionCount, pageHash.Value);
            }

            EnsureCacheStarted(userId, pageHash.Value);
            if (!WaitUntilCachePresent(pageHash.Value))
            {
                // The background build faulted; return an empty page instead of hanging.
                return [];
            }

            WaitUntilCacheHasStartedWork(pageHash.Value);

            return WaitForPageSections(userId, language, page, pageSize, pageHash.Value);
        }
    
        public void CacheSectionsForUser(Guid userId, Guid? pageHash = null)
        {
            if (m_dataCache.Cache.ContainsKey(pageHash ?? Guid.Empty))
            {
                return;
            }
            
            ModularHomeUserSettings? settings = m_homeScreenManager.GetUserSettings(userId);

            List<IHomeScreenSection> sectionTypes = m_homeScreenManager.GetSectionTypes().Where(x => settings?.EnabledSections.Contains(x.Section ?? string.Empty) ?? false).ToList();

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
            if (!m_dataCache.Cache.ContainsKey(pageHash))
            {
                m_buildTasks.GetOrAdd(pageHash, _ => Task.Run(() => CacheSectionsForUser(userId, pageHash)));
            }
        }

        private bool WaitUntilCachePresent(Guid pageHash)
        {
            while (!m_dataCache.Cache.ContainsKey(pageHash))
            {
                // The build task adds the cache entry before doing any heavy work, so a
                // completed task without an entry means it faulted; fail fast instead of
                // spinning forever (upstream #247 turned this into an infinite hang).
                if (m_buildTasks.TryGetValue(pageHash, out Task? buildTask) && buildTask.IsCompleted)
                {
                    if (buildTask.Exception != null)
                    {
                        PluginLog.SectionCacheBuildFailed(m_logger, buildTask.Exception.GetBaseException(), pageHash);
                    }

                    return m_dataCache.Cache.ContainsKey(pageHash);
                }

                Thread.Sleep(10);
            }

            m_buildTasks.TryRemove(pageHash, out _);
            return true;
        }

        private void WaitUntilCacheHasStartedWork(Guid pageHash)
        {
            // If there's no data at all then we wait until its started.
            while (m_dataCache.Cache[pageHash].SectionsInProgress.IsEmpty && m_dataCache.Cache[pageHash].OrderedSections.IsEmpty)
            {
                Thread.Sleep(10);
            }
        }

        private IReadOnlyList<HomeScreenSectionInfo>? WaitForPageSections(Guid userId, string? language, int page, int? pageSize, Guid pageHash)
        {
            // We always wait from the start, if we hit a page that's already cached then we'll just return immediately.
            // If its still in progress then we'll wait for it to finish.
            UserSectionsData cache = m_dataCache.Cache[pageHash];
            int lowestSectionIndex = Math.Min(
                !m_dataCache.Cache[pageHash].OrderedSections.IsEmpty
                    ? m_dataCache.Cache[pageHash].OrderedSections.Min(x => x.Key) 
                    : int.MaxValue,
                !m_dataCache.Cache[pageHash].SectionsInProgress.IsEmpty
                    ? m_dataCache.Cache[pageHash].SectionsInProgress.Min(x => x.Key) 
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
                // DefaultIfEmpty guards the fresh-install case where no admin SectionSettings
                // exist yet; Enumerable.Max on an empty sequence used to throw and 500 the
                // whole endpoint (upstream #247).
                MaxOrderIndex = groupedOrderedSections.Select(x => x.Key).DefaultIfEmpty(0).Max()
            };

            m_dataCache.Cache.TryAdd(pageHash, userSectionsData);

            if (groupedOrderedSections.Length == 0)
            {
                // Seed a sentinel order group so the wait helpers see a complete (empty)
                // page instead of spinning until SectionsInProgress/OrderedSections fill up.
                userSectionsData.OrderedSections.TryAdd(0, []);
            }

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
                PluginLog.SectionInstanceError(m_logger, e, userId, sectionType.Section);
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
                string? translatedResult = m_translationManager.Translate(info.Section!, language?.Trim() ?? m_configurationManager.Configuration.UICulture, info.DisplayText, section.TranslationMetadata);

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
                User? user = m_userManager.GetUserById(userId);
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
                PluginLog.SectionTitleLinkResolveFailed(m_logger, ex, additionalData, userId);
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

            BaseItem? byId = m_libraryManager.GetItemById(itemId);
            return byId != null ? m_dtoService.GetBaseItemDto(byId, dtoOptions, user) : null;
        }

        private BaseItemDto? ResolveTitleLinkByName(string additionalData, Guid userId, User user, DtoOptions dtoOptions)
        {
            BoxSet? collection = m_collectionManagerProxy.GetCollections(user)
                .FirstOrDefault(x => string.Equals(x.Name, additionalData, StringComparison.OrdinalIgnoreCase));
            if (collection != null)
            {
                return m_dtoService.GetBaseItemDto(collection, dtoOptions, user);
            }

            Playlist? playlist = m_playlistManager.GetPlaylists(userId)
                .FirstOrDefault(x => string.Equals(x.Name, additionalData, StringComparison.OrdinalIgnoreCase));
            if (playlist != null)
            {
                return m_dtoService.GetBaseItemDto(playlist, dtoOptions, user);
            }

            Genre? genre = m_libraryManager.GetGenre(additionalData);
            return genre != null ? m_dtoService.GetBaseItemDto(genre, dtoOptions, user) : null;
        }
    }

    public class UserHomeSections
    {
        public Guid PageHash { get; set; }
        public IList<HomeScreenSectionInfo> Sections { get; set; } = [];
    }
}
