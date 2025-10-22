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

            IReadOnlyList<BaseItem> episodes = m_libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { SectionItemKind },
                OrderBy = new[] { (ItemSortBy.PremiereDate, SortOrder.Descending) },
                DtoOptions = new DtoOptions
                    { Fields = Array.Empty<ItemFields>(), EnableImages = true },
                IsPlayed = isPlayed
            });
            
            List<BaseItem> series = episodes
                .Where(x => !x.IsUnaired && !x.IsVirtualItem)
                .Select(x => (x.FindParent<Series>(), (x as Episode)?.PremiereDate))
                .GroupBy(x => x.Item1)
                .Select(x => (x.Key, x.Max(y => y.PremiereDate)))
                .OrderByDescending(x => x.Item2)
                .Select(x => x.Key as BaseItem)
                .Take(16)
                .ToList();
            
            return new QueryResult<BaseItemDto>(Array.ConvertAll(series.ToArray(),
                i => m_dtoService.GetBaseItemDto(i, dtoOptions, user)));
        }
        
        protected override LatestSectionBase CreateInstance()
        {
            return new LatestShowsSection(m_userViewManager, m_userManager, m_libraryManager, m_tvSeriesManager, m_dtoService, m_serviceProvider);
        }
    }
}