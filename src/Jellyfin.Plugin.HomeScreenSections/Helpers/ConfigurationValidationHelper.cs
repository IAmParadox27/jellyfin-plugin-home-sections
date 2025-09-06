using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Library;

namespace Jellyfin.Plugin.HomeScreenSections.Helpers
{
    /// <summary>
    /// Helper that provides validation for plugin configuration options.
    /// </summary>
    public class ConfigurationValidationHelper
    {
        /// <summary>
        /// Validates a configuration option value against its defined constraints.
        /// </summary>
        /// <param name="option">The configuration option to validate against.</param>
        /// <param name="value">The value to validate.</param>
        /// <returns>ValidationResult indicating success or failure with error message.</returns>
        public ValidationResult ValidateOption(PluginConfigurationOption option, object? value)
        {
            var context = new ValidationContext(new { }) { MemberName = option.Key };
            
            return option.Type switch
            {
                PluginConfigurationType.TextBox => ValidateTextBox(option, value?.ToString(), context),
                PluginConfigurationType.NumberBox => ValidateNumberBox(option, value, context),
                PluginConfigurationType.Dropdown => ValidateDropdown(option, value?.ToString(), context),
                PluginConfigurationType.Checkbox => ValidationResult.Success!, // Checkboxes are always valid
                _ => ValidationResult.Success!
            };
        }

        /// <summary>
        /// Validates a text box input according to defined text constraints.
        /// </summary>
        private ValidationResult ValidateTextBox(PluginConfigurationOption option, string? value, ValidationContext context)
        {
            // Required validation (matches HTML5 required attribute)
            if (option.Required && string.IsNullOrEmpty(value))
                return new ValidationResult($"{option.Name} is required", new[] { context.MemberName! });

            if (string.IsNullOrEmpty(value))
                return ValidationResult.Success!;

            // MinLength validation (matches HTML5 minlength attribute)
            if (option.MinLength.HasValue && value.Length < option.MinLength.Value)
                return new ValidationResult($"{option.Name} must be at least {option.MinLength} characters", new[] { context.MemberName! });

            // MaxLength validation (matches HTML5 maxlength attribute)
            if (option.MaxLength.HasValue && value.Length > option.MaxLength.Value)
                return new ValidationResult($"{option.Name} cannot exceed {option.MaxLength} characters", new[] { context.MemberName! });

            // Pattern validation (matches HTML5 pattern attribute)
            if (!string.IsNullOrEmpty(option.Pattern))
            {
                try
                {
                    if (!Regex.IsMatch(value, option.Pattern))
                        return new ValidationResult(option.ValidationMessage ?? $"{option.Name} format is invalid", new[] { context.MemberName! });
                }
                catch (ArgumentException)
                {
                    return new ValidationResult("Invalid pattern configuration", new[] { context.MemberName! });
                }
            }

            return ValidationResult.Success!;
        }

        /// <summary>
        /// Validates a number box input according to defined numeric constraints.
        /// </summary>
        private ValidationResult ValidateNumberBox(PluginConfigurationOption option, object? value, ValidationContext context)
        {
            // Required validation (matches HTML5 required attribute)
            if (option.Required && (value == null || string.IsNullOrEmpty(value.ToString())))
                return new ValidationResult($"{option.Name} is required", new[] { context.MemberName! });

            if (value == null || string.IsNullOrEmpty(value.ToString()))
                return ValidationResult.Success!;

            // Parse number
            if (!double.TryParse(value.ToString(), out double numValue))
                return new ValidationResult($"{option.Name} must be a valid number", new[] { context.MemberName! });

            // Min validation (matches HTML5 min attribute)
            if (option.MinValue.HasValue && numValue < option.MinValue.Value)
                return new ValidationResult($"{option.Name} must be at least {option.MinValue}", new[] { context.MemberName! });

            // Max validation (matches HTML5 max attribute)
            if (option.MaxValue.HasValue && numValue > option.MaxValue.Value)
                return new ValidationResult($"{option.Name} cannot exceed {option.MaxValue}", new[] { context.MemberName! });

            // Step validation (matches HTML5 step attribute)
            if (option.Step.HasValue && option.MinValue.HasValue)
            {
                double steps = (numValue - option.MinValue.Value) / option.Step.Value;
                if (Math.Abs(steps - Math.Round(steps)) > 0.0001) // Allow for floating point precision
                {
                    return new ValidationResult($"{option.Name} must be in increments of {option.Step}", new[] { context.MemberName! });
                }
            }

            return ValidationResult.Success!;
        }

