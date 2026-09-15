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

        public List<HomeScreenSectionInfo>? GetCachedSectionsForUser(Guid userId, string? language, int page, int pageSize, Guid pageHash)
        {
            if (!m_dataCache.Cache.TryGetValue(pageHash, out UserSectionsData? userSectionsData))
            {
                return null;
            }
            
            // Make sure that it's flagged as being used, even if we don't return anything here the page is still active
            // as we've received a request for it.
            userSectionsData.LastAccessed = DateTime.UtcNow;
            m_dataCache.Touch(pageHash);
            
            (IHomeScreenSection Section, int ConfiguredOrder)[]? completedSections = userSectionsData.CompletedSections;
            if (completedSections != null)
            {
                long offset = Math.Max(0, ((long)page - 1) * pageSize);
                int count = (int)Math.Max(0, Math.Min(pageSize, completedSections.Length - offset));
                List<HomeScreenSectionInfo> results = new List<HomeScreenSectionInfo>(count);
                for (int i = 0; i < count; i++)
                {
                    (IHomeScreenSection Section, int ConfiguredOrder) row = completedSections[(int)offset + i];
                    results.Add(SectionToInfo(row.Section, row.ConfiguredOrder, language));
                }
                return results;
            }

            // Capture pending work before the rows, so a finishing group cannot make an earlier row snapshot look complete.
            int? firstInProgress = null;
            foreach (KeyValuePair<int, bool> sectionInProgress in userSectionsData.SectionsInProgress)
            {
                if (!firstInProgress.HasValue || sectionInProgress.Key < firstInProgress.Value)
                {
                    firstInProgress = sectionInProgress.Key;
                }
            }

            int[] orderedKeys = userSectionsData.OrderedSections.Keys.OrderBy(x => x).ToArray();
            List<(IHomeScreenSection Section, int ConfiguredOrder)> sectionsToReturn = new List<(IHomeScreenSection, int)>();
            bool isComplete = true;
            for (int i = 0; i < orderedKeys.Length; i++)
            {
                int key = orderedKeys[i];
                if (firstInProgress.HasValue && firstInProgress.Value <= key)
                {
                    isComplete = false;
                    break;
                }

                long prevKey = i > 0 ? orderedKeys[i - 1] : (long)orderedKeys[i] - 1;

                bool cohesive = (key - prevKey) == 1;
                if (key - prevKey > 1)
                {
                    // If any of the ranges contain both the "key before" and "key after" then we can safely know this is cohesive.
                    if (userSectionsData.OrderIndicesWithoutSections.Any(x => x.Contains(key - 1) && x.Contains((int)(prevKey + 1))))
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
            
            long requestedOffset = Math.Max(0, ((long)page - 1) * pageSize);
            sectionsToReturn = sectionsToReturn.Skip((int)Math.Min(int.MaxValue, requestedOffset)).Take(pageSize).ToList();
            if ((isComplete && !firstInProgress.HasValue) || sectionsToReturn.Count == pageSize)
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
            Guid activePageHash = m_dataCache.BeginUse(userId, pageHash);
            try
            {
                return MonitorCachedSectionsForUser(userId, language, page, pageSize, activePageHash);
            }
            finally
            {
                m_dataCache.EndUse(activePageHash);
            }
        }

        private List<HomeScreenSectionInfo>? MonitorCachedSectionsForUser(Guid userId, string? language, int page, int? pageSize, Guid pageHash)
        {
            if (!m_dataCache.Cache.ContainsKey(pageHash))
            {
                Thread cacheThread = new Thread(() => CacheSectionsForUser(userId, pageHash));
                cacheThread.Start();
            }

            SpinWait spinWait = new SpinWait();
            while (!m_dataCache.Cache.ContainsKey(pageHash))
            {
                spinWait.SpinOnce();
            }
            spinWait.Reset();

            // If there's no data at all then we wait until its started.
            while (!m_dataCache.Cache[pageHash].SectionsInProgress.Any() && !m_dataCache.Cache[pageHash].OrderedSections.Any() &&
                m_dataCache.Cache[pageHash].CompletedSections == null)
            {
                spinWait.SpinOnce();
            }
            
            if (!pageSize.HasValue)
            {
                while (m_dataCache.Cache[pageHash].SectionsInProgress.Any())
                {
                    spinWait.SpinOnce();
                }
            }

            // We always wait from the start, if we hit a page that's already cached then we'll just return immediately.
            // If its still in progress then we'll wait for it to finish.
            UserSectionsData cache = m_dataCache.Cache[pageHash];
            (IHomeScreenSection Section, int ConfiguredOrder)[]? completedSections = cache.CompletedSections;
            if (completedSections != null)
            {
                return GetCachedSectionsForUser(userId, language, page, pageSize ?? completedSections.Length, pageHash);
            }

            int[] sectionIndices = cache.ConfiguredOrderIndices ?? cache.SectionsInProgress.Keys.Concat(cache.OrderedSections.Keys)
                .Distinct().OrderBy(x => x).ToArray();
            foreach (int i in sectionIndices)
            {
                while (cache.SectionsInProgress.ContainsKey(i))
                {
                    spinWait.SpinOnce();
                }
                
                List<HomeScreenSectionInfo>? sections = GetCachedSectionsForUser(userId, language, page, pageSize ?? cache.OrderedSections.SelectMany(x => x.Value).Count(), pageHash);
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
                    MaxOrderIndex = groupedOrderedSections.Select(g => g.Key).DefaultIfEmpty(0).Max()
                };
                
                foreach (int orderIndex in groupedOrderedSections.Select(x => x.Key).OrderBy(x => x))
                {
                    userSectionsData.SectionsInProgress.TryAdd(orderIndex, true);
                }

                int[] sectionIndices = userSectionsData.SectionsInProgress.Keys.OrderBy(x => x).ToArray();
                userSectionsData.ConfiguredOrderIndices = sectionIndices;
                for (int i = 1; i < sectionIndices.Length; i++)
                {
                    int prevIndex = sectionIndices[i - 1];
                    int currentIndex = sectionIndices[i];

                    if ((long)currentIndex - prevIndex > 1)
                    {
                        userSectionsData.OrderIndicesWithoutSections.Add(new IntRange()
                        {
                            Start = prevIndex + 1, 
                            End = currentIndex - 1
                        });
                    }
                }

                if (!m_dataCache.Cache.TryAdd(pageHash.Value, userSectionsData))
                {
                    return;
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

            if (userSectionsData != null)
            {
                userSectionsData.CompletedSections = userSectionsData.OrderedSections.OrderBy(x => x.Key)
                    .SelectMany(x => x.Value.Select(section => (section, x.Key)))
                    .ToArray();
            }
        }

        private HomeScreenSectionInfo SectionToInfo(IHomeScreenSection section, int configuredOrder, string? language)
        {
            HomeScreenSectionInfo info = section.AsInfo();

            info.OrderIndex = configuredOrder;
            SectionViewMode? configuredViewMode = null;
            foreach (SectionSettings settings in HomeScreenSectionsPlugin.Instance.Configuration.SectionSettings)
            {
                if (settings.SectionId == info.Section)
                {
                    configuredViewMode = settings.ViewMode;
                    break;
                }
            }
            info.ViewMode = configuredViewMode ?? info.ViewMode ?? SectionViewMode.Landscape;
            
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
        public List<HomeScreenSectionInfo> Sections { get; set; } = new List<HomeScreenSectionInfo>();
    }
}
