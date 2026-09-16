using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace Jellyfin.Plugin.HomeScreenSections.Configuration
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum PluginConfigurationType
    {
        Checkbox,
        Dropdown,
        TextBox,
        NumberBox
    }
    
    public abstract class PluginConfigurationOption
    {
        public string Key { get; set; } = string.Empty;
        
        public string Name { get; set; } = string.Empty;
        
        public string Description { get; set; } = string.Empty;
        
        public abstract PluginConfigurationType Type { get; }
        
        public object? DefaultValue { get; set; }
        
        public string? Placeholder { get; set; }
        
        public string? ValidationMessage { get; set; }
        
        public bool Required { get; set; } = false;
        
        public required string TranslationKey { get; set; } = string.Empty;
        
        public virtual int? MinLength { get; set; } = null;
        
        public virtual int? MaxLength { get; set; } = null;
        
        public virtual string? Pattern { get; set; } = null;
        
        public virtual double? MinValue { get; set; } = null;
        
        public virtual double? MaxValue { get; set; } = null;
        
        public virtual double? Step { get; set; } = null;

        public virtual Dictionary<string, string>? DropdownOptions { get; set; } = null;
    }

    public class CheckboxConfigurationOption : PluginConfigurationOption
    {
        public override PluginConfigurationType Type => PluginConfigurationType.Checkbox;
    }
    
    public class TextBoxConfigurationOption : PluginConfigurationOption
    {
        public override PluginConfigurationType Type => PluginConfigurationType.TextBox;
        
        public override required int? MinLength { get; set; }
        
        public override required int? MaxLength { get; set; }
        
        public override required string? Pattern { get; set; }
    }
    
    public class NumericConfigurationOption : PluginConfigurationOption
    {
        public override PluginConfigurationType Type => PluginConfigurationType.NumberBox;
        
        public override required double? MinValue { get; set; }
        
        public override required double? MaxValue { get; set; }
        
        public override required double? Step { get; set; }
    }
    
    public class DropdownConfigurationOption : PluginConfigurationOption
    {
        public override PluginConfigurationType Type => PluginConfigurationType.Dropdown;
        
        public override required Dictionary<string, string>? DropdownOptions { get; set; }
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
}