using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Helpers;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections
{
    public class PluginDefinedSection : IHomeScreenSection
    {
        public delegate QueryResult<BaseItemDto> GetResultsDelegate(HomeScreenSectionPayload payload);
        
        public string? Section { get; }
        public string? DisplayText { get; set; }
        public int? Limit { get; }
        public string? Route { get; }
        public string? AdditionalData { get; set; }

        public object? OriginalPayload => null;
        
        public required GetResultsDelegate OnGetResults { get; set; }
        
        private readonly List<PluginConfigurationOption>? _configurationOptions;
        
        public PluginDefinedSection(string sectionUuid, string displayText, string? route = null, string? additionalData = null, List<PluginConfigurationOption>? configurationOptions = null)
        {
            Section = sectionUuid;
            DisplayText = displayText;
            Limit = 1;
            Route = route;
            AdditionalData = additionalData;
            _configurationOptions = configurationOptions;
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
                AdditionalData = AdditionalData,
                Route = Route,
                Limit = Limit ?? 1,
                OriginalPayload = OriginalPayload,
                ViewMode = SectionViewMode.Landscape
            };
        }

        /// <summary>
        /// Get configuration options for this section
        /// </summary>
        /// <returns>Collection of configuration options</returns>
        public virtual IEnumerable<PluginConfigurationOption> GetConfigurationOptions()
        {
            // Return custom configuration options if provided, otherwise return empty collection
            return _configurationOptions ?? Enumerable.Empty<PluginConfigurationOption>();
        }
    }
}