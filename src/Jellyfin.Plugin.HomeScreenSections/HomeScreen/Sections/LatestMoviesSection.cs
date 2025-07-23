using Jellyfin.Data.Entities;
using Jellyfin.Data.Enums;
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

        public string? Route => "movies";
        
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

            List<BaseItem> latestMovies = m_libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[]
                {
                    BaseItemKind.Movie
                },
                Limit = 16,
                OrderBy = new[]
                {
                    (ItemSortBy.PremiereDate, SortOrder.Descending)
                }
            });

            return new QueryResult<BaseItemDto>(Array.ConvertAll(latestMovies.ToArray(),
                i => m_dtoService.GetBaseItemDto(i, dtoOptions, user)));
        }

        public IHomeScreenSection CreateInstance(Guid? userId, IEnumerable<IHomeScreenSection>? otherInstances = null)
        {
            User? user = m_userManager.GetUserById(userId ?? Guid.Empty);

            BaseItemDto? originalPayload = null;
            
            // Get only movie collection folders that the user can access
            var movieFolders = m_libraryManager.GetUserRootFolder()
                .GetChildren(user, true)
                .OfType<Folder>()
                .Where(x => (x as ICollectionFolder)?.CollectionType == CollectionType.movies)
                .ToArray();
            
            // Check if there's a configured default library, otherwise use first available
            var config = HomeScreenSectionsPlugin.Instance?.Configuration;
            var folder = !string.IsNullOrEmpty(config?.DefaultMoviesLibraryId)
                ? movieFolders.FirstOrDefault(x => x.Id.ToString() == config.DefaultMoviesLibraryId)
                : null;
            
            // Fall back to first movies library if no configured library found
            folder ??= movieFolders.FirstOrDefault();
            
            if (folder != null)
            {
                DtoOptions dtoOptions = new DtoOptions();
                dtoOptions.Fields =
                    [..dtoOptions.Fields, ItemFields.PrimaryImageAspectRatio, ItemFields.DisplayPreferencesId];
                
                originalPayload = Array.ConvertAll(new[] { folder }, i => m_dtoService.GetBaseItemDto(i, dtoOptions, user)).First();
            }

            return new LatestMoviesSection(m_userViewManager, m_userManager, m_libraryManager, m_dtoService)
            {
                DisplayText = DisplayText,
                AdditionalData = AdditionalData,
                OriginalPayload = originalPayload
            };
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