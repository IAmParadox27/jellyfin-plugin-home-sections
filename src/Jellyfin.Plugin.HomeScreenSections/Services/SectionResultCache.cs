using System.Collections.Concurrent;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.HomeScreenSections.Services
{
    /// <summary>
    /// Short-lived, per-user cache of section results, so that going back to the home page (or opening it on a second device)
    /// does not recompute every section. Entries are keyed by user, section and the request parameters, so nothing is ever
    /// shared between users. An entry stops being used when it is older than the configured time, when any library item that
    /// can appear in a section is added, removed or changed, or when that user's own watch data changes (so Continue Watching
    /// is never stale).
    /// </summary>
    public sealed class SectionResultCache : IDisposable
    {
        private const int c_maxEntries = 4000;
        private const int c_sweepEvery = 200;
        private const int c_shortLivedSeconds = 30;

        // Bumped when the plugin configuration is saved: section settings (hide watched, item type, ...) change what a section returns.
        private static long s_configGeneration;

        private sealed class Entry
        {
            public Entry(Lazy<QueryResult<BaseItemDto>> value, DateTime created, TimeSpan lifetime, long libraryGeneration, long userGeneration, long configGeneration)
            {
                Value = value;
                Created = created;
                Lifetime = lifetime;
                LibraryGeneration = libraryGeneration;
                UserGeneration = userGeneration;
                ConfigGeneration = configGeneration;
            }

            public TimeSpan Lifetime { get; }

            public long ConfigGeneration { get; }

            public Lazy<QueryResult<BaseItemDto>> Value { get; }

            public DateTime Created { get; }

            public long LibraryGeneration { get; }

            public long UserGeneration { get; }
        }

        private readonly ConcurrentDictionary<string, Entry> m_entries = new();
        private readonly ConcurrentDictionary<Guid, long> m_userGenerations = new();
        private readonly ILibraryManager m_libraryManager;
        private readonly IUserDataManager m_userDataManager;
        private readonly ILogger m_logger;
        private long m_libraryGeneration;
        private long m_computes;

        public SectionResultCache(ILibraryManager libraryManager, IUserDataManager userDataManager, ILogger<SectionResultCache> logger)
        {
            m_libraryManager = libraryManager;
            m_userDataManager = userDataManager;
            m_logger = logger;

            m_libraryManager.ItemAdded += OnLibraryItemChanged;
            m_libraryManager.ItemRemoved += OnLibraryItemChanged;
            m_libraryManager.ItemUpdated += OnLibraryItemChanged;
            m_userDataManager.UserDataSaved += OnUserDataSaved;
        }

        /// <summary>Drops every cached result. Called when the plugin configuration is saved.</summary>
        public static void InvalidateAll()
        {
            Interlocked.Increment(ref s_configGeneration);
        }

        public QueryResult<BaseItemDto> GetOrCompute(string sectionKey, Guid userId, IQueryCollection query, int ttlSeconds, bool alwaysShortLived, Func<QueryResult<BaseItemDto>> compute)
        {
            // Sections that show state held outside Jellyfin (Seerr, Sonarr/Radarr, sections from other plugins), or that depend on the time
            // of day (Live TV "on now"), get a short life whatever is configured.
            bool shortLived = alwaysShortLived
                || sectionKey.StartsWith("Discover", StringComparison.Ordinal)
                || sectionKey.StartsWith("Upcoming", StringComparison.Ordinal)
                || sectionKey == "MyJellyseerrRequests"
                || sectionKey == "LiveTV";
            TimeSpan lifetime = TimeSpan.FromSeconds(shortLived ? Math.Min(ttlSeconds, c_shortLivedSeconds) : ttlSeconds);

            string key = BuildKey(sectionKey, userId, query);
            long libraryGeneration = Interlocked.Read(ref m_libraryGeneration);
            long userGeneration = m_userGenerations.TryGetValue(userId, out long g) ? g : 0;
            long configGeneration = Interlocked.Read(ref s_configGeneration);
            DateTime now = DateTime.UtcNow;

            Entry fresh = new Entry(new Lazy<QueryResult<BaseItemDto>>(compute, LazyThreadSafetyMode.ExecutionAndPublication), now, lifetime, libraryGeneration, userGeneration, configGeneration);

            // If a usable entry exists use it, otherwise publish ours. Two identical requests at the same moment end up sharing one
            // entry (and, through Lazy, one computation).
            Entry entry = m_entries.AddOrUpdate(key, fresh, (_, current) => IsUsable(current, now, libraryGeneration, userGeneration, configGeneration) ? current : fresh);

            try
            {
                QueryResult<BaseItemDto> result = entry.Value.Value;

                if (Interlocked.Increment(ref m_computes) % c_sweepEvery == 0 || m_entries.Count > c_maxEntries)
                {
                    Sweep();
                }

                return result;
            }
            catch
            {
                m_entries.TryRemove(new KeyValuePair<string, Entry>(key, entry)); // never keep a failure
                throw;
            }
        }

        public void Dispose()
        {
            m_libraryManager.ItemAdded -= OnLibraryItemChanged;
            m_libraryManager.ItemRemoved -= OnLibraryItemChanged;
            m_libraryManager.ItemUpdated -= OnLibraryItemChanged;
            m_userDataManager.UserDataSaved -= OnUserDataSaved;
        }

        // The only request parameters a section's result can depend on. Keying on every parameter would let a caller create an unlimited
        // number of entries by adding junk to the query string.
        private static readonly string[] s_keyParameters = { "AdditionalData", "Language", "EnableRewatching" };

        private static string BuildKey(string sectionKey, Guid userId, IQueryCollection query)
        {
            List<string> parts = new();

            foreach (string name in s_keyParameters)
            {
                if (query.TryGetValue(name, out Microsoft.Extensions.Primitives.StringValues value))
                {
                    parts.Add($"{name}={value}");
                }
            }

            // NextUpDateCutoff is "now minus N days" with a time of day, which would make every request unique: only its date counts.
            if (query.TryGetValue("NextUpDateCutoff", out Microsoft.Extensions.Primitives.StringValues cutoff))
            {
                parts.Add($"NextUpDateCutoff={cutoff.ToString().Split('T')[0]}");
            }

            return $"{userId:N}|{sectionKey}|{string.Join('&', parts)}";
        }

        private void OnLibraryItemChanged(object? sender, ItemChangeEventArgs e)
        {
            // Live TV programmes and channels, people, genres and studios change constantly (guide refreshes, metadata refreshes) and never
            // appear as a section item, so they must not empty the cache. Any other item might.
            if (e.Item is LiveTvProgram or LiveTvChannel or Person or Genre or MusicGenre or Studio)
            {
                return;
            }

            Interlocked.Increment(ref m_libraryGeneration);
        }

        private void OnUserDataSaved(object? sender, UserDataSaveEventArgs e)
        {
            m_userGenerations.AddOrUpdate(e.UserId, 1, (_, current) => current + 1);
        }

        private static bool IsUsable(Entry entry, DateTime now, long libraryGeneration, long userGeneration, long configGeneration)
        {
            return entry.LibraryGeneration == libraryGeneration
                && entry.UserGeneration == userGeneration
                && entry.ConfigGeneration == configGeneration
                && now - entry.Created < entry.Lifetime;
        }

        // Entries are only ever replaced when the same key is asked for again, so remove the ones that can no longer be used (expired, or
        // made stale by a change), and if the cache is still too big, the oldest.
        private void Sweep()
        {
            DateTime now = DateTime.UtcNow;
            long configGeneration = Interlocked.Read(ref s_configGeneration);
            long libraryGeneration = Interlocked.Read(ref m_libraryGeneration);

            foreach (KeyValuePair<string, Entry> pair in m_entries)
            {
                long userGeneration = 0;
                if (Guid.TryParseExact(pair.Key.AsSpan(0, 32), "N", out Guid userId))
                {
                    m_userGenerations.TryGetValue(userId, out userGeneration);
                }

                if (!IsUsable(pair.Value, now, libraryGeneration, userGeneration, configGeneration))
                {
                    m_entries.TryRemove(pair);
                }
            }

            if (m_entries.Count > c_maxEntries)
            {
                foreach (KeyValuePair<string, Entry> pair in m_entries.OrderBy(x => x.Value.Created).Take(m_entries.Count - (c_maxEntries * 3 / 4)))
                {
                    m_entries.TryRemove(pair.Key, out _);
                }
            }

            m_logger.LogDebug("Section result cache holds {Count} entries after sweep", m_entries.Count);
        }
    }
}
