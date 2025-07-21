using Jellyfin.Plugin.HomeScreenSections.Configuration;
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
        
        public bool SupportsShowPlayedItems { get; set; } = false;
        
        public bool SupportsPreventDuplicates { get; set; } = false;
        
        public required GetResultsDelegate OnGetResults { get; set; }
        
        public PluginDefinedSection(string sectionUuid, string displayText, string? route = null, string? additionalData = null, bool supportsShowPlayedItems = false, bool supportsPreventDuplicates = false)
        {
            Section = sectionUuid;
            DisplayText = displayText;
            Limit = 1;
            Route = route;
            AdditionalData = additionalData;
            SupportsShowPlayedItems = supportsShowPlayedItems;
            SupportsPreventDuplicates = supportsPreventDuplicates;
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
                ViewMode = SectionViewMode.Landscape,
                SupportsShowPlayedItems = SupportsShowPlayedItems,
                SupportsPreventDuplicates = SupportsPreventDuplicates
            };
        }
    }
}