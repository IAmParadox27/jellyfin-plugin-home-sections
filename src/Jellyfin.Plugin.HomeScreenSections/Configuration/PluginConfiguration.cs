using System.Text.Json.Serialization;
using System.Xml.Serialization;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.HomeScreenSections.Configuration
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public bool Enabled { get; set; } = false;

        public bool AllowUserOverride { get; set; } = true;

        public bool RespectUserHomepage { get; set; } = true;

        public string? LibreTranslateUrl { get; set; } = "";

        public string? LibreTranslateApiKey { get; set; } = "";
        
        public string? JellyseerrUrl { get; set; } = "";

        public string? JellyseerrApiKey { get; set; } = "";
        
        public string? JellyseerrPreferredLanguages { get; set; } = "en";
        
        public string? DefaultMoviesLibraryId { get; set; } = "";
        
        public string? DefaultTVShowsLibraryId { get; set; } = "";
        
        public SectionSettings[] SectionSettings { get; set; } = Array.Empty<SectionSettings>();
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum SectionViewMode
    {
        Portrait,
        Landscape,
        Square
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum UserOverrideItem
    {
        CustomDisplayName,
        EnableDisableSection
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum PluginConfigurationType
    {
        Checkbox,
        Dropdown,
        TextBox,
        NumberBox
    }

    public class PluginConfigurationOption
    {
        public string Key { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public PluginConfigurationType Type { get; set; }
        public bool AllowUserOverride { get; set; } = false;
        public bool IsAdvanced { get; set; } = false;
        public object? DefaultValue { get; set; }
        public string[]? DropdownOptions { get; set; } // For dropdown type
        public string[]? DropdownLabels { get; set; } // Human-readable labels for dropdown options
        
        // Additional properties for text/number validation
        public string? Placeholder { get; set; } // Placeholder text for text/number inputs
        public int? MinLength { get; set; } // Minimum length for text inputs
        public int? MaxLength { get; set; } // Maximum length for text inputs
        public double? MinValue { get; set; } // Minimum value for number inputs
        public double? MaxValue { get; set; } // Maximum value for number inputs
        public double? Step { get; set; } // Step increment for number inputs
        public string? Pattern { get; set; } // Regex pattern for text validation
        public string? ValidationMessage { get; set; } // Custom validation error message
    }
    
    public class UserOverrideSetting
    {
        public UserOverrideItem Item { get; set; }
        public bool AllowUserOverride { get; set; } = true;
    }

    [XmlType("PluginConfigurationEntry")]
    public class PluginConfigurationEntry
    {
        [XmlAttribute("Key")]
        public string Key { get; set; } = string.Empty;
        
        [XmlAttribute("Value")]
        public string? Value { get; set; }
        
        [XmlAttribute("Type")]
        public string Type { get; set; } = "string";
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
        
        public UserOverrideSetting[] UserOverrideSettings { get; set; } = new UserOverrideSetting[]
        {
            new UserOverrideSetting { Item = UserOverrideItem.CustomDisplayName, AllowUserOverride = true },
            new UserOverrideSetting { Item = UserOverrideItem.EnableDisableSection, AllowUserOverride = true }
        };

        // Plugin-defined configuration options with admin default values
        [XmlArray("PluginConfigurations")]
        [XmlArrayItem("Entry")]
        public PluginConfigurationEntry[] PluginConfigurations { get; set; } = Array.Empty<PluginConfigurationEntry>();
        
        // Helper method to get configuration value with type conversion
        public T? GetPluginConfiguration<T>(string key, T? defaultValue = default)
        {
            var entry = PluginConfigurations?.FirstOrDefault(x => x.Key == key);
            if (entry?.Value == null) return defaultValue;
            
            try
            {
                if (typeof(T) == typeof(bool))
                {
                    return (T)(object)bool.Parse(entry.Value);
                }
                else if (typeof(T) == typeof(int))
                {
                    return (T)(object)int.Parse(entry.Value);
                }
                else if (typeof(T) == typeof(double))
                {
                    return (T)(object)double.Parse(entry.Value);
                }
                else if (typeof(T) == typeof(decimal))
                {
                    return (T)(object)decimal.Parse(entry.Value);
                }
                else if (typeof(T) == typeof(string))
                {
                    return (T)(object)entry.Value;
                }
                else
                {
                    return defaultValue;
                }
            }
            catch
            {
                return defaultValue;
            }
        }
        
        // Helper method to set configuration value
        public void SetPluginConfiguration<T>(string key, T? value)
        {
            var entries = PluginConfigurations?.ToList() ?? new List<PluginConfigurationEntry>();
            var existingEntry = entries.FirstOrDefault(x => x.Key == key);
            
            if (existingEntry != null)
            {
                existingEntry.Value = value?.ToString();
                existingEntry.Type = typeof(T).Name.ToLower();
            }
            else
            {
                entries.Add(new PluginConfigurationEntry
                {
                    Key = key,
                    Value = value?.ToString(),
                    Type = typeof(T).Name.ToLower()
                });
            }
            
            PluginConfigurations = entries.ToArray();
        }
    }
}