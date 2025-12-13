using Jellyfin.Plugin.HomeScreenSections.Configuration;
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

            // Single query: Get recent episodes, limited but enough to find 16 unique series
            // Fetch more episodes to account for multiple episodes per series
            var recentEpisodes = m_libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.Episode },
                OrderBy = new[] { (ItemSortBy.PremiereDate, SortOrder.Descending) },
                Limit = 200, // Enough to find 16 unique series even with multi-episode releases
                IsVirtualItem = false,
                IsPlayed = isPlayed,
                Recursive = true,
                DtoOptions = new DtoOptions { Fields = Array.Empty<ItemFields>(), EnableImages = false }
            }).OfType<Episode>()
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

            // Fetch the full series objects with proper DtoOptions for images
            var seriesIds = seriesWithLatestEpisode.Select(x => x.Series.Id).ToArray();
            var seriesItems = m_libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                ItemIds = seriesIds,
                DtoOptions = dtoOptions
            });

            // Maintain the order from our sorted list
            var orderedSeries = seriesIds
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
