using Jellyfin.Plugin.HomeScreenSections.Configuration;

namespace Jellyfin.Plugin.HomeScreenSections.Helpers
{
    public static class PluginConfigurationHelper
    {
        public static PluginConfigurationOption CreateCheckbox(string key, string name, string description, string translationKey, bool defaultValue = false, bool required = false)
        {
            return new CheckboxConfigurationOption
            {
                Key = key,
                Name = name,
                Description = description,
                DefaultValue = defaultValue,
                Required = required,
                TranslationKey = translationKey
            };
        }

        public static PluginConfigurationOption CreateDropdown(string key, string name, string description, Dictionary<string, string> options, string translationKey, string? defaultValue = null, bool required = false)
        {   
            return new DropdownConfigurationOption
            {
                Key = key,
                Name = name,
                Description = description,
                DropdownOptions = options,
                DefaultValue = defaultValue ?? options.FirstOrDefault().Key,
                Required = required,
                TranslationKey = translationKey
            };
        }

        public static PluginConfigurationOption CreateTextBox(string key, string name, string description, string translationKey, string defaultValue = "", bool required = false, string? placeholder = null, int? minLength = null, int? maxLength = null, string? pattern = null, string? validationMessage = null)
        {
            return new TextBoxConfigurationOption
            {
                Key = key,
                Name = name,
                Description = description,
                DefaultValue = defaultValue,
                Required = required,
                Placeholder = placeholder,
                MinLength = minLength,
                MaxLength = maxLength,
                Pattern = pattern,
                ValidationMessage = validationMessage,
                TranslationKey = translationKey
            };
        }

        public static PluginConfigurationOption CreateNumberBox(string key, string name, string description, string translationKey, double defaultValue = 0, bool required = false, string? placeholder = null, double? minValue = null, double? maxValue = null, double? step = null, string? validationMessage = null)
        {
            return new NumericConfigurationOption
            {
                Key = key,
                Name = name,
                Description = description,
                DefaultValue = defaultValue,
                Required = required,
                Placeholder = placeholder,
                MinValue = minValue,
                MaxValue = maxValue,
                Step = step,
                ValidationMessage = validationMessage,
                TranslationKey = translationKey
            };
        }
    }
}
