using System.Collections.Concurrent;
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
        private readonly object m_cacheCreationLock = new object();
        private static readonly TimeSpan c_generationTimeout = TimeSpan.FromSeconds(60);
    
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
            
            if (!userSectionsData.Initialized.Task.IsCompleted)
            {
                return null;
            }

            // Make sure that it's flagged as being used, even if we don't return anything here the page is still active
            // as we've received a request for it.
            userSectionsData.LastAccessed = DateTime.UtcNow;
            m_dataCache.PageHashExpiry.TryUpdate(pageHash, DateTime.UtcNow.AddHours(1), m_dataCache.PageHashExpiry[pageHash]); // TODO: In a future update we should make this configurable.
            
            // Check if the userSectionsData has the data we're after
            int[] orderedKeys = userSectionsData.OrderedSections.Keys.OrderBy(x => x).ToArray();

            List<(IHomeScreenSection Section, int ConfiguredOrder)> sectionsToReturn = new List<(IHomeScreenSection, int)>();
            bool isComplete = true;
            for (int i = 0; i < orderedKeys.Length; i++)
            {
                int key = orderedKeys[i];
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
            
            long offset = ((long)page - 1) * pageSize;
            sectionsToReturn = offset > int.MaxValue
                ? new List<(IHomeScreenSection, int)>()
                : sectionsToReturn.Skip((int)offset).Take(pageSize).ToList();
            if ((isComplete && !userSectionsData.SectionsInProgress.Any()) || sectionsToReturn.Count == pageSize)
            {
                return sectionsToReturn
                    .Select(x => SectionToInfo(x.Section, x.ConfiguredOrder, language))
                    .ToList();
            }

            // Return nothing if we don't have the complete picture.
            return null;
        }

        private Guid GeneratePageHash(Guid userId)
        {
            Guid pageHash = Guid.NewGuid();
            m_dataCache.PageHashOwnerIds.TryAdd(pageHash, userId);
            m_dataCache.PageHashExpiry.TryAdd(pageHash, DateTime.UtcNow.AddHours(1)); // TODO: In a future update we should make this configurable.
            
            return pageHash;
        }

        public List<HomeScreenSectionInfo>? MonitorLiveUpdatedSectionsForUser(Guid userId, string? language, int page, int? pageSize = null, Guid? pageHash = null)
        {
            return MonitorLiveUpdatedSectionsForUserAsync(userId, language, page, pageSize, pageHash, CancellationToken.None).GetAwaiter().GetResult();
        }

        public async Task<List<HomeScreenSectionInfo>> MonitorLiveUpdatedSectionsForUserAsync(Guid userId, string? language, int page, int? pageSize, Guid? pageHash, CancellationToken cancellationToken)
        {
            if (page < 1 || pageSize < 1 || (pageSize.HasValue && (long)(page - 1) * pageSize.Value > int.MaxValue))
            {
                throw new ArgumentOutOfRangeException(nameof(page), "Page and page size must be positive.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            await ClearExpiredUserCaches(userId);
            if (pageHash.HasValue && !DoesPageBelongToUser(pageHash.Value, userId))
            {
                pageHash = GeneratePageHash(userId);
            }

            pageHash ??= GetActiveTempPageCacheForUser(userId) ?? GeneratePageHash(userId);
            UserSectionsData cache = GetOrStartSectionsForUser(userId, pageHash.Value);
            using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(c_generationTimeout);
            try
            {
                await cache.Initialized.Task.WaitAsync(deadline.Token);
                ThrowIfInitializationFailed(cache);

                foreach (Task sectionTask in cache.SectionTasks.OrderBy(x => x.Key).Select(x => x.Value))
                {
                    await sectionTask.WaitAsync(deadline.Token);
                    List<HomeScreenSectionInfo>? sections = GetCachedSectionsForUser(userId, language, page,
                        pageSize ?? cache.OrderedSections.SelectMany(x => x.Value).Count(), pageHash.Value);
                    if (sections != null && pageSize.HasValue)
                    {
                        return sections;
                    }
                }

                // Empty configuration and groups producing no instances are successful completed results.
                return GetCachedSectionsForUser(userId, language, page,
                    pageSize ?? cache.OrderedSections.SelectMany(x => x.Value).Count(), pageHash.Value) ?? new List<HomeScreenSectionInfo>();
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("Section generation timed out.");
            }
        }

        public void CacheSectionsForUser(Guid userId, Guid? pageHash = null)
        {
            UserSectionsData cache = GetOrStartSectionsForUser(userId, pageHash ?? GeneratePageHash(userId));
            cache.Completion.WaitAsync(c_generationTimeout).GetAwaiter().GetResult();
            ThrowIfInitializationFailed(cache);
        }

        private static void ThrowIfInitializationFailed(UserSectionsData cache)
        {
            if (cache.InitializationError is OperationCanceledException || cache.InitializationError is TimeoutException)
            {
                throw new TimeoutException("Section generation timed out.");
            }

            if (cache.InitializationError != null)
            {
                throw new InvalidOperationException("Section initialization failed.", cache.InitializationError);
            }
        }

        private UserSectionsData GetOrStartSectionsForUser(Guid userId, Guid pageHash)
        {
            lock (m_cacheCreationLock)
            {
                if (m_dataCache.Cache.TryGetValue(pageHash, out UserSectionsData? existing))
                {
                    return existing;
                }

                UserSectionsData cache = new UserSectionsData() { UserId = userId, MaxOrderIndex = 0 };
                m_dataCache.Cache[pageHash] = cache;
                cache.Completion = Task.Run(() => BuildSectionsForUserAsync(userId, cache));
                return cache;
            }
        }

        private async Task BuildSectionsForUserAsync(Guid userId, UserSectionsData cache)
        {
            using CancellationTokenSource deadline = new CancellationTokenSource(c_generationTimeout);
            try
            {
                // Third-party factories keep their synchronous contract. Waiting is bounded even if one ignores cancellation.
                (List<IHomeScreenSection> sectionTypes, IGrouping<int, SectionSettings>[] groupedSections) =
                    await Task.Run(() => GetSectionConfiguration(userId)).WaitAsync(deadline.Token);
                cache.MaxOrderIndex = groupedSections.Select(x => x.Key).DefaultIfEmpty(0).Max();
                foreach (IGrouping<int, SectionSettings> group in groupedSections)
                {
                    cache.SectionsInProgress.TryAdd(group.Key, true);
                }

                int[] sectionIndices = cache.SectionsInProgress.Keys.OrderBy(x => x).ToArray();
                for (int i = 1; i < sectionIndices.Length; i++)
                {
                    if ((long)sectionIndices[i] - sectionIndices[i - 1] > 1)
                    {
                        cache.OrderIndicesWithoutSections.Add(new IntRange() { Start = sectionIndices[i - 1] + 1, End = sectionIndices[i] - 1 });
                    }
                }

                foreach (IGrouping<int, SectionSettings> group in groupedSections)
                {
                    cache.SectionTasks.Add(group.Key, BuildSectionGroupAsync(userId, cache, sectionTypes, group, deadline.Token));
                }

                cache.Initialized.TrySetResult(true);
                await Task.WhenAll(cache.SectionTasks.Values);
            }
            catch (Exception exception)
            {
                cache.InitializationError = exception;
                m_logger.LogError(exception, "Section initialization failed for user {UserId}.", userId);
            }
            finally
            {
                cache.SectionsInProgress.Clear();
                cache.Initialized.TrySetResult(true);
            }
        }

        private (List<IHomeScreenSection> SectionTypes, IGrouping<int, SectionSettings>[] Groups) GetSectionConfiguration(Guid userId)
        {
            ModularHomeUserSettings? settings = m_homeScreenManager.GetUserSettings(userId);
            List<IHomeScreenSection> sectionTypes = m_homeScreenManager.GetSectionTypes()
                .Where(x => settings?.EnabledSections.Contains(x.Section ?? string.Empty) ?? false).ToList();
            IGrouping<int, SectionSettings>[] groupedSections = HomeScreenSectionsPlugin.Instance.Configuration.SectionSettings
                .OrderBy(x => x.OrderIndex).GroupBy(x => x.OrderIndex).ToArray();
            return (sectionTypes, groupedSections);
        }

        private async Task BuildSectionGroupAsync(Guid userId, UserSectionsData cache, List<IHomeScreenSection> sectionTypes,
            IGrouping<int, SectionSettings> group, CancellationToken cancellationToken)
        {
            try
            {
                List<IHomeScreenSection> sections = await Task.Run(() => CreateSectionGroup(userId, sectionTypes, group, cancellationToken))
                    .WaitAsync(cancellationToken);
                cache.OrderedSections.TryAdd(group.Key, sections);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                m_logger.LogWarning("Section group {OrderIndex} timed out for user {UserId}.", group.Key, userId);
                cache.OrderedSections.TryAdd(group.Key, Array.Empty<IHomeScreenSection>());
                throw new TimeoutException("Section generation timed out.");
            }
            catch (Exception exception)
            {
                m_logger.LogError(exception, "Section group {OrderIndex} failed for user {UserId}.", group.Key, userId);
                cache.OrderedSections.TryAdd(group.Key, Array.Empty<IHomeScreenSection>());
            }
            finally
            {
                cache.SectionsInProgress.TryRemove(group.Key, out _);
            }
        }

        private List<IHomeScreenSection> CreateSectionGroup(Guid userId, List<IHomeScreenSection> sectionTypes,
            IGrouping<int, SectionSettings> group, CancellationToken cancellationToken)
        {
            ConcurrentBag<IHomeScreenSection> sections = new ConcurrentBag<IHomeScreenSection>();
            Parallel.ForEach(group, new ParallelOptions() { CancellationToken = cancellationToken }, sectionSettings =>
            {
                IHomeScreenSection? sectionType = sectionTypes.FirstOrDefault(x => x.Section == sectionSettings.SectionId);
                if (sectionType == null)
                {
                    return;
                }

                try
                {
                    int instanceCount = 1;
                    if (sectionType.Limit > 1)
                    {
                        if (sectionSettings.LowerLimit < 0 || sectionSettings.UpperLimit < sectionSettings.LowerLimit || sectionSettings.UpperLimit > sectionType.Limit)
                        {
                            throw new ArgumentOutOfRangeException(nameof(sectionSettings), "Invalid section instance bounds.");
                        }

                        instanceCount = Random.Shared.Next(sectionSettings.LowerLimit, sectionSettings.UpperLimit);
                    }

                    foreach (IHomeScreenSection section in sectionType.CreateInstances(userId, instanceCount))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        sections.Add(section);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    // A failed factory must not stop other rows or strand the page's completion.
                    m_logger.LogError(exception, "An error occurred while creating section instances for user {UserId} and section {Section}.", userId, sectionType.Section);
                }
            });
            List<IHomeScreenSection> result = sections.ToList();
            result.Shuffle();
            return result;
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
            return m_dataCache.PageHashOwnerIds.TryGetValue(pageHash, out Guid ownerId) && ownerId == userId;
        }
    }

    public class UserHomeSections
    {
        public Guid PageHash { get; set; }
        public List<HomeScreenSectionInfo> Sections { get; set; } = new List<HomeScreenSectionInfo>();
    }
}
