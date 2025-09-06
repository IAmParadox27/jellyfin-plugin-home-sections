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
    public class LatestAlbumsSection : IHomeScreenSection
    {
        public string? Section => "LatestAlbums";
        
        public string? DisplayText { get; set; } = "Latest Albums";
        
        public int? Limit => 1;

        public string? Route => "music";
        
        public string? AdditionalData { get; set; } = "albums";
        
        public object? OriginalPayload { get; set; } = null;
        
        private readonly IUserViewManager m_userViewManager;
        private readonly IUserManager m_userManager;
        private readonly ILibraryManager m_libraryManager;
        private readonly IDtoService m_dtoService;

        public LatestAlbumsSection(IUserViewManager userViewManager,
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
                ImageType.Primary,
                ImageType.Thumb,
                ImageType.Backdrop,
            };
            
            User? user = m_userManager.GetUserById(payload.UserId);

            IReadOnlyList<BaseItem> latestAlbums = m_libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[]
                {
                    BaseItemKind.MusicAlbum
                },
                Limit = 16,
                OrderBy = new[]
                {
                    (ItemSortBy.PremiereDate, SortOrder.Descending)
                }
            });

            return new QueryResult<BaseItemDto>(Array.ConvertAll(latestAlbums.ToArray(),
                i => m_dtoService.GetBaseItemDto(i, dtoOptions, user)));
        }


        public IHomeScreenSection CreateInstance(Guid? userId, IEnumerable<IHomeScreenSection>? otherInstances = null)
        {
            User? user = m_userManager.GetUserById(userId ?? Guid.Empty);

            BaseItemDto? originalPayload = null;
            
            var musicFolders = m_libraryManager.GetUserRootFolder()
                .GetChildren(user, true)
                .OfType<Folder>()
                .Where(x => (x as ICollectionFolder)?.CollectionType == CollectionType.music)
                .ToArray();
            
            var config = HomeScreenSectionsPlugin.Instance?.Configuration;
            var folder = !string.IsNullOrEmpty(config?.DefaultMusicLibraryId)
                ? musicFolders.FirstOrDefault(x => x.Id.ToString() == config.DefaultMusicLibraryId)
                : null;
            
            folder ??= musicFolders.FirstOrDefault();
            
            if (folder != null)
            {
                DtoOptions dtoOptions = new DtoOptions();
                dtoOptions.Fields =
                    [..dtoOptions.Fields, ItemFields.PrimaryImageAspectRatio, ItemFields.DisplayPreferencesId];
                
                originalPayload = Array.ConvertAll(new[] { folder }, i => m_dtoService.GetBaseItemDto(i, dtoOptions, user)).First();
            }

            return new LatestAlbumsSection(m_userViewManager, m_userManager, m_libraryManager, m_dtoService)
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
                ViewMode = SectionViewMode.Square
            };
        }
    }
}
