namespace Jellyfin.Plugin.HomeScreenSections.Services
{
    public class ServerWideStatsCache
    {
        public IReadOnlyList<Guid> MostPlayedItemIds { get; set; } = Array.Empty<Guid>();

        public IReadOnlyList<Guid> RecentlyPlayedItemIds { get; set; } = Array.Empty<Guid>();

        public DateTime? LastComputed { get; set; }
    }
}
