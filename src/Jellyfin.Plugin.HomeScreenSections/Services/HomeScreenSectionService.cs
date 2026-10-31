using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Diagnostics;
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
        private static readonly ConditionalWeakTable<UserSectionsData, ProgressivePageState> s_pageStates = new ConditionalWeakTable<UserSectionsData, ProgressivePageState>();
        private static readonly ConcurrentDictionary<Guid, byte> s_generationsStarted = new ConcurrentDictionary<Guid, byte>();
        private static readonly TimeSpan s_shortPageTimeout = TimeSpan.FromMilliseconds(250);

        private sealed class ProgressivePageState
        {
            public readonly object Sync = new();
            public readonly SortedDictionary<int, List<IHomeScreenSection>> Groups = new();
            public readonly Dictionary<int, List<(IHomeScreenSection Section, int Order)>> Pages = new();
            public readonly Dictionary<int, int> PageEndOffsets = new();
        }

    
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
            if (page < 1 || pageSize < 1 || !m_dataCache.Cache.TryGetValue(pageHash, out UserSectionsData? cache))
            {
                return null;
            }

            cache.LastAccessed = DateTime.UtcNow;
            if (m_dataCache.PageHashExpiry.TryGetValue(pageHash, out DateTime expiry))
            {
                m_dataCache.PageHashExpiry.TryUpdate(pageHash, DateTime.UtcNow.AddHours(1), expiry);
            }

            ProgressivePageState state = s_pageStates.GetValue(cache, _ => new ProgressivePageState());
            lock (state.Sync)
            {
                if (state.Pages.TryGetValue(page, out List<(IHomeScreenSection Section, int Order)>? existingPage))
                {
                    return existingPage.Select(x => SectionToInfo(x.Section, x.Order, language)).ToList();
                }

                // A later page cannot be allocated until the previous boundary is fixed.
                if (page > 1 && !state.PageEndOffsets.TryGetValue(page - 1, out _))
                {
                    return null;
                }

                int skip = page == 1 ? 0 : state.PageEndOffsets[page - 1];
                List<(IHomeScreenSection Section, int Order)> available = new List<(IHomeScreenSection Section, int Order)>();
                // Never pass an unfinished earlier group; completed groups and their
                // stable prefixes may be consumed, in configured group order.
                foreach (KeyValuePair<int, List<IHomeScreenSection>> group in state.Groups)
                {
                    available.AddRange(group.Value.Select(x => (x, group.Key)));
                    if (cache.SectionsInProgress.ContainsKey(group.Key))
                    {
                        break;
                    }
                }

                List<(IHomeScreenSection Section, int Order)> slice = available.Skip(skip).Take(pageSize).ToList();
                bool complete = cache.SectionsInProgress.IsEmpty;
                if (slice.Count < pageSize && !complete)
                {
                    return null;
                }

                return CommitPage(state, page, skip, slice, language);
            }
        }

        private List<HomeScreenSectionInfo> CommitPage(ProgressivePageState state, int page, int skip,
            List<(IHomeScreenSection Section, int Order)> sections, string? language)
        {
            state.Pages[page] = sections;
            state.PageEndOffsets[page] = skip + sections.Count;
            return sections.Select(x => SectionToInfo(x.Section, x.Order, language)).ToList();
        }

        private List<HomeScreenSectionInfo>? TryCommitShortPage(Guid pageHash, string? language,
            int page, int pageSize)
        {
            if (!m_dataCache.Cache.TryGetValue(pageHash, out UserSectionsData? cache))
            {
                return null;
            }

            ProgressivePageState state = s_pageStates.GetValue(cache, _ => new ProgressivePageState());
            lock (state.Sync)
            {
                if (state.Pages.TryGetValue(page, out List<(IHomeScreenSection Section, int Order)>? existing))
                {
                    return existing.Select(x => SectionToInfo(x.Section, x.Order, language)).ToList();
                }
                if (page > 1 && !state.PageEndOffsets.ContainsKey(page - 1))
                {
                    return null;
                }

                int skip = page == 1 ? 0 : state.PageEndOffsets[page - 1];
                List<(IHomeScreenSection Section, int Order)> available = new List<(IHomeScreenSection Section, int Order)>();
                foreach (KeyValuePair<int, List<IHomeScreenSection>> group in state.Groups)
                {
                    available.AddRange(group.Value.Select(x => (x, group.Key)));
                    if (cache.SectionsInProgress.ContainsKey(group.Key))
                    {
                        break;
                    }
                }
                List<(IHomeScreenSection Section, int Order)> slice = available.Skip(skip).Take(pageSize).ToList();
                return slice.Count > 0 ? CommitPage(state, page, skip, slice, language) : null;
            }
        }

        private Guid GeneratePageHash(Guid userId, Guid? forceHash = null)
        {
            Guid pageHash = forceHash ?? Guid.NewGuid();
            m_dataCache.PageHashOwnerIds.TryAdd(pageHash, userId);
            m_dataCache.PageHashExpiry.TryAdd(pageHash, DateTime.UtcNow.AddHours(1)); // TODO: In a future update we should make this configurable.
            
            return pageHash;
        }

        public List<HomeScreenSectionInfo>? MonitorLiveUpdatedSectionsForUser(Guid userId, string? language, int page, int? pageSize = null, Guid? pageHash = null)
        {
            _ = Task.Run(() => ClearExpiredUserCaches(userId));

            if (pageHash.HasValue && !DoesPageBelongToUser(pageHash.Value, userId))
            {
                pageHash = GeneratePageHash(userId);
            }
            pageHash ??= GetActiveTempPageCacheForUser(userId) ?? GeneratePageHash(userId);
            Guid hash = pageHash.Value;

            // Starting the same generation twice could overwrite a completed cache.
            if (!m_dataCache.Cache.ContainsKey(hash) && s_generationsStarted.TryAdd(hash, 0))
            {
                _ = Task.Run(() =>
                {
                    try
                    {
                        CacheSectionsForUser(userId, hash);
                    }
                    catch (Exception ex)
                    {
                        m_logger.LogError(ex, "Failed to generate home sections for user {UserId}", userId);
                    }
                    finally
                    {
                        s_generationsStarted.TryRemove(hash, out _);
                    }
                });
            }

            Stopwatch timer = Stopwatch.StartNew();
            int requestedSize = pageSize.GetValueOrDefault(int.MaxValue);
            while (true)
            {
                List<HomeScreenSectionInfo>? sections = GetCachedSectionsForUser(userId, language, page, requestedSize, hash);
                if (sections != null)
                {
                    return sections;
                }
                if (timer.Elapsed >= s_shortPageTimeout)
                {
                    sections = TryCommitShortPage(hash, language, page, requestedSize);
                    if (sections != null)
                    {
                        return sections;
                    }
                }

                // No usable sections yet: do not return an empty page that the
                // client might mistake for end-of-pagination.
                Thread.Sleep(20);
            }
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
            
            if (userSectionsData == null)
            {
                return;
            }

            ProgressivePageState state = s_pageStates.GetValue(userSectionsData, _ => new ProgressivePageState());
            lock (state.Sync)
            {
                foreach (IGrouping<int, SectionSettings> group in groupedOrderedSections)
                {
                    state.Groups.TryAdd(group.Key, new List<IHomeScreenSection>());
                }
            }

            Parallel.ForEach(groupedOrderedSections, orderedSections =>
            {
                Parallel.ForEach(orderedSections, sectionSettings =>
                {
                    IHomeScreenSection? sectionType = sectionTypes.FirstOrDefault(x => x.Section == sectionSettings.SectionId);
                    if (sectionType == null)
                    {
                        return;
                    }

                    try
                    {
                        int count = sectionType.Limit > 1
                            ? Random.Shared.Next(sectionSettings.LowerLimit, sectionSettings.UpperLimit)
                            : 1;
                        List<IHomeScreenSection> batch = sectionType.CreateInstances(userId, count).ToList();
                        batch.Shuffle();
                        lock (state.Sync)
                        {
                            // Append only: sections already served never move.
                            state.Groups[orderedSections.Key].AddRange(batch);
                        }
                    }
                    catch (Exception ex)
                    {
                        m_logger.LogError(ex, "An error occurred while creating section instances for user '{UserId}' and section '{Section}'.", userId, sectionType.Section);
                    }
                });

                lock (state.Sync)
                {
                    userSectionsData.OrderedSections.TryAdd(orderedSections.Key, state.Groups[orderedSections.Key].ToArray());
                    userSectionsData.SectionsInProgress.TryRemove(orderedSections.Key, out _);
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
                // Fallback to system default language if there's no language provided.
                string? translatedResult = m_translationManager.Translate(info.Section!, language?.Trim() ?? m_configurationManager.Configuration.UICulture, info.DisplayText, section.TranslationMetadata);

                info.DisplayText = translatedResult;
            }
            
            return info;
        }

        private async Task ClearExpiredUserCaches(Guid userId)
        {
            await Task.Yield();
            
            Guid[] userPageHashes = m_dataCache.PageHashOwnerIds
                .Where(x => x.Value == userId)
                .Select(x => x.Key)
                .ToArray();
            Guid[] expiredPageHashes = m_dataCache.PageHashExpiry
                .Where(x => userPageHashes.Any(y => y == x.Key))
                .Where(x => x.Value < DateTime.UtcNow)
                .Select(x => x.Key)
                .ToArray();

            foreach (Guid pageHash in expiredPageHashes)
            {
                m_dataCache.Cache.TryRemove(pageHash, out _);
            }
        }

        private Guid? GetActiveTempPageCacheForUser(Guid userId)
        {
            Guid[] userPageHashes = m_dataCache.PageHashOwnerIds
                .Where(x => x.Value == userId)
                .Select(x => x.Key)
                .ToArray();
            Guid[] activePageHashes = m_dataCache.PageHashExpiry
                .Where(x => userPageHashes.Any(y => y == x.Key))
                .Where(x => x.Value > DateTime.UtcNow)
                .OrderByDescending(x => x.Value)
                .Select(x => x.Key)
                .ToArray();

            if (activePageHashes.Length == 0)
            {
                return null;
            }
            
            return activePageHashes.First();
        }
        
        private bool DoesPageBelongToUser(Guid pageHash, Guid userId)
        {
            bool doesExist = m_dataCache.PageHashOwnerIds.TryGetValue(pageHash, out Guid ownerId);

            if (!doesExist)
            {
                GeneratePageHash(userId, pageHash); // There is no reason if the hash doesn't exist for us to not just add it.
                return true;
            }
            
            return doesExist && ownerId == userId;
        }
    }

    public class UserHomeSections
    {
        public Guid PageHash { get; set; }
        public List<HomeScreenSectionInfo> Sections { get; set; } = new List<HomeScreenSectionInfo>();
    }
}
