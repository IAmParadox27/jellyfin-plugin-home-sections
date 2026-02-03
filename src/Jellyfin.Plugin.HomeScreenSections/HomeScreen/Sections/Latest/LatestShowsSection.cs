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

            List<(Series Series, DateTime? LatestPremiereDate)> selectedSeries = new List<(Series, DateTime?)>();
            int dayIncrement = 30;
            DateTime currentDate = DateTime.Now;
            DateTime stopDate = DateTime.Parse("01/01/1925"); // The first show ever was 1925 so this should be safe, we never expect to get as far back as this but we need an escape.
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
                        Limit = 200, // Enough to find 16 unique series even with multi-episode releases
                        IsVirtualItem = false,
                        IsPlayed = isPlayed,
                        Recursive = true,
                        ParentId = folder.Id,
                        MaxPremiereDate = currentDate,
                        MinPremiereDate = currentDate.Subtract(TimeSpan.FromDays(dayIncrement)),
                        EnableTotalRecordCount = true // This might have to go
                        // DtoOptions = new DtoOptions { Fields = Array.Empty<ItemFields>(), EnableImages = false }
                    });

                    return (Items: items.Items, items.Items.Count, items.TotalRecordCount);
                }).ToArray();

                var recentEpisodes = mainQuery.SelectMany(x => x.Items).OfType<Episode>()
                .Where(x => !x.IsUnaired)
                .ToList();
                
                // Group by series and get the one with the latest premiere date per series
                var seriesWithLatestEpisode = recentEpisodes
                    .Select(ep => (Episode: ep, Series: ep.Series))
                    .Where(x => x.Series != null)
                    .GroupBy(x => x.Series!.Id)
                    .Select(g => (
                        Series: g.First().Series!,
                        LatestPremiereDate: g.Max(x => x.Episode.PremiereDate)
                    ))
                    .OrderByDescending(x => x.LatestPremiereDate)
                    .Take(16)
                    .ToList();
                
                var seriesToAdd = seriesWithLatestEpisode.Where(x => selectedSeries.All(y => y.Series.Id != x.Series.Id)).ToList();
                
                selectedSeries.AddRange(seriesToAdd);

                if (selectedSeries.Count >= 16)
                {
                    continueSearching = false;
                }
                
                currentDate = currentDate.Subtract(TimeSpan.FromDays(dayIncrement));
                
                if (currentDate < stopDate)
                {
                    break;
                }
            } while (continueSearching);
            
            // Fetch the full series objects with proper DtoOptions for images
            var seriesIds = selectedSeries.OrderByDescending(x => x.LatestPremiereDate).Select(x => x.Series.Id);
            var seriesIdArray = seriesIds.ToArray();
            var seriesItems = m_libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                ItemIds = seriesIdArray,
                DtoOptions = dtoOptions
            });
            
            // Maintain the order from our sorted list
            var orderedSeries = seriesIdArray
                .Select(id => seriesItems.FirstOrDefault(s => s.Id == id))
                .Where(s => s != null)
                .ToList();
            
            return new QueryResult<BaseItemDto>(Array.ConvertAll(orderedSeries.ToArray(),
                i => m_dtoService.GetBaseItemDto(i!, dtoOptions, user)));
        }

        protected override LatestSectionBase CreateInstance()
        {
            return new LatestShowsSection(m_userViewManager, m_userManager, m_libraryManager, m_tvSeriesManager, m_dtoService, m_serviceProvider);
        }
    }
}
