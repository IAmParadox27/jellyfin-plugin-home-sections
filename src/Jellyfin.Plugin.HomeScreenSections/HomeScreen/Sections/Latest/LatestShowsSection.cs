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

            Dictionary<Guid, (Series Series, DateTime? LatestPremiereDate)> selectedSeries = new();
            const int pageSize = 200;
            DateTime currentDate = DateTime.Now;

            foreach (VirtualFolderInfo library in folders)
            {
                var item = m_libraryManager.GetParentItem(Guid.Parse(library.ItemId), user?.Id);

                if (item is not Folder folder)
                {
                    folder = m_libraryManager.GetUserRootFolder();
                }

                int startIndex = 0;

                while (true)
                {
                    var episodes = folder.GetItems(new InternalItemsQuery(user)
                    {
                        IncludeItemTypes = new[] { SectionItemKind },
                        OrderBy = new[] { (ItemSortBy.PremiereDate, SortOrder.Descending) },
                        Limit = pageSize,
                        StartIndex = startIndex,
                        IsVirtualItem = false,
                        IsPlayed = isPlayed,
                        Recursive = true,
                        ParentId = folder.Id,
                        MaxPremiereDate = currentDate,
                        MinPremiereDate = new DateTime(1925, 1, 1),
                        EnableTotalRecordCount = false
                    }).Items;

                    foreach (Episode episode in episodes.OfType<Episode>().Where(x => !x.IsUnaired))
                    {
                        Series? series = episode.Series;
                        if (series != null && (!selectedSeries.TryGetValue(series.Id, out var selected)
                            || episode.PremiereDate > selected.LatestPremiereDate))
                        {
                            selectedSeries[series.Id] = (series, episode.PremiereDate);
                        }
                    }

                    if (episodes.Count < pageSize)
                    {
                        break;
                    }

                    // Later pages cannot improve the current top 16 once they are older
                    // than its last entry. Keep paging if one long series fills a page.
                    if (selectedSeries.Count >= 16
                        && episodes.Min(x => x.PremiereDate) <= selectedSeries.Values
                            .OrderByDescending(x => x.LatestPremiereDate)
                            .ElementAt(15).LatestPremiereDate)
                    {
                        break;
                    }

                    startIndex += episodes.Count;
                }
            }
            
            // Fetch the full series objects with proper DtoOptions for images
            var seriesIds = selectedSeries.Values.OrderByDescending(x => x.LatestPremiereDate).Take(16).Select(x => x.Series.Id);
            var seriesIdArray = seriesIds.ToArray();
            if (seriesIdArray.Length == 0)
            {
                return new QueryResult<BaseItemDto>(Array.Empty<BaseItemDto>());
            }

            var seriesItems = m_libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                ItemIds = seriesIdArray,
                DtoOptions = dtoOptions
            });
            
            // Maintain the order from our sorted list
            var orderedSeries = seriesIdArray
                .Select(id => seriesItems.FirstOrDefault(s => s.Id == id))
                .OfType<BaseItem>()
                .ToArray();
            
            return new QueryResult<BaseItemDto>(m_dtoService.GetBaseItemDtos(orderedSeries, dtoOptions, user));
        }

        protected override LatestSectionBase CreateInstance()
        {
            return new LatestShowsSection(m_userViewManager, m_userManager, m_libraryManager, m_tvSeriesManager, m_dtoService, m_serviceProvider);
        }
    }
}
