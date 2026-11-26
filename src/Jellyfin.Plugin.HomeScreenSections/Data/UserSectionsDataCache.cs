using System.Collections.Concurrent;
using Jellyfin.Plugin.HomeScreenSections.Library;

namespace Jellyfin.Plugin.HomeScreenSections.Data
{
    public class UserSectionsDataCache
    {
        // The GUID here represents the page hash
        public ConcurrentDictionary<Guid, UserSectionsData> Cache { get; set; } = new ConcurrentDictionary<Guid, UserSectionsData>();
        
        public ConcurrentDictionary<Guid, Guid> PageHashOwnerIds { get; set; } = new ConcurrentDictionary<Guid, Guid>();
        
        public ConcurrentDictionary<Guid, DateTime> PageHashExpiry { get; set; } = new ConcurrentDictionary<Guid, DateTime>();

        /// <summary>
        /// Marks every cached page of this user as expired, so the next home screen load builds it again from the
        /// current settings. Entries are expired rather than removed: a request still reading one of them keeps working,
        /// and the existing cleanup removes them on the user's next request.
        /// </summary>
        public void ExpireUser(Guid userId)
        {
            foreach (Guid pageHash in PageHashOwnerIds.Where(x => x.Value == userId).Select(x => x.Key).ToArray())
            {
                PageHashExpiry[pageHash] = DateTime.MinValue;
            }
        }

        /// <summary>
        /// Marks every cached page of every user as expired (the admin section settings changed).
        /// </summary>
        public void ExpireAll()
        {
            foreach (Guid pageHash in PageHashExpiry.Keys.ToArray())
            {
                PageHashExpiry[pageHash] = DateTime.MinValue;
            }
        }
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

        private int m_isInitialised;

        // Set once SectionsInProgress and OrderIndicesWithoutSections have been populated. Lets readers tell
        // "not started yet" apart from "started, but there's nothing to load" (e.g. no sections enabled).
        public bool IsInitialised
        {
            get => Volatile.Read(ref m_isInitialised) == 1;
            set => Volatile.Write(ref m_isInitialised, value ? 1 : 0);
        }
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