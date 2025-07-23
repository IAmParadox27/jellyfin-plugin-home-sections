﻿using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.HomeScreenSections.Configuration
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public bool Enabled { get; set; } = false;

        public bool AllowUserOverride { get; set; } = true;

        public string? LibreTranslateUrl { get; set; } = "";

        public string? LibreTranslateApiKey { get; set; } = "";
        
        public string? DefaultMoviesLibraryId { get; set; } = "";
        
        public string? DefaultTVShowsLibraryId { get; set; } = "";
        
        public SectionSettings[] SectionSettings { get; set; } = Array.Empty<SectionSettings>();
    }

    public enum SectionViewMode
    {
        Portrait,
        Landscape,
        Square
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
    }
}