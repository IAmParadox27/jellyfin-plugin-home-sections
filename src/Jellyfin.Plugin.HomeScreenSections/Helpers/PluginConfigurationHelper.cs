using Jellyfin.Plugin.HomeScreenSections.Configuration;

namespace Jellyfin.Plugin.HomeScreenSections.Helpers
{
    /// <summary>
    /// Helper class for external plugins to easily create configuration options
    /// </summary>
    public static class PluginConfigurationHelper
    {
        /// <summary>
        /// Creates a checkbox configuration option
        /// </summary>
        /// <param name="key">Unique identifier for the configuration</param>
        /// <param name="name">Display name shown in UI</param>
        /// <param name="description">Help text describing the option</param>
        /// <param name="defaultValue">Default checkbox state</param>
        /// <param name="userOverridable">Whether this option can potentially be overridden by users (subject to admin permission)</param>
        /// <param name="isAdvanced">Whether this is an advanced option that should be hidden by default</param>
        /// <returns>Configuration option for a checkbox</returns>
        public static PluginConfigurationOption CreateCheckbox(string key, string name, string description, bool defaultValue = false, bool userOverridable = true, bool isAdvanced = false)
        {
            return new PluginConfigurationOption
            {
                Key = key,
                Name = name,
                Description = description,
                Type = PluginConfigurationType.Checkbox,
                DefaultValue = defaultValue,
                AllowUserOverride = userOverridable,
                IsAdvanced = isAdvanced
            };
        }

        /// <summary>
        /// Creates a dropdown configuration option
        /// </summary>
        /// <param name="key">Unique identifier for the configuration</param>
        /// <param name="name">Display name shown in UI</param>
        /// <param name="description">Help text describing the option</param>
        /// <param name="options">Array of option values</param>
        /// <param name="labels">Array of human-readable labels (optional, uses options if null)</param>
        /// <param name="defaultValue">Default selected option</param>
        /// <param name="userOverridable">Whether this option can potentially be overridden by users (subject to admin permission)</param>
        /// <param name="isAdvanced">Whether this is an advanced option that should be hidden by default</param>
        /// <returns>Configuration option for a dropdown</returns>
        public static PluginConfigurationOption CreateDropdown(string key, string name, string description, string[] options, string[]? labels = null, string? defaultValue = null, bool userOverridable = true, bool isAdvanced = false)
        {
            return new PluginConfigurationOption
            {
                Key = key,
                Name = name,
                Description = description,
                Type = PluginConfigurationType.Dropdown,
                DropdownOptions = options,
                DropdownLabels = labels ?? options,
                DefaultValue = defaultValue ?? options.FirstOrDefault(),
                AllowUserOverride = userOverridable,
                IsAdvanced = isAdvanced
            };
        }

        /// <summary>
        /// Creates a text box configuration option
        /// </summary>
        /// <param name="key">Unique identifier for the configuration</param>
        /// <param name="name">Display name shown in UI</param>
        /// <param name="description">Help text describing the option</param>
        /// <param name="defaultValue">Default text value</param>
        /// <param name="userOverridable">Whether this option can potentially be overridden by users (subject to admin permission)</param>
        /// <param name="isAdvanced">Whether this is an advanced option that should be hidden by default</param>
        /// <param name="placeholder">Placeholder text for the input</param>
        /// <param name="minLength">Minimum allowed length</param>
        /// <param name="maxLength">Maximum allowed length</param>
        /// <param name="pattern">Regex pattern for validation</param>
        /// <param name="validationMessage">Custom validation error message</param>
        /// <returns>Configuration option for a text box</returns>
        public static PluginConfigurationOption CreateTextBox(string key, string name, string description, string defaultValue = "", bool userOverridable = true, bool isAdvanced = false, string? placeholder = null, int? minLength = null, int? maxLength = null, string? pattern = null, string? validationMessage = null)
        {
            return new PluginConfigurationOption
            {
                Key = key,
                Name = name,
                Description = description,
                Type = PluginConfigurationType.TextBox,
                DefaultValue = defaultValue,
                AllowUserOverride = userOverridable,
                IsAdvanced = isAdvanced,
                Placeholder = placeholder,
                MinLength = minLength,
                MaxLength = maxLength,
                Pattern = pattern,
                ValidationMessage = validationMessage
            };
        }

        /// <summary>
        /// Creates a number box configuration option
        /// </summary>
        /// <param name="key">Unique identifier for the configuration</param>
        /// <param name="name">Display name shown in UI</param>
        /// <param name="description">Help text describing the option</param>
        /// <param name="defaultValue">Default numeric value</param>
        /// <param name="userOverridable">Whether this option can potentially be overridden by users (subject to admin permission)</param>
        /// <param name="isAdvanced">Whether this is an advanced option that should be hidden by default</param>
        /// <param name="placeholder">Placeholder text for the input</param>
        /// <param name="minValue">Minimum allowed value</param>
        /// <param name="maxValue">Maximum allowed value</param>
        /// <param name="step">Step increment for the input</param>
        /// <param name="validationMessage">Custom validation error message</param>
        /// <returns>Configuration option for a number box</returns>
        public static PluginConfigurationOption CreateNumberBox(string key, string name, string description, double defaultValue = 0, bool userOverridable = true, bool isAdvanced = false, string? placeholder = null, double? minValue = null, double? maxValue = null, double? step = null, string? validationMessage = null)
        {
            return new PluginConfigurationOption
            {
                Key = key,
                Name = name,
                Description = description,
                Type = PluginConfigurationType.NumberBox,
                DefaultValue = defaultValue,
                AllowUserOverride = userOverridable,
                IsAdvanced = isAdvanced,
                Placeholder = placeholder,
                MinValue = minValue,
                MaxValue = maxValue,
                Step = step,
                ValidationMessage = validationMessage
            };
        }
    }
}
