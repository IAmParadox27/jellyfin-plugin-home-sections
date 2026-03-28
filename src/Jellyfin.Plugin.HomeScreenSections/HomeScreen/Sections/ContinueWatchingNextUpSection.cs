using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
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
        private readonly ILibraryManager m_libraryManager;
        private readonly IUserManager m_userManager;
        private readonly IUserDataManager m_userDataManager;
        
        public ContinueWatchingNextUpSection(
            IHomeScreenManager homeScreenManager,
            ILibraryManager libraryManager,
            IUserManager userManager,
            IUserDataManager userDataManager)
        {
            m_continueWatchingSection = homeScreenManager.GetSection("ContinueWatching") as ContinueWatchingSection;
            m_nextUpSection = homeScreenManager.GetSection("NextUp") as NextUpSection;
            m_libraryManager = libraryManager;
            m_userManager = userManager;
            m_userDataManager = userDataManager;
        }
        
        public QueryResult<BaseItemDto> GetResults(HomeScreenSectionPayload payload, IQueryCollection queryCollection)
        {
            IReadOnlyList<BaseItemDto>? cwResults = m_continueWatchingSection?.GetResults(payload, queryCollection).Items;
            
            // Apply default Next Up settings, halves performance impact
            // Unfortunately we can't get the user's actual Next Up settings, as they're stored in local storage on the client
            var nuQuery = new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
                ["UserId"] = queryCollection["UserId"],
                ["EnableRewatching"] = "false",
                ["NextUpDateCutoff"] = DateTime.UtcNow.AddDays(-365).ToString("O")
            };
            
            IReadOnlyList<BaseItemDto>? nuResults = m_nextUpSection?.GetResults(payload, new QueryCollection(nuQuery)).Items;
            
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

            Dictionary<Guid, DateTime> seriesLastPlayed = BuildSeriesLastPlayedLookup(returnItems, payload.UserId);
            List<BaseItemDto> sortedItems = returnItems
                .OrderByDescending(x => GetSortDate(x, seriesLastPlayed))
                .ToList();

            return new QueryResult<BaseItemDto>(sortedItems);
        }

        private Dictionary<Guid, DateTime> BuildSeriesLastPlayedLookup(List<BaseItemDto> items, Guid userId)
        {
            Dictionary<Guid, DateTime> lookup = new Dictionary<Guid, DateTime>();
            User? user = m_userManager.GetUserById(userId);
            if (user == null) return lookup;

            // Collect all unique series IDs that need lookup
            List<Guid> seriesIds = items
                .Where(x => x.Type == BaseItemKind.Episode && 
                           x.SeriesId != null && 
                           x.UserData?.LastPlayedDate == null)
                .Select(x => x.SeriesId!.Value)
                .Distinct()
                .ToList();

            if (seriesIds.Count == 0) return lookup;

            // Query recent played episodes for each series
            // Limit 2: most recent + fallback in case first lacks LastPlayedDate
            foreach (Guid seriesId in seriesIds)
            {
                IReadOnlyList<BaseItem> recentEpisodes = m_libraryManager.GetItemList(new InternalItemsQuery(user)
                {
                    AncestorIds = new[] { seriesId },
                    IncludeItemTypes = new[] { BaseItemKind.Episode },
                    IsPlayed = true,
                    OrderBy = new[] { (ItemSortBy.DatePlayed, SortOrder.Descending) },
                    Limit = 2
                });

                // Find the first episode with a valid LastPlayedDate
                foreach (BaseItem episode in recentEpisodes)
                {
                    DateTime? lastPlayedDate = m_userDataManager.GetUserData(user, episode)?.LastPlayedDate;
                    if (lastPlayedDate != null)
                    {
                        lookup[seriesId] = lastPlayedDate.Value;
                        break;  // Found it, move to next series
                    }
                }
            }
            return lookup;
        }

        // Uses native LastPlayedDate if available,
        // otherwise checks series LastPlayedDate,
        // then falls back to DateCreated (but I don't think this should happen)
        private DateTime GetSortDate(BaseItemDto item, Dictionary<Guid, DateTime> seriesLastPlayed)
        {
            if (item.UserData?.LastPlayedDate != null)
                return item.UserData.LastPlayedDate.Value;

            if (item.SeriesId != null && seriesLastPlayed.TryGetValue(item.SeriesId.Value, out DateTime seriesDate))
                return seriesDate;

            return item.DateCreated ?? DateTime.MinValue;
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