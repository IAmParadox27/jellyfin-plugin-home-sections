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
    public abstract class RecentlyAddedSectionBase : IHomeScreenSection
    {
        public abstract string? Section { get; }

        public abstract string? DisplayText { get; set; }

        public virtual int? Limit => 1;

        public abstract string? Route { get; }

        public abstract string? AdditionalData { get; set; }

        public virtual object? OriginalPayload { get; set; } = null;

        protected abstract BaseItemKind SectionItemKind { get; }

        protected abstract CollectionType CollectionType { get; }

        protected abstract string? LibraryId { get; }

        protected abstract SectionViewMode DefaultViewMode { get; }
        
        private readonly IUserViewManager m_userViewManager;
        private readonly IUserManager m_userManager;
        private readonly ILibraryManager m_libraryManager;
        private readonly IDtoService m_dtoService;

        protected RecentlyAddedSectionBase(IUserViewManager userViewManager,
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
            User? user = m_userManager.GetUserById(payload.UserId);

            DtoOptions dtoOptions = new DtoOptions
            {
                Fields = new List<ItemFields>
                {
                    ItemFields.PrimaryImageAspectRatio,
                    ItemFields.Path
                },
                ImageTypeLimit = 1,
                ImageTypes = new List<ImageType>
                {
                    ImageType.Primary,
                    ImageType.Thumb,
                    ImageType.Backdrop,
                }
            };
            
            var config = HomeScreenSectionsPlugin.Instance?.Configuration;
            var sectionSettings = config?.SectionSettings.FirstOrDefault(x => x.SectionId == Section);
            // If HideWatchedItems is enabled for this section, set isPlayed to false to hide watched items; otherwise, include all.
            bool? isPlayed = sectionSettings?.HideWatchedItems == true ? false : null;
            
            IReadOnlyList<BaseItem> recentlyAddedItems = m_libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[]
                {
                    SectionItemKind
                },
                Limit = 16,
                OrderBy = new[]
                {
                    (ItemSortBy.DateCreated, SortOrder.Descending)
                },
                DtoOptions = dtoOptions,
                IsPlayed = isPlayed
            });

            return new QueryResult<BaseItemDto>(Array.ConvertAll(recentlyAddedItems.ToArray(),
                i => m_dtoService.GetBaseItemDto(i, dtoOptions, user)));
        }

        public IHomeScreenSection CreateInstance(Guid? userId, IEnumerable<IHomeScreenSection>? otherInstances = null)
        {
            User? user = m_userManager.GetUserById(userId ?? Guid.Empty);

            BaseItemDto? originalPayload = null;
            
            Folder[] itemFolders = m_libraryManager.GetUserRootFolder()
                .GetChildren(user, true)
                .OfType<Folder>()
                .Where(x => (x as ICollectionFolder)?.CollectionType == CollectionType)
                .ToArray();
            
            Folder? folder = !string.IsNullOrEmpty(LibraryId)
                ? itemFolders.FirstOrDefault(x => x.Id.ToString() == LibraryId)
                : null;
            
            folder ??= itemFolders.FirstOrDefault();
            
            if (folder != null)
            {
                DtoOptions dtoOptions = new DtoOptions();
                dtoOptions.Fields =
                    [..dtoOptions.Fields, ItemFields.PrimaryImageAspectRatio, ItemFields.DisplayPreferencesId];

                originalPayload = Array.ConvertAll(new[] { folder }, i => m_dtoService.GetBaseItemDto(i, dtoOptions, user)).First();
            }

            RecentlyAddedSectionBase instance = (Activator.CreateInstance(GetType(), m_userViewManager, m_userManager, m_libraryManager, m_dtoService) as RecentlyAddedSectionBase)!;
            
            instance.AdditionalData = AdditionalData;
            instance.DisplayText = DisplayText;
            instance.OriginalPayload = originalPayload;
            
            return instance;
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
                ViewMode = DefaultViewMode,
                AllowHideWatched = true
            };
        }
    }
}
