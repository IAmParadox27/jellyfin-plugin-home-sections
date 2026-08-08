using System.Collections.Concurrent;
using Jellyfin.Plugin.HomeScreenSections.Library;

namespace Jellyfin.Plugin.HomeScreenSections.Data
{
    public class UserSectionsDataCache
    {
        // The GUID here represents the page hash
        public ConcurrentDictionary<Guid, UserSectionsData> Cache { get; set; } = new ConcurrentDictionary<Guid, UserSectionsData>();

        /// <summary>
        /// Drop all cached home-section pages so the next home load rebuilds order/content.
        /// </summary>
        public void Clear()
        {
            Cache.Clear();
        }

        /// <summary>
        /// Drop cached pages for a single user.
        /// </summary>
        public void ClearForUser(Guid userId)
        {
            foreach (KeyValuePair<Guid, UserSectionsData> entry in Cache.ToArray())
            {
                if (entry.Value.UserId == userId)
                {
                    Cache.TryRemove(entry.Key, out _);
                }
            }
        }
    }

    public class UserSectionsData
    {
        public DateTime? LastAccessed { get; set; }
        
        public required Guid UserId { get; set; }
        
        public required int MaxOrderIndex { get; set; }
        
        // The int here represents the order index group
        public ConcurrentDictionary<int, IEnumerable<IHomeScreenSection>> OrderedSections { get; set; } = new ConcurrentDictionary<int, IEnumerable<IHomeScreenSection>>();
        
        // This list represents a collection of index numbers that don't have any sections assigned to them
        public ISet<IntRange> OrderIndicesWithoutSections { get; set; } = new HashSet<IntRange>();
        
        // This list represents a collection of index numbers that are currently being processed
        public ConcurrentDictionary<int, bool> SectionsInProgress { get; set; } = new ConcurrentDictionary<int, bool>();
    }
    
    public sealed record IntRange
    {
        public required int Start { get; init; }
        public required int End { get; init; }
        public bool Contains(int value) => value >= Start && value <= End;
    }
}