using System.Collections.Concurrent;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.HomeScreenSections.Services
{
    // In-memory, per-user cache for expensive scoring/candidate-scanning work
    // that sections would otherwise redo on every home screen load.
    // Entries are marked stale per user via UserDataSaved rather than a timer,
    // and are refreshed in the background while the stale value is served.
    // maxAge below is only a fallback in case that event is missed.
    public class PerUserComputedStatsCache
    {
        private readonly ConcurrentDictionary<(Guid UserId, string Key), CacheEntry> m_entries = new();
        private readonly ConcurrentDictionary<(Guid UserId, string Key), byte> m_inProgress = new();
        private readonly ConcurrentDictionary<(Guid UserId, string Key), object> m_locks = new();
        private readonly ILogger<PerUserComputedStatsCache> m_logger;

        public PerUserComputedStatsCache(IUserDataManager userDataManager, ILogger<PerUserComputedStatsCache> logger)
        {
            m_logger = logger;
            userDataManager.UserDataSaved += OnUserDataSaved;
        }

        private class CacheEntry
        {
            public required object Data { get; init; }

            public DateTime ComputedAt { get; set; }

            public bool ClearOnUserDataChange { get; init; } = true;
        }

        // Only these two save reasons actually change anything our scores
        // depend on (PlayCount/Played). PlaybackStart/PlaybackProgress fire
        // repeatedly during normal playback (position ticks for resume
        // support) and would thrash every active viewer's cache for no
        // reason; UpdateUserRating is the personal star rating, which none
        // of these sections read.
        private static readonly HashSet<UserDataSaveReason> RelevantSaveReasons = new()
        {
            UserDataSaveReason.PlaybackFinished,
            UserDataSaveReason.TogglePlayed
        };

        private void OnUserDataSaved(object? sender, UserDataSaveEventArgs e)
        {
            if (!RelevantSaveReasons.Contains(e.SaveReason))
            {
                return;
            }

            foreach (KeyValuePair<(Guid UserId, string Key), CacheEntry> pair in m_entries.Where(x => x.Key.UserId == e.UserId && x.Value.ClearOnUserDataChange))
            {
                pair.Value.ComputedAt = DateTime.MinValue;
            }
        }

        // Computes inline the first time a key is needed; afterwards a stale value is
        // returned straight away and refreshed in the background.
        // Returns false only if the computation throws.
        public bool TryGetOrCompute<T>(Guid userId, string key, TimeSpan maxAge, Func<T> compute, out T value, Func<T, bool>? isCacheable = null, bool clearOnUserDataChange = true)
            where T : notnull
        {
            (Guid UserId, string Key) cacheKey = (userId, key);

            if (m_entries.TryGetValue(cacheKey, out CacheEntry? entry) && entry.Data is T typed)
            {
                if (DateTime.UtcNow - entry.ComputedAt >= maxAge && m_inProgress.TryAdd(cacheKey, 0))
                {
                    Task.Run(() =>
                    {
                        try
                        {
                            ComputeAndStore(cacheKey, compute, isCacheable, clearOnUserDataChange);
                        }
                        catch (Exception ex)
                        {
                            m_logger.LogError(ex, "Error refreshing cached stats for user {UserId}, key {Key}", userId, key);
                        }
                        finally
                        {
                            m_inProgress.TryRemove(cacheKey, out _);
                        }
                    });
                }

                value = typed;
                return true;
            }

            lock (m_locks.GetOrAdd(cacheKey, _ => new object()))
            {
                if (m_entries.TryGetValue(cacheKey, out CacheEntry? computedEntry) && computedEntry.Data is T computedTyped)
                {
                    value = computedTyped;
                    return true;
                }

                try
                {
                    value = ComputeAndStore(cacheKey, compute, isCacheable, clearOnUserDataChange);
                    return true;
                }
                catch (Exception ex)
                {
                    m_logger.LogError(ex, "Error computing cached stats for user {UserId}, key {Key}", userId, key);
                }
            }

            value = default!;
            return false;
        }

        private T ComputeAndStore<T>((Guid UserId, string Key) cacheKey, Func<T> compute, Func<T, bool>? isCacheable, bool clearOnUserDataChange)
            where T : notnull
        {
            T computed = compute();

            if (isCacheable == null || isCacheable(computed))
            {
                m_entries[cacheKey] = new CacheEntry { Data = computed, ComputedAt = DateTime.UtcNow, ClearOnUserDataChange = clearOnUserDataChange };
            }

            return computed;
        }
    }
}
