using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections
{
    public class LatestMoviesSection : IHomeScreenSection
    {
        public string? Section => "LatestMovies";
        
        public string? DisplayText { get; set; } = "Latest Movies";
        
        public int? Limit => 1;

        public string? Route { get; set; } = null;
        
        public string? AdditionalData { get; set; }
        
        public object? OriginalPayload { get; set; } = null;
        
        private readonly IUserViewManager m_userViewManager;
        private readonly IUserManager m_userManager;
        private readonly ILibraryManager m_libraryManager;
        private readonly IDtoService m_dtoService;
        
        public LatestMoviesSection(IUserViewManager userViewManager,
            IUserManager userManager,
            ILibraryManager libraryManager,
            IDtoService dtoService)
        {
            m_userViewManager = userViewManager;
            m_userManager = userManager;
            m_libraryManager = libraryManager;
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

            Folder? movieFolder = m_libraryManager.GetUserRootFolder()
                .GetChildren(user, true)
                .OfType<Folder>()
                .FirstOrDefault(x => (x as ICollectionFolder)?.CollectionType == CollectionType.movies);

            QueryResult<BaseItem>? latestMovies = movieFolder?.GetItems(new InternalItemsQuery
            {
                IncludeItemTypes = new[]
                {
                    BaseItemKind.Movie
                },
                Limit = 16,
                User = user,
                OrderBy = new[]
                {
                    (ItemSortBy.PremiereDate, SortOrder.Descending)
                }
            });

            return new QueryResult<BaseItemDto>(Array.ConvertAll(latestMovies?.Items.ToArray() ?? Array.Empty<BaseItem>(),
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