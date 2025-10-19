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
        
        private readonly List<PluginConfigurationOption>? m_configurationOptions;
        
        public PluginDefinedSection(string sectionUuid, string displayText, SectionInfo? info = null, string? route = null, string? additionalData = null, List<PluginConfigurationOption>? configurationOptions = null, bool? enableByDefault = null)
        {
            Section = sectionUuid;
            DisplayText = displayText;
            Info = info;
            Limit = 1;
            Route = route;
            AdditionalData = additionalData;
            EnableByDefault = enableByDefault;
            m_configurationOptions = configurationOptions;
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
            m_configurationOptions = ParseJsonConfigurationOptions(jsonConfigurationOptions);
        }
        
        private static List<PluginConfigurationOption>? ParseJsonConfigurationOptions(string? jsonConfigurationOptions)
        {
            if (string.IsNullOrEmpty(jsonConfigurationOptions))
                return null;
                
            try
            {
                var jsonArray = JArray.Parse(jsonConfigurationOptions);
                return jsonArray.Select(token => 
                {
                    var option = token.ToObject<PluginConfigurationOption>() ?? new PluginConfigurationOption();
                    
                    // Handle dropdown options
                    if (token["options"] is JArray optionsArray && 
                        string.Equals(token["type"]?.ToString(), "dropdown", StringComparison.OrdinalIgnoreCase))
                    {
                        var opts = optionsArray.ToObject<List<dynamic>>();
                        option.DropdownOptions = opts?.Select(o => (string)o.key).ToArray() ?? Array.Empty<string>();
                        option.DropdownLabels = opts?.Select(o => (string)o.value).ToArray() ?? Array.Empty<string>();
                    }
                    
                    // Handle validation object
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
                    
                    if (token["advanced"] != null)
                    {
                        option.IsAdvanced = token["advanced"]?.ToObject<bool>() ?? false;
                    }
                    
                    return option;
                }).ToList();
            }
            catch (Exception)
            {
                return null;
            }
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
            return m_configurationOptions ?? Enumerable.Empty<PluginConfigurationOption>();
        }
    }
}