using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections
{
    public class PluginDefinedSection : IHomeScreenSection
    {
        public delegate QueryResult<BaseItemDto> GetResultsDelegate(HomeScreenSectionPayload payload);
        
        public string? Section { get; }
        public string? DisplayText { get; set; }
        public SectionInfo? Info { get; }
        public int? Limit { get; }
        public string? Route { get; }
        public string? AdditionalData { get; set; }
        public bool? EnableByDefault { get; }

        public object? OriginalPayload => null;
        
        public required GetResultsDelegate OnGetResults { get; set; }
        
        private readonly List<PluginConfigurationOption>? _configurationOptions;
        private readonly string? _jsonConfigurationOptionsString;
        
        public PluginDefinedSection(string sectionUuid, string displayText, SectionInfo? info = null, string? route = null, string? additionalData = null, List<PluginConfigurationOption>? configurationOptions = null, bool? enableByDefault = null)
        {
            Section = sectionUuid;
            DisplayText = displayText;
            Info = info;
            Limit = 1;
            Route = route;
            AdditionalData = additionalData;
            EnableByDefault = enableByDefault;
            _configurationOptions = configurationOptions;
            _jsonConfigurationOptionsString = null;
        }
        
        public PluginDefinedSection(string sectionUuid, string displayText, SectionInfo? info = null, string? route = null, string? additionalData = null, string? jsonConfigurationOptions = null, bool? enableByDefault = null)
        {
            Section = sectionUuid;
            DisplayText = displayText;
            Info = info;
            Limit = 1;
            Route = route;
            AdditionalData = additionalData;
            EnableByDefault = enableByDefault;
            _configurationOptions = null;
            _jsonConfigurationOptionsString = jsonConfigurationOptions;
        }
        
        public QueryResult<BaseItemDto> GetResults(HomeScreenSectionPayload payload, IQueryCollection queryCollection)
        {
            return OnGetResults(payload);
        }

        public IHomeScreenSection CreateInstance(Guid? userId, IEnumerable<IHomeScreenSection>? otherInstances = null)
        {
            return this;
        }
        
        public HomeScreenSectionInfo GetInfo()
        {
            return new HomeScreenSectionInfo
            {
                Section = Section,
                DisplayText = DisplayText,
                Info = Info,
                AdditionalData = AdditionalData,
                Route = Route,
                Limit = Limit ?? 1,
                OriginalPayload = OriginalPayload,
                ViewMode = SectionViewMode.Landscape,
                EnableByDefault = EnableByDefault
            };
        }

        /// <summary>
        /// Get configuration options for this section
        /// </summary>
        /// <returns>Collection of configuration options</returns>
        public virtual IEnumerable<PluginConfigurationOption>? GetConfigurationOptions()
        {
            // If we have typed configuration options, return them directly
            if (_configurationOptions != null)
            {
                return _configurationOptions;
            }
            
            // If we have JSON configuration options string, parse and convert them to typed options
            if (!string.IsNullOrEmpty(_jsonConfigurationOptionsString))
            {
                try
                {
                    var jsonArray = JArray.Parse(_jsonConfigurationOptionsString);
                    return jsonArray.Select(token => 
                    {
                        // Let ToObject handle most of the conversion automatically
                        var option = token.ToObject<PluginConfigurationOption>() ?? new PluginConfigurationOption();
                        
                        // Handle the nested properties that ToObject can't handle automatically
                        if (token["options"] is JArray optionsArray && token["type"]?.ToString().ToLower() == "dropdown")
                        {
                            var opts = optionsArray.ToObject<List<dynamic>>();
                            option.DropdownOptions = opts?.Select(o => (string)o.key).ToArray() ?? Array.Empty<string>();
                            option.DropdownLabels = opts?.Select(o => (string)o.value).ToArray() ?? Array.Empty<string>();
                        }
                        
                        if (token["validation"] is JObject validation)
                        {
                            option.MinValue = validation["min"]?.ToObject<double?>();
                            option.MaxValue = validation["max"]?.ToObject<double?>();
                            option.MinLength = validation["minLength"]?.ToObject<int?>();
                            option.MaxLength = validation["maxLength"]?.ToObject<int?>();
                            option.Step = validation["step"]?.ToObject<double?>();
                            option.Pattern = validation["pattern"]?.ToString();
                            option.ValidationMessage = validation["message"]?.ToString() ?? validation["validationMessage"]?.ToString();
                        }
                        
                        return option;
                    });
                }
                catch (Exception)
                {
                    // If JSON parsing fails, return empty collection
                    return Enumerable.Empty<PluginConfigurationOption>();
                }
            }
            
            // Return empty collection if no configuration options are provided
            return Enumerable.Empty<PluginConfigurationOption>();
        }
    }
}