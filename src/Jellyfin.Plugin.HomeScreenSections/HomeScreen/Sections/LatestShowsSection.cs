using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Library;
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

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections
{
    public class LatestShowsSection : IHomeScreenSection
    {
        public string? Section => "LatestShows";
        
        public string? DisplayText { get; set; } = "Latest Shows";
        
        public int? Limit => 1;
        
        public string? Route { get; }
        
        public string? AdditionalData { get; set; }
        
        public object? OriginalPayload { get; }
        
        private readonly IUserViewManager m_userViewManager;
        private readonly IUserManager m_userManager;
        private readonly ILibraryManager m_libraryManager;
        private readonly ITVSeriesManager m_tvSeriesManager;
        private readonly IDtoService m_dtoService;

        public LatestShowsSection(IUserViewManager userViewManager,
            IUserManager userManager,
            ILibraryManager libraryManager,
            ITVSeriesManager tvSeriesManager,
            IDtoService dtoService)
        {
            m_userViewManager = userViewManager;
            m_userManager = userManager;
            m_libraryManager = libraryManager;
            m_tvSeriesManager = tvSeriesManager;
            m_dtoService = dtoService;
        }
        
        public QueryResult<BaseItemDto> GetResults(HomeScreenSectionPayload payload, IQueryCollection queryCollection)
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

            IReadOnlyList<BaseItem> episodes = m_libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.Episode },
                OrderBy = new[] { (ItemSortBy.PremiereDate, SortOrder.Descending) },
                DtoOptions = new DtoOptions
                    { Fields = new[] { ItemFields.ParentId }, EnableImages = false }
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
    }
}