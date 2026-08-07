using System.Collections.Concurrent;
using System.Threading.Channels;
using Jellyfin.Extensions;
using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Data;
using Jellyfin.Plugin.HomeScreenSections.Helpers;
using Jellyfin.Plugin.HomeScreenSections.Library;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
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
    
        public HomeScreenSectionService(IHomeScreenManager homeScreenManager,
            ILogger<HomeScreenSectionsPlugin> logger, ITranslationManager translationManager,
            UserSectionsDataCache dataCache, IServerConfigurationManager _configurationManager)
        {
            m_homeScreenManager = homeScreenManager;
            m_logger = logger;
            m_translationManager = translationManager;
            m_dataCache = dataCache;
            m_configurationManager = _configurationManager;
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
                    .Select(x => SectionToInfo(x.Section, x.ConfiguredOrder, language))
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
            WaitUntilCachePresent(pageHash.Value);
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

            IGrouping<int, SectionSettings>[] groupedOrderedSections = HomeScreenSectionsPlugin.Instance.Configuration.SectionSettings
                .OrderBy(x => x.OrderIndex)
                .GroupBy(x => x.OrderIndex)
                .ToArray();

            UserSectionsData? userSectionsData = pageHash != null
                ? InitializeUserSectionsData(userId, pageHash.Value, groupedOrderedSections)
                : null;
            
            Parallel.ForEach(groupedOrderedSections, orderedSections =>
            {
                PopulateOrderGroup(userId, sectionTypes, orderedSections, userSectionsData);
            });
        }

        private static List<(IHomeScreenSection Section, int ConfiguredOrder)> CollectCohesiveSections(
            UserSectionsData userSectionsData,
            int[] orderedKeys,
            out bool isComplete)
        {
            List<(IHomeScreenSection Section, int ConfiguredOrder)> sectionsToReturn = new List<(IHomeScreenSection, int)>();
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
                Thread cacheThread = new Thread(() => CacheSectionsForUser(userId, pageHash));
                cacheThread.Start();
            }
        }

        private void WaitUntilCachePresent(Guid pageHash)
        {
            SpinWait spinWait = new SpinWait();
            while (!m_dataCache.Cache.ContainsKey(pageHash))
            {
                spinWait.SpinOnce();
            }
        }

        private void WaitUntilCacheHasStartedWork(Guid pageHash)
        {
            SpinWait spinWait = new SpinWait();
            // If there's no data at all then we wait until its started.
            while (m_dataCache.Cache[pageHash].SectionsInProgress.IsEmpty && m_dataCache.Cache[pageHash].OrderedSections.IsEmpty)
            {
                spinWait.SpinOnce();
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

            SpinWait spinWait = new SpinWait();
            for (int i = lowestSectionIndex; i <= cache.MaxOrderIndex; i++)
            {
                if (cache.OrderIndicesWithoutSections.Any(x => x.Contains(i)))
                {
                    continue;
                }
                
                while (cache.SectionsInProgress.ContainsKey(i))
                {
                    spinWait.SpinOnce();
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
            
            m_dataCache.Cache.TryAdd(pageHash, userSectionsData);

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

        private HomeScreenSectionInfo SectionToInfo(IHomeScreenSection section, int configuredOrder, string? language)
        {
            HomeScreenSectionInfo info = section.AsInfo();

            info.OrderIndex = configuredOrder;
            info.ViewMode = HomeScreenSectionsPlugin.Instance.Configuration.SectionSettings.FirstOrDefault(y => string.Equals(y.SectionId, info.Section, StringComparison.Ordinal))?.ViewMode ?? info.ViewMode ?? SectionViewMode.Landscape;
            
            if (info.DisplayText != null)
            {
                // Fallback to system default language if there's no language provided.
                string? translatedResult = m_translationManager.Translate(info.Section!, language?.Trim() ?? m_configurationManager.Configuration.UICulture, info.DisplayText, section.TranslationMetadata);

                info.DisplayText = translatedResult;
            }
            
            return info;
        }
    }

    public class UserHomeSections
    {
        public Guid PageHash { get; set; }
        public IList<HomeScreenSectionInfo> Sections { get; set; } = new List<HomeScreenSectionInfo>();
    }
}
