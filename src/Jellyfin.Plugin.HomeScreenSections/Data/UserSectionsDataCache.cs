using System.Collections.Concurrent;
using Jellyfin.Plugin.HomeScreenSections.Library;

namespace Jellyfin.Plugin.HomeScreenSections.Data
{
    public class UserSectionsDataCache
    {
        // The GUID here represents the page hash
        public ConcurrentDictionary<Guid, UserSectionsData> Cache { get; set; } = new ConcurrentDictionary<Guid, UserSectionsData>();
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