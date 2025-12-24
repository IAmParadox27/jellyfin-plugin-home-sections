using System.Collections.Concurrent;
using System.Threading.Channels;
using Jellyfin.Extensions;
using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Data;
using Jellyfin.Plugin.HomeScreenSections.Helpers;
using Jellyfin.Plugin.HomeScreenSections.Library;
using MediaBrowser.Controller;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.HomeScreenSections.Services
{
    public class HomeScreenSectionService
    {
        private readonly IDisplayPreferencesManager m_displayPreferencesManager;
        private readonly IHomeScreenManager m_homeScreenManager;
        private readonly ILogger<HomeScreenSectionsPlugin> m_logger;
        private readonly ITranslationManager m_translationManager;
        private readonly UserSectionsDataCache m_dataCache;
    
        public HomeScreenSectionService(IDisplayPreferencesManager displayPreferencesManager,
            IHomeScreenManager homeScreenManager, ILogger<HomeScreenSectionsPlugin> logger, 
            ITranslationManager translationManager, UserSectionsDataCache dataCache)
        {
            m_displayPreferencesManager = displayPreferencesManager;
            m_homeScreenManager = homeScreenManager;
            m_logger = logger;
            m_translationManager = translationManager;
            m_dataCache = dataCache;
        }

        public List<HomeScreenSectionInfo>? GetCachedSectionsForUser(Guid userId, string? language, int page, int pageSize, Guid pageHash)
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

            List<(IHomeScreenSection Section, int ConfiguredOrder)> sectionsToReturn = new List<(IHomeScreenSection, int)>();
            bool isComplete = true;
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
            
            sectionsToReturn = sectionsToReturn.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            if (isComplete || sectionsToReturn.Count == pageSize)
            {
                return sectionsToReturn
                    .Select(x => SectionToInfo(x.Section, x.ConfiguredOrder, language))
                    .ToList();
            }

            // Return nothing if we don't have the complete picture.
            return null;
        }

        public List<HomeScreenSectionInfo>? MonitorLiveUpdatedSectionsForUser(Guid userId, string? language, int page, int? pageSize = null, Guid? pageHash = null)
        {
            if (pageHash == null)
            {
                pageHash = Guid.NewGuid();
                
                CacheSectionsForUser(userId, pageHash.Value);

                int totalSectionCount = m_dataCache.Cache[pageHash.Value].OrderedSections.SelectMany(x => x.Value).Count();
                return GetCachedSectionsForUser(userId, language, 1, totalSectionCount, pageHash.Value);
            }

            if (!m_dataCache.Cache.ContainsKey(pageHash.Value))
            {
                Thread cacheThread = new Thread(() => CacheSectionsForUser(userId, pageHash.Value));
                cacheThread.Start();
            }

            SpinWait spinWait = new SpinWait();
            while (!m_dataCache.Cache.ContainsKey(pageHash.Value))
            {
                spinWait.SpinOnce();
            }
            spinWait.Reset();

            // If there's no data at all then we wait until its started.
            while (!m_dataCache.Cache[pageHash.Value].SectionsInProgress.Any() && !m_dataCache.Cache[pageHash.Value].OrderedSections.Any())
            {
                spinWait.SpinOnce();
            }
            
            // We always wait from the start, if we hit a page that's already cached then we'll just return immediately.
            // If its still in progress then we'll wait for it to finish.
            UserSectionsData cache = m_dataCache.Cache[pageHash.Value];
            int lowestSectionIndex = Math.Min(
                m_dataCache.Cache[pageHash.Value].OrderedSections.Any() 
                    ? m_dataCache.Cache[pageHash.Value].OrderedSections.Min(x => x.Key) 
                    : int.MaxValue,
                m_dataCache.Cache[pageHash.Value].SectionsInProgress.Any() 
                    ? m_dataCache.Cache[pageHash.Value].SectionsInProgress.Min(x => x.Key) 
                    : int.MaxValue);

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
                
                List<HomeScreenSectionInfo>? sections = GetCachedSectionsForUser(userId, language, page, pageSize ?? cache.OrderedSections.SelectMany(x => x.Value).Count(), pageHash.Value);
                if (sections != null)
                {
                    return sections;
                }
            }
            
            return null;
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

            UserSectionsData? userSectionsData = null;
            if (pageHash != null)
            {
                userSectionsData = new UserSectionsData()
                {
                    UserId = userId,
                    MaxOrderIndex = groupedOrderedSections.Max(x => x.Key)
                };
                
                m_dataCache.Cache.TryAdd(pageHash.Value, userSectionsData);

                foreach (int orderIndex in groupedOrderedSections.Select(x => x.Key).OrderBy(x => x))
                {
                    userSectionsData.SectionsInProgress.TryAdd(orderIndex, true);
                }

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
            
            Parallel.ForEach(groupedOrderedSections, orderedSections =>
            {
                ConcurrentBag<IHomeScreenSection?> tmpPluginSections = new ConcurrentBag<IHomeScreenSection?>(); // we want these randomly distributed among each other.

                Parallel.ForEach(orderedSections, sectionSettings =>
                {
                    IHomeScreenSection? sectionType =
                        sectionTypes.FirstOrDefault(x => x.Section == sectionSettings.SectionId);

                    if (sectionType != null)
                    {
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
                        catch (Exception e)
                        {
                            // Adding an error log here to stop issues like #128 from completely breaking the home screen.
                            // Whatever this section is won't work, but the rest of the home screen will still work.
                            m_logger.LogError(e, $"An error occurred while creating section instances for user '{userId}' and section '{sectionType.Section}'.");
                        }
                    }
                });

                List<IHomeScreenSection> sectionList = tmpPluginSections.Where(x => x != null).Select(x => x!).ToList();
                sectionList.Shuffle();

                if (userSectionsData != null)
                {
                    userSectionsData.OrderedSections.TryAdd(orderedSections.Key, sectionList);
                    userSectionsData.SectionsInProgress.Remove(orderedSections.Key, out _);
                }
            });
        }

        private HomeScreenSectionInfo SectionToInfo(IHomeScreenSection section, int configuredOrder, string? language)
        {
            HomeScreenSectionInfo info = section.AsInfo();

            info.OrderIndex = configuredOrder;
            info.ViewMode = HomeScreenSectionsPlugin.Instance.Configuration.SectionSettings.FirstOrDefault(y => y.SectionId == info.Section)?.ViewMode ?? info.ViewMode ?? SectionViewMode.Landscape;
            
            if (info.DisplayText != null)
            {
                // Always fallback to "en" if there's no language provided.
                string? translatedResult = m_translationManager.Translate(info.Section!, language?.Trim() ?? "en", info.DisplayText, section.TranslationMetadata);

                info.DisplayText = translatedResult;
            }
            
            return info;
        }
    }

    public class UserHomeSections
    {
        public Guid PageHash { get; set; }
        public List<HomeScreenSectionInfo> Sections { get; set; } = new List<HomeScreenSectionInfo>();
    }
}
