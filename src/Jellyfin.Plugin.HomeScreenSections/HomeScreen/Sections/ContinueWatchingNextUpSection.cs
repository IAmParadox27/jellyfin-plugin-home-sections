using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections
{
    public class ContinueWatchingNextUpSection : IHomeScreenSection
    {
        public string? Section => "ContinueWatchingNextUp";
        public string? DisplayText { get; set; } = "Continue Watching / Next Up";
        public int? Limit => 1;
        public string? Route => null;
        public string? AdditionalData { get; set; } = null;
        public object? OriginalPayload => null;

        private ContinueWatchingSection? m_continueWatchingSection;
        private NextUpSection? m_nextUpSection;
        
        public ContinueWatchingNextUpSection(IHomeScreenManager homeScreenManager)
        {
            m_continueWatchingSection = homeScreenManager.GetSection("ContinueWatching") as ContinueWatchingSection;
            m_nextUpSection = homeScreenManager.GetSection("NextUp") as NextUpSection;
        }
        
        public QueryResult<BaseItemDto> GetResults(HomeScreenSectionPayload payload, IQueryCollection queryCollection)
        {
            IReadOnlyList<BaseItemDto>? cwResults = m_continueWatchingSection?.GetResults(payload, queryCollection).Items;
            IReadOnlyList<BaseItemDto>? nuResults = m_nextUpSection?.GetResults(payload, queryCollection).Items;
            
            List<BaseItemDto> returnItems = new List<BaseItemDto>();

            if (cwResults != null)
            {
                returnItems.AddRange(cwResults);
            }

            if (nuResults != null)
            {
                returnItems.AddRange(nuResults.Where(x => returnItems.All(y => y.Id != x.Id)));
            }

            // If HideWatchedItems is enabled for this section, filter out watched items
            var config = HomeScreenSectionsPlugin.Instance?.Configuration;
            var sectionSettings = config?.SectionSettings.FirstOrDefault(x => x.SectionId == Section);
            if (sectionSettings?.HideWatchedItems == true)
            {
                returnItems = returnItems.Where(x => x.UserData?.Played != true).ToList();
            }

            // For now just returning this data as Continue Watching then Next Up, but we may want to sort this differently in the future
            return new QueryResult<BaseItemDto>(returnItems);
        }

        public IEnumerable<IHomeScreenSection> CreateInstances(Guid? userId, int instanceCount)
        {
            yield return this;
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
                AllowHideWatched = true
            };
        }
    }
}