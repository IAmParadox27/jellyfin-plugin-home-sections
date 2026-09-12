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

            List<(Guid SeriesId, DateTime? LatestPremiereDate)> selectedSeries = new List<(Guid, DateTime?)>();
            int episodeIncrement = 200;
            int currentIndex = 0;
            bool continueSearching = true;
            
            do
            {
                // Single query: Get recent episodes, limited but enough to find 16 unique series
                // Fetch more episodes to account for multiple episodes per series
                var mainQuery = folders.Select(x =>
                {
                    var item = m_libraryManager.GetParentItem(Guid.Parse(x.ItemId), user?.Id);

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

                    return (FolderId: x.ItemId, Items: items.Items, items.Items.Count);
                }).ToArray();

                folders = folders.Where(x => (mainQuery.First(y => y.FolderId == x.ItemId).Count) >= episodeIncrement).ToArray();
                
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
                
                // Group by series and get the one with the latest premiere date per series
                var seriesWithLatestEpisode = recentEpisodes
                    .Select(ep => (Episode: ep, SeriesId: ep.SeriesId == Guid.Empty ? ep.Series?.Id ?? Guid.Empty : ep.SeriesId))
                    .Where(x => x.SeriesId != Guid.Empty)
                    .GroupBy(x => x.SeriesId)
                    .Select(g => (
                        SeriesId: g.Key,
                        LatestPremiereDate: g.Max(x => x.Episode.PremiereDate)
                    ))
                    .OrderByDescending(x => x.LatestPremiereDate)
                    .Take(16)
                    .ToList();
                
                var seriesToAdd = seriesWithLatestEpisode.Where(x => selectedSeries.All(y => y.SeriesId != x.SeriesId)).ToList();
                
                selectedSeries.AddRange(seriesToAdd);

                Guid[] selectedIds = selectedSeries
                    .Select(x => x.SeriesId)
                    .ToArray();

                List<Series> resolvedSeries = m_libraryManager
                    .GetItemList(new InternalItemsQuery(user)
                    {
                        ItemIds = selectedIds,
                        DtoOptions = dtoOptions,
                        GroupByPresentationUniqueKey = false,
                        EnableTotalRecordCount = false
                    })
                    .OfType<Series>()
                    .ToList();
                
                int logicalSeriesCount = resolvedSeries
                    .GroupBy(x => string.IsNullOrEmpty(x.PresentationUniqueKey)
                        ? x.Id.ToString()
                        : x.PresentationUniqueKey)
                    .Count();

                if (logicalSeriesCount >= 16)
                {
                    continueSearching = false;
                }
            } while (continueSearching);
            
            // Maintain the order from our sorted list
            Dictionary<Guid, DateTime?> latestDates = selectedSeries
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
                .Take(16)
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
