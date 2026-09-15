using System.Collections.Concurrent;
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
            
            if (userSectionsData.UserId != userId)
            {
                throw new UnauthorizedAccessException("The page cache belongs to another user.");
            }

            if (!userSectionsData.Initialized.Task.IsCompleted)
            {
                return null;
            }
            ThrowIfInitializationFailed(userSectionsData);

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
            return MonitorLiveUpdatedSectionsForUserAsync(userId, language, page, pageSize, pageHash, CancellationToken.None).GetAwaiter().GetResult();
        }

        public async Task<List<HomeScreenSectionInfo>> MonitorLiveUpdatedSectionsForUserAsync(Guid userId, string? language, int page, int? pageSize, Guid? pageHash, CancellationToken cancellationToken)
        {
            if (page < 1 || pageSize < 1 || (pageSize.HasValue && (long)(page - 1) * pageSize.Value > int.MaxValue))
            {
                throw new ArgumentOutOfRangeException(nameof(page), "Page and page size must be positive.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            Guid activePageHash = m_dataCache.BeginUse(userId, pageHash);
            try
            {
                return await MonitorCachedSectionsForUserAsync(userId, language, page, pageSize, activePageHash, cancellationToken);
            }
            finally
            {
                m_dataCache.EndUse(activePageHash);
            }
        }

        private async Task<List<HomeScreenSectionInfo>> MonitorCachedSectionsForUserAsync(Guid userId, string? language,
            int page, int? pageSize, Guid pageHash, CancellationToken cancellationToken)
        {
            UserSectionsData cache = GetOrStartSectionsForUser(userId, pageHash);
            using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(c_generationTimeout);
            try
            {
                if (!pageSize.HasValue)
                {
                    await cache.Completion.WaitAsync(deadline.Token);
                    ThrowIfInitializationFailed(cache);
                }
                else
                {
                    await cache.Initialized.Task.WaitAsync(deadline.Token);
                    ThrowIfInitializationFailed(cache);

                    foreach (Task sectionTask in cache.SectionTasks.OrderBy(x => x.Key).Select(x => x.Value))
                    {
                        await sectionTask.WaitAsync(deadline.Token);
                        List<HomeScreenSectionInfo>? sections = GetCachedSectionsForUser(userId, language, page,
                            pageSize.Value, pageHash);
                        if (sections != null)
                        {
                            return sections;
                        }
                    }
                }

                // Empty configuration and groups producing no instances are successful completed results.
                return GetCachedSectionsForUser(userId, language, page,
                    pageSize ?? cache.OrderedSections.SelectMany(x => x.Value).Count(), pageHash) ?? new List<HomeScreenSectionInfo>();
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("Section generation timed out.");
            }
        }

        public void CacheSectionsForUser(Guid userId, Guid? pageHash = null)
        {
            Guid activePageHash = m_dataCache.BeginUse(userId, pageHash);
            try
            {
                UserSectionsData cache = GetOrStartSectionsForUser(userId, activePageHash);
                cache.Completion.WaitAsync(c_generationTimeout).GetAwaiter().GetResult();
                ThrowIfInitializationFailed(cache);
            }
            finally
            {
                m_dataCache.EndUse(activePageHash);
            }
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
                cache.Completion = BuildSectionsForUserAsync(userId, cache);
                return cache;
            }
        }

        private async Task BuildSectionsForUserAsync(Guid userId, UserSectionsData cache)
        {
            long startedTimestamp = Stopwatch.GetTimestamp();
            using CancellationTokenSource deadline = new CancellationTokenSource(c_generationTimeout);
            CancellationToken cancellationToken = deadline.Token;
            try
            {
                // Configuration and third-party factories keep their synchronous contract off the request thread.
                await Task.Run(() => BuildSectionsForUser(userId, cache, cancellationToken, startedTimestamp)).WaitAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                ThrowIfGenerationTimedOut(startedTimestamp);
                cache.CompletedSections = cache.OrderedSections.OrderBy(x => x.Key)
                    .SelectMany(x => x.Value.Select(section => (section, x.Key)))
                    .ToArray();
            }
            catch (AggregateException exception) when (exception.Flatten().InnerExceptions.Any(x => x is TimeoutException))
            {
                cache.InitializationError = new TimeoutException("Section generation timed out.", exception);
                m_logger.LogError(exception, "Section generation timed out for user {UserId}.", userId);
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

        private void BuildSectionsForUser(Guid userId, UserSectionsData cache, CancellationToken cancellationToken, long startedTimestamp)
        {
            ThrowIfGenerationTimedOut(startedTimestamp);
            ModularHomeUserSettings? settings = m_homeScreenManager.GetUserSettings(userId);
            List<IHomeScreenSection> sectionTypes = m_homeScreenManager.GetSectionTypes()
                .Where(x => settings?.EnabledSections.Contains(x.Section ?? string.Empty) ?? false).ToList();
            IGrouping<int, SectionSettings>[] groupedSections = HomeScreenSectionsPlugin.Instance.Configuration.SectionSettings
                .OrderBy(x => x.OrderIndex).GroupBy(x => x.OrderIndex).ToArray();
            cancellationToken.ThrowIfCancellationRequested();
            cache.MaxOrderIndex = groupedSections.Select(x => x.Key).DefaultIfEmpty(0).Max();
            Dictionary<int, TaskCompletionSource<bool>> completions = new Dictionary<int, TaskCompletionSource<bool>>();
            foreach (IGrouping<int, SectionSettings> group in groupedSections)
            {
                TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                completions.Add(group.Key, completion);
                cache.SectionTasks.Add(group.Key, completion.Task);
                cache.SectionsInProgress.TryAdd(group.Key, true);
            }

            for (int i = 1; i < groupedSections.Length; i++)
            {
                int previous = groupedSections[i - 1].Key;
                int current = groupedSections[i].Key;
                if ((long)current - previous > 1)
                {
                    cache.OrderIndicesWithoutSections.Add(new IntRange() { Start = previous + 1, End = current - 1 });
                }
            }

            ThrowIfGenerationTimedOut(startedTimestamp);
            cache.Initialized.TrySetResult(true);
            Parallel.ForEach(groupedSections, new ParallelOptions() { CancellationToken = cancellationToken }, group =>
            {
                try
                {
                    ThrowIfGenerationTimedOut(startedTimestamp);
                    List<IHomeScreenSection> sections = CreateSectionGroup(userId, sectionTypes, group, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    ThrowIfGenerationTimedOut(startedTimestamp);
                    cache.OrderedSections.TryAdd(group.Key, sections);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    completions[group.Key].TrySetCanceled(cancellationToken);
                    throw;
                }
                catch (TimeoutException exception)
                {
                    completions[group.Key].TrySetException(exception);
                    throw;
                }
                catch (Exception exception)
                {
                    m_logger.LogError(exception, "Section group {OrderIndex} failed for user {UserId}.", group.Key, userId);
                    ThrowIfGenerationTimedOut(startedTimestamp, completions[group.Key]);
                    cache.OrderedSections.TryAdd(group.Key, Array.Empty<IHomeScreenSection>());
                }
                finally
                {
                    cache.SectionsInProgress.TryRemove(group.Key, out _);
                    completions[group.Key].TrySetResult(true);
                }
            });
        }

        private static void ThrowIfGenerationTimedOut(long startedTimestamp, TaskCompletionSource<bool>? completion = null)
        {
            // A queued timer callback can be late when the thread pool is busy.
            if (Stopwatch.GetElapsedTime(startedTimestamp) >= c_generationTimeout)
            {
                TimeoutException exception = new TimeoutException("Section generation timed out.");
                completion?.TrySetException(exception);
                throw exception;
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
