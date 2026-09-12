using System.Collections.Concurrent;
using Jellyfin.Plugin.HomeScreenSections.Library;

namespace Jellyfin.Plugin.HomeScreenSections.Data
{
    public class UserSectionsDataCache : IDisposable
    {
        private readonly object m_syncLock = new object();
        private readonly Dictionary<Guid, int> m_activeRequests = new Dictionary<Guid, int>();
        private readonly Timer m_cleanupTimer;

        public UserSectionsDataCache()
        {
            m_cleanupTimer = new Timer(_ => ClearExpired(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        public Guid BeginUse(Guid userId, Guid? requestedHash)
        {
            lock (m_syncLock)
            {
                Guid pageHash = requestedHash ?? PageHashExpiry
                    .Where(x => x.Value > DateTime.UtcNow && PageHashOwnerIds.TryGetValue(x.Key, out Guid ownerId) && ownerId == userId)
                    .OrderByDescending(x => x.Value)
                    .Select(x => x.Key)
                    .FirstOrDefault();
                if (pageHash == Guid.Empty)
                {
                    pageHash = Guid.NewGuid();
                }

                if (PageHashOwnerIds.TryGetValue(pageHash, out Guid existingOwnerId) && existingOwnerId != userId)
                {
                    throw new UnauthorizedAccessException("The page cache belongs to another user.");
                }

                if (PageHashExpiry.TryGetValue(pageHash, out DateTime expiry) && expiry <= DateTime.UtcNow &&
                    !m_activeRequests.ContainsKey(pageHash) &&
                    (!Cache.TryGetValue(pageHash, out UserSectionsData? data) || !data.SectionsInProgress.Any()))
                {
                    RemovePage(pageHash);
                }

                PageHashOwnerIds[pageHash] = userId;
                PageHashExpiry[pageHash] = DateTime.UtcNow.AddHours(1);
                m_activeRequests.TryGetValue(pageHash, out int activeRequests);
                m_activeRequests[pageHash] = activeRequests + 1;
                return pageHash;
            }
        }

        public void EndUse(Guid pageHash)
        {
            lock (m_syncLock)
            {
                if (m_activeRequests.TryGetValue(pageHash, out int activeRequests) && activeRequests > 1)
                {
                    m_activeRequests[pageHash] = activeRequests - 1;
                }
                else
                {
                    m_activeRequests.Remove(pageHash);
                }
            }
        }

        public void Touch(Guid pageHash)
        {
            lock (m_syncLock)
            {
                if (PageHashOwnerIds.ContainsKey(pageHash))
                {
                    PageHashExpiry[pageHash] = DateTime.UtcNow.AddHours(1);
                }
            }
        }

        public void ClearExpired()
        {
            lock (m_syncLock)
            {
                Guid[] expiredHashes = PageHashExpiry.Where(x => x.Value <= DateTime.UtcNow).Select(x => x.Key).ToArray();
                foreach (Guid pageHash in expiredHashes)
                {
                    if (m_activeRequests.ContainsKey(pageHash) ||
                        (Cache.TryGetValue(pageHash, out UserSectionsData? data) && data.SectionsInProgress.Any()))
                    {
                        continue;
                    }

                    RemovePage(pageHash);
                }
            }
        }

        private void RemovePage(Guid pageHash)
        {
            Cache.TryRemove(pageHash, out _);
            PageHashOwnerIds.TryRemove(pageHash, out _);
            PageHashExpiry.TryRemove(pageHash, out _);
        }

        public void Dispose()
        {
            m_cleanupTimer.Dispose();
        }

        // The GUID here represents the page hash
        public ConcurrentDictionary<Guid, UserSectionsData> Cache { get; set; } = new ConcurrentDictionary<Guid, UserSectionsData>();
        
        public ConcurrentDictionary<Guid, Guid> PageHashOwnerIds { get; set; } = new ConcurrentDictionary<Guid, Guid>();
        
        public ConcurrentDictionary<Guid, DateTime> PageHashExpiry { get; set; } = new ConcurrentDictionary<Guid, DateTime>();
    }

    public class UserSectionsData
    {
        public DateTime? LastAccessed { get; set; } = null;
        
        public required Guid UserId { get; set; }
        
        public required int MaxOrderIndex { get; set; }
        
        // The int here represents the order index group
        public ConcurrentDictionary<int, IEnumerable<IHomeScreenSection>> OrderedSections { get; set; } = new ConcurrentDictionary<int, IEnumerable<IHomeScreenSection>>();
        
        // This list represents a collection of index numbers that don't have any sections assigned to them
        public HashSet<IntRange> OrderIndicesWithoutSections { get; set; } = new HashSet<IntRange>();
        
        // This list represents a collection of index numbers that are currently being processed
        public ConcurrentDictionary<int, bool> SectionsInProgress { get; set; } = new ConcurrentDictionary<int, bool>();
    }
    
    public class IntRange : IEquatable<IntRange>
    {
        public required int Start { get; init; }
        
        public required int End { get; init; }

        public bool Contains(int value)
        {
            return value >= Start && value <= End;
        }

        public override bool Equals(object? obj)
        {
            return obj is IntRange range && Start == range.Start && End == range.End;
        }

        public bool Equals(IntRange? other)
        {
            return Start == other?.Start && End == other.End;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Start, End);
        }
    }
}