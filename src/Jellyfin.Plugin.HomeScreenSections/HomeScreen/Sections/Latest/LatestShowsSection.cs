using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Helpers;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.TV;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections.Latest
{
    public class LatestShowsSection : LatestSectionBase
    {
        public override string? Section => "LatestShows";
        
        public override string? Route => "tvshows";

        public override string? DisplayText { get; set; } = "Latest Shows";

        private readonly ITVSeriesManager m_tvSeriesManager;
        
        public LatestShowsSection(IUserViewManager userViewManager,
            IUserManager userManager,
            ILibraryManager libraryManager,
            ITVSeriesManager tvSeriesManager,
            IDtoService dtoService,
            IServiceProvider serviceProvider) : base(userViewManager, userManager, libraryManager, dtoService, serviceProvider)
        {
            m_tvSeriesManager = tvSeriesManager;
        }

        public override SectionViewMode DefaultViewMode => SectionViewMode.Landscape;
        protected override BaseItemKind SectionItemKind => BaseItemKind.Episode;
        protected override CollectionType CollectionType => CollectionType.tvshows;
        protected override string? LibraryId => HomeScreenSectionsPlugin.Instance?.Configuration?.DefaultTVShowsLibraryId;
        protected override CollectionTypeOptions CollectionTypeOptions => CollectionTypeOptions.tvshows;

        public override QueryResult<BaseItemDto> GetResults(HomeScreenSectionPayload payload, IQueryCollection queryCollection)
        {
            DtoOptions? dtoOptions = new DtoOptions
            {
                Fields = new List<ItemFields>
                {
                    ItemFields.PrimaryImageAspectRatio,
                    ItemFields.Path
                }
            };

            dtoOptions.ImageTypeLimit = 1;
            dtoOptions.ImageTypes = new List<ImageType>
            {
                ImageType.Thumb,
                ImageType.Backdrop,
                ImageType.Primary,
            };

            User? user = m_userManager.GetUserById(payload.UserId);

            var config = HomeScreenSectionsPlugin.Instance?.Configuration;
            var sectionSettings = config?.SectionSettings.FirstOrDefault(x => x.SectionId == Section);
            // If HideWatchedItems is enabled for this section, set isPlayed to false to hide watched items; otherwise, include all.
            bool? isPlayed = sectionSettings?.HideWatchedItems == true ? false : null;

            VirtualFolderInfo[] folders = m_libraryManager.GetVirtualFolders()
                .Where(x => x.CollectionType == CollectionTypeOptions)
                .FilterToUserPermitted(m_libraryManager, user);

            Dictionary<Guid, Series> hydratedShowCache = new Dictionary<Guid, Series>();
            
            List<(Guid SeriesId, DateTime LatestPremiereDate, string UniqueKey)> selectedSeries = new List<(Guid, DateTime, string)>();
            int episodeIncrement = 200;
            int currentIndex = 0;
            int maxResults = 16; // Adding this as a var here so we can make it configurable easier at a later date.
            bool continueSearching = true;
            
            List<VirtualFolderInfo> foldersToQuery = folders.ToList();
            do
            {
                // Single query: Get recent episodes, limited but enough to find 16 unique series
                // Fetch more episodes to account for multiple episodes per series
                List<(string FolderId, IEnumerable<BaseItem> Items, int Count)> mainQuery = new List<(string, IEnumerable<BaseItem>, int)>();
                List<VirtualFolderInfo> foldersToQueryCopy = foldersToQuery.ToList();
                foreach (VirtualFolderInfo virtualFolder in foldersToQueryCopy)
                {
                    var item = m_libraryManager.GetParentItem(Guid.Parse(virtualFolder.ItemId), user?.Id);

                    if (item is not Folder folder)
                    {
                        folder = m_libraryManager.GetUserRootFolder();
                    }

                    var items = folder.GetItems(new InternalItemsQuery(user)
                    {
                        IncludeItemTypes = new[] { SectionItemKind },
                        OrderBy = new[] { (ItemSortBy.PremiereDate, SortOrder.Descending) },
                        StartIndex = currentIndex,
                        Limit = episodeIncrement,
                        IsVirtualItem = false,
                        IsPlayed = isPlayed,
                        Recursive = true,
                        ParentId = folder.Id,
                        EnableTotalRecordCount = false
                        // DtoOptions = new DtoOptions { Fields = Array.Empty<ItemFields>(), EnableImages = false }
                    });

                    bool includeResults = true;
                    if (selectedSeries.Select(x => x.UniqueKey).Distinct().Count() >= maxResults) // Intentional double buffer to allow for duplicate presentation unique keys to be filtered down to a count greater than maxResults
                    {
                        // When the query for this folder has started returning episodes older than the oldest we've got saved, we can stop searching it.
                        DateTime latestPremiereInQuery = items.Items
                            .OfType<Episode>()
                            .Where(x => !x.IsUnaired)
                            .Select(x => x.PremiereDate)
                            .DefaultIfEmpty(DateTime.MinValue)
                            .Max() ?? DateTime.MinValue;
                        
                        if (latestPremiereInQuery < selectedSeries.Min(x => x.LatestPremiereDate))
                        {
                            foldersToQuery.Remove(virtualFolder); // Avoid needing to query this folder again.
                            continue;
                        }
                    }

                    mainQuery.Add((FolderId: virtualFolder.ItemId, Items: items.Items, Count: items.Items.Count));

                    // We don't need to continue this folder as it's exhausted.
                    if (items.Items.Count() < episodeIncrement)
                    {
                        foldersToQuery.Remove(virtualFolder); // Avoid needing to query this folder again.
                    }
                }

                var rawEpisodeResults = mainQuery.SelectMany(x => x.Items).OfType<Episode>()
                    .ToList();

                if (rawEpisodeResults.Count == 0)
                {
                    break;
                }
                
                currentIndex += episodeIncrement;

                var recentEpisodes = rawEpisodeResults
                .Where(x => !x.IsUnaired)
                .ToList();
                
                m_libraryManager.GetItemList(new InternalItemsQuery(user)
                {
                    ItemIds = recentEpisodes.Select(ep => ep.SeriesId == Guid.Empty
                        ? ep.Series?.Id ?? Guid.Empty
                        : ep.SeriesId).Distinct().Where(x => !hydratedShowCache.Keys.Contains(x)).ToArray(),
                    GroupByPresentationUniqueKey = false,
                    EnableTotalRecordCount = false
                }).OfType<Series>().ToList().ForEach(x => hydratedShowCache[x.Id] = x);
                
                // Group by series and get the one with the latest premiere date per series
                var seriesWithLatestEpisode = recentEpisodes
                    .Select(ep => (Episode: ep, SeriesId: ep.SeriesId == Guid.Empty ? ep.Series?.Id ?? Guid.Empty : ep.SeriesId))
                    .Where(x => x.SeriesId != Guid.Empty)
                    .GroupBy(x => x.SeriesId)
                    .Where(g => hydratedShowCache.TryGetValue(g.Key, out _))
                    .Select(g =>
                    {
                        Series series = hydratedShowCache[g.Key];
                        
                        return (
                            SeriesId: g.Key,
                            LatestPremiereDate: g.Max(x => x.Episode.PremiereDate ?? DateTime.MinValue),
                            UniqueKey: series.GetPresentationUniqueKey() ?? g.Key.ToString()
                        );
                    })
                    .OrderByDescending(x => x.LatestPremiereDate)
                    .ToList();
                
                var seriesToAdd = seriesWithLatestEpisode.Where(x => selectedSeries.All(y => y.SeriesId != x.SeriesId)).ToList();
                
                // We've decided that at least one of the shows we found fits in the list, so lets add them and trim down to the best 16
                selectedSeries.AddRange(seriesToAdd);

                selectedSeries = selectedSeries
                    .GroupBy(x => x.UniqueKey)
                    .Select(g => g
                        .OrderByDescending(x => x.LatestPremiereDate)
                        .First())
                    .OrderByDescending(x => x.LatestPremiereDate)
                    .Take(maxResults)
                    .ToList();
                
                if (foldersToQuery.Count == 0)
                {
                    continueSearching = false;
                }
            } while (continueSearching);
            
            // Maintain the order from our sorted list
            Dictionary<Guid, DateTime> latestDates = selectedSeries
                .ToDictionary(x => x.SeriesId, x => x.LatestPremiereDate);

            // Fetch the full series objects with proper DtoOptions for images
            List<Series> hydratedSeries = m_libraryManager
                .GetItemList(new InternalItemsQuery(user)
                {
                    ItemIds = latestDates.Keys.ToArray(),
                    DtoOptions = dtoOptions,
                    GroupByPresentationUniqueKey = false,
                    EnableTotalRecordCount = false
                })
                .OfType<Series>()
                .ToList();

            Series[] orderedSeries = hydratedSeries
                .GroupBy(x => string.IsNullOrEmpty(x.PresentationUniqueKey)
                    ? x.Id.ToString()
                    : x.PresentationUniqueKey)
                .OrderByDescending(g => g.Max(x => latestDates[x.Id]))
                .Select(g => g
                    .OrderByDescending(x => latestDates[x.Id])
                    .First())
                .Take(maxResults)
                .ToArray();
            
            return new QueryResult<BaseItemDto>(Array.ConvertAll(orderedSeries.ToArray(),
                i => m_dtoService.GetBaseItemDto(i!, dtoOptions, user)));
        }

        protected override LatestSectionBase CreateInstance()
        {
            return new LatestShowsSection(m_userViewManager, m_userManager, m_libraryManager, m_tvSeriesManager, m_dtoService, m_serviceProvider);
        }
    }
}