        /// <summary>
        /// Validates a dropdown selection according to defined selection constraints.
        /// </summary>
        private ValidationResult ValidateDropdown(PluginConfigurationOption option, string? value, ValidationContext context)
        {
            // Required validation
            if (option.Required && string.IsNullOrEmpty(value))
                return new ValidationResult($"{option.Name} is required", new[] { context.MemberName! });

            if (string.IsNullOrEmpty(value))
                return ValidationResult.Success!;

            // Validate against allowed options
            if (option.DropdownOptions?.Contains(value) != true)
                return new ValidationResult($"{option.Name} has an invalid selection", new[] { context.MemberName! });

            return ValidationResult.Success!;
        }

        /// <summary>
        /// Validates admin configuration settings and returns structured error list.
        /// </summary>
        /// <param name="configuration">The admin configuration to validate.</param>
        /// <param name="homeScreenManager">The home screen manager to get section options.</param>
        /// <returns>List of validation errors with section, field, and message details.</returns>
        public List<object> ValidateAdminSettings(PluginConfiguration configuration, IHomeScreenManager homeScreenManager)
        {
            var errors = new List<object>();

            if (configuration.SectionSettings == null) return errors;

            foreach (var sectionSetting in configuration.SectionSettings)
            {
                if (sectionSetting.PluginConfigurations == null) continue;

                var section = homeScreenManager.GetSectionTypes()
                    .FirstOrDefault(s => s.Section?.Equals(sectionSetting.SectionId, StringComparison.OrdinalIgnoreCase) == true);
                
                var options = section?.GetConfigurationOptions()?.ToArray();
                if (options == null) continue;

                foreach (var pluginConfig in sectionSetting.PluginConfigurations)
                {
                    var option = options.FirstOrDefault(o => string.Equals(o.Key, pluginConfig.Key, StringComparison.OrdinalIgnoreCase));
                    if (option != null)
                    {
                        var validationResult = ValidateOption(option, pluginConfig.Value);
                        if (validationResult != ValidationResult.Success)
                        {
                            errors.Add(new 
                            { 
                                section = sectionSetting.SectionId, 
                                field = pluginConfig.Key, 
                                message = validationResult.ErrorMessage ?? "Invalid value" 
                            });
                        }
                    }
                }
            }

            return errors;
        }

        /// <summary>
        /// Validates user settings and returns structured error list.
        /// Only validates user-allowed overrides based on permission context.
        /// </summary>
        /// <param name="userSettings">The user settings to validate.</param>
        /// <param name="permissionContexts">Dictionary of section permission contexts.</param>
        /// <returns>List of validation errors with section, field, and message details.</returns>
        public List<object> ValidateUserSettings(ModularHomeUserSettings userSettings, Dictionary<string, object> permissionContexts)
        {
            var errors = new List<object>();

            if (userSettings.SectionSettings == null) return errors;

            foreach (var sectionSetting in userSettings.SectionSettings)
            {
                if (!permissionContexts.TryGetValue(sectionSetting.SectionId, out var contextObj)) continue;
                
                // Use dynamic to access the permission context properties
                dynamic ctx = contextObj;

                if (sectionSetting.PluginConfigurations == null) continue;

                foreach (var config in sectionSetting.PluginConfigurations)
                {
                    try
                    {
                        // Only validate user-allowed overrides
                        var overrideMap = (Dictionary<string, bool>)ctx.OverrideMap;
                        if (!overrideMap.GetValueOrDefault(config.Key, false)) continue;

                        var optionDefs = (Dictionary<string, PluginConfigurationOption>)ctx.OptionDefs;
                        if (optionDefs.TryGetValue(config.Key, out var optionDef))
                        {
                            var validationResult = ValidateOption(optionDef, config.Value);
                            if (validationResult != ValidationResult.Success)
                            {
                                errors.Add(new 
                                { 
                                    section = sectionSetting.SectionId, 
                                    field = config.Key, 
                                    message = validationResult.ErrorMessage ?? "Invalid value" 
                                });
                            }
                        }
                    }
                    catch
                    {
                        // Skip validation if we can't access the permission context
                        continue;
                    }
                }
            }

            return errors;
        }
    }
}