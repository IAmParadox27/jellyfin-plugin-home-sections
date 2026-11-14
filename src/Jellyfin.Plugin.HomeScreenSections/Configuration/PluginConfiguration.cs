using System.Xml.Serialization;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.HomeScreenSections.Configuration
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public bool Enabled { get; set; } = false;

        public bool LazyLoadEnabled { get; set; } = false;

        public int NumSectionsPerPage { get; set; } = 10;
        /// <summary>
        /// Keep every section of a page hidden behind a placeholder until they have all loaded (or <see cref="RevealTimeoutMs"/>
        /// has passed), then show them together, instead of each section appearing and pushing the ones below it down.
        /// </summary>
        public bool RevealSectionsTogether { get; set; } = false;

        /// <summary>How long to wait for a page of sections before showing whatever has loaded.</summary>
        public int RevealTimeoutMs { get; set; } = 1200;

        /// <summary>
        /// Load the card images of every row ahead of time instead of when a card is scrolled near. Cards in the first screen are
        /// ready before the sections are revealed; the rest are fetched in the background and shown finished, with no blurhash
        /// and no fade-in, when they are scrolled into view.
        /// </summary>
        public bool PreloadSectionImages { get; set; } = false;

        /// <summary>
        /// Paint an outline of the home page (rows of placeholder cards) the moment the page starts loading, instead of a blank
        /// page until the web app has started and the first section is created. Takes effect from a user's second visit, because
        /// the browser has to know about the option before the app has started.
        /// </summary>
        public bool ShowBootSkeleton { get; set; } = false;
        
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

        public bool FilterUpcomingByLibraryAccess { get; set; } = true;

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
        
        [XmlArray("PluginConfigurations")]
        [XmlArrayItem("Entry")]
        public PluginConfigurationEntry[] PluginConfigurations { get; set; } = Array.Empty<PluginConfigurationEntry>();
        
        public T? GetAdminConfig<T>(string key, T? defaultValue = default)
        {
            PluginConfigurationEntry? entry = PluginConfigurations.FirstOrDefault(x => x.Key == key);
            if (entry?.Value == null) 
            {
                return defaultValue;
            }
            
            try
            {
                T? result = entry.Type.ToLower() switch
                {
                    "boolean" or "bool" or "checkbox" => (T)(object)bool.Parse(entry.Value),
                    "integer" or "int32" or "int" => (T)(object)int.Parse(entry.Value),
                    "double" or "number" or "numberbox" => (T)(object)double.Parse(entry.Value),
                    "decimal" => (T)(object)decimal.Parse(entry.Value),
                    "string" => (T)(object)entry.Value,
                    _ => ConvertValue<T>(entry.Value, defaultValue)
                };
                
                return result;
            }
            catch
            {
                return defaultValue;
            }
        }

        private static T? ConvertValue<T>(string value, T? defaultValue)
        {
            try
            {
                return (T?)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return defaultValue;
            }
        }
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