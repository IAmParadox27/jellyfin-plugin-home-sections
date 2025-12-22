using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.HomeScreenSections.Configuration
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public bool Enabled { get; set; } = false;

        public bool LazyLoadEnabled { get; set; } = false;

        public int NumSectionsPerPage { get; set; } = 10;
        
        public bool AllowUserOverride { get; set; } = true;

        public string? LibreTranslateUrl { get; set; } = "";

        public string? LibreTranslateApiKey { get; set; } = "";
        
        public string? JellyseerrUrl { get; set; } = "";

        public string? JellyseerrExternalUrl { get; set; } = "";

        public string? JellyseerrApiKey { get; set; } = "";
        
        public string? JellyseerrPreferredLanguages { get; set; } = "en";
        
        public string? DefaultMoviesLibraryId { get; set; } = "";
        
        public string? DefaultTVShowsLibraryId { get; set; } = "";
        
        public string? DefaultMusicLibraryId { get; set; } = "";
        
        public string? DefaultBooksLibraryId { get; set; } = "";
        
        public string? DefaultMusicVideosLibraryId { get; set; } = "";

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

        public int MaxImageCacheEntries { get; set; } = 10000;

        public int MaxImageWidth { get; set; } = 600;

        public int ImageJpegQuality { get; set; } = 85;

        public SectionSettings[] SectionSettings { get; set; } = Array.Empty<SectionSettings>();
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
        public bool ConsiderCinemaRelease { get; set; } = false;
        public bool ConsiderPhysicalRelease { get; set; } = false;
        public bool ConsiderDigitalRelease { get; set; } = true;
    }   
}