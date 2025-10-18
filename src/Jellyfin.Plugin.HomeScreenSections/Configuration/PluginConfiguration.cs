using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.HomeScreenSections.Configuration
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public bool Enabled { get; set; } = false;

        public bool AllowUserOverride { get; set; } = true;

        public string? LibreTranslateUrl { get; set; } = "";

        public string? LibreTranslateApiKey { get; set; } = "";
        
        public string? JellyseerrUrl { get; set; } = "";

        public string? JellyseerrApiKey { get; set; } = "";
        
        public string? JellyseerrPreferredLanguages { get; set; } = "en";
        
        public string? DefaultMoviesLibraryId { get; set; } = "";
        
        public string? DefaultTVShowsLibraryId { get; set; } = "";
        
        public string? DefaultMusicLibraryId { get; set; } = "";
        
        public string? DefaultBooksLibraryId { get; set; } = "";

        public ArrConfig Sonarr { get; set; } = new ArrConfig { UpcomingTimeframeValue = 1, UpcomingTimeframeUnit = TimeframeUnit.Weeks };

        public ArrConfig Radarr { get; set; } = new ArrConfig { UpcomingTimeframeValue = 3, UpcomingTimeframeUnit = TimeframeUnit.Months };

        public ArrConfig Lidarr { get; set; } = new ArrConfig { UpcomingTimeframeValue = 6, UpcomingTimeframeUnit = TimeframeUnit.Months };

        public ArrConfig Readarr { get; set; } = new ArrConfig { UpcomingTimeframeValue = 1, UpcomingTimeframeUnit = TimeframeUnit.Years };

        public string DateFormat { get; set; } = "YYYY/MM/DD";

        public string DateDelimiter { get; set; } = "/";
        public bool DeveloperMode { get; set; } = false;

        public int CacheBustCounter { get; set; } = 0;

        public int CacheTimeoutSeconds { get; set; } = 86400;

        public bool OverrideStreamyfinHome { get; set; } = false;

        public SectionSettings[] SectionSettings { get; set; } = Array.Empty<SectionSettings>();

        public List<NetworkConfig> JellyseerrNetworks { get; set; } = new List<NetworkConfig>
        {
            new NetworkConfig { Id = 213, Name = "Netflix", Enabled = false },
            new NetworkConfig { Id = 2739, Name = "Disney+", Enabled = false },
            new NetworkConfig { Id = 49, Name = "HBO", Enabled = false },
            new NetworkConfig { Id = 1024, Name = "Amazon Prime Video", Enabled = false },
            new NetworkConfig { Id = 174, Name = "AMC", Enabled = false },
            new NetworkConfig { Id = 453, Name = "Hulu", Enabled = false }
        };
    }

    public enum SectionViewMode
    {
        Portrait,
        Landscape,
        Square,
        Small
    }

    public enum TimeframeUnit
    {
        Days,
        Weeks,
        Months,
        Years
    }
    
    public class SectionSettings
    {
        public string SectionId { get; set; } = string.Empty;
        
        public bool Enabled { get; set; }
        
        public bool AllowUserOverride { get; set; }
        
        public int LowerLimit { get; set; }
        
        public int UpperLimit { get; set; }

        public int OrderIndex { get; set; }
        
        public SectionViewMode ViewMode { get; set; } = SectionViewMode.Landscape;

        public bool HideWatchedItems { get; set; } = false;
    }
    
    public class ArrConfig
    {
        public string? ApiKey { get; set; } = "";
        public string? Url { get; set; } = "";
        public int UpcomingTimeframeValue { get; set; }
        public TimeframeUnit UpcomingTimeframeUnit { get; set; }
    }

    public class NetworkConfig
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool Enabled { get; set; }
    }
}