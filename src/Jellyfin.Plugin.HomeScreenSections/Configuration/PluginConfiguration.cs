using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.HomeScreenSections.Configuration
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public bool Enabled { get; set; } = false;

        public bool AllowUserOverride { get; set; } = true;

        public string? LibreTranslateUrl { get; set; } = "";

        public string? LibreTranslateApiKey { get; set; } = "";
        
        public SectionSettings[] SectionSettings { get; set; } = Array.Empty<SectionSettings>();
    }

    public enum SectionViewMode
    {
        Portrait,
        Landscape,
        Square
    }

    public enum UserOverrideItem
    {
        CustomDisplayName,
        ShowPlayedItems,
        EnableDisableSection,
        PreventDuplicates
    }
    
    public class UserOverrideSetting
    {
        public UserOverrideItem Item { get; set; }
        public bool AllowUserOverride { get; set; } = true;
    }
    
    public class SectionSettings
    {
        public string SectionId { get; set; } = string.Empty;
        
        public bool Enabled { get; set; }
        
        public int LowerLimit { get; set; }
        
        public int UpperLimit { get; set; }

        public int OrderIndex { get; set; }
        
        public SectionViewMode ViewMode { get; set; } = SectionViewMode.Landscape;
        
        public string? CustomDisplayName { get; set; }
        
        public bool ShowPlayedItems { get; set; } = true;

        public bool PreventDuplicates { get; set; } = true;

        public UserOverrideSetting[] UserOverrideSettings { get; set; } = new UserOverrideSetting[]
        {
            new UserOverrideSetting { Item = UserOverrideItem.CustomDisplayName, AllowUserOverride = true },
            new UserOverrideSetting { Item = UserOverrideItem.ShowPlayedItems, AllowUserOverride = true },
            new UserOverrideSetting { Item = UserOverrideItem.PreventDuplicates, AllowUserOverride = true },
            new UserOverrideSetting { Item = UserOverrideItem.EnableDisableSection, AllowUserOverride = true }
        };
    }
}