using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

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
        
        protected abstract CollectionTypeOptions CollectionTypeOptions { get; }

        protected abstract string? LibraryId { get; }

        protected abstract SectionViewMode DefaultViewMode { get; }
        
        protected readonly IUserViewManager m_userViewManager;
        protected readonly IUserManager m_userManager;
        protected readonly ILibraryManager m_libraryManager;
        protected readonly IDtoService m_dtoService;
        private readonly IServiceProvider m_serviceProvider;

        protected RecentlyAddedSectionBase(IUserViewManager userViewManager,
            IUserManager userManager,
            ILibraryManager libraryManager,
            IDtoService dtoService,
            IServiceProvider serviceProvider)
        {
            m_userViewManager = userViewManager;
            m_userManager = userManager;
            m_libraryManager = libraryManager;
            m_dtoService = dtoService;
            m_serviceProvider = serviceProvider;
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
            
            PluginConfiguration? config = HomeScreenSectionsPlugin.Instance?.Configuration;
            SectionSettings? sectionSettings = config?.SectionSettings.FirstOrDefault(x => x.SectionId == Section);
            // If HideWatchedItems is enabled for this section, set isPlayed to false to hide watched items; otherwise, include all.
            bool? isPlayed = sectionSettings?.HideWatchedItems == true ? false : null;
            
            VirtualFolderInfo[] folders = m_libraryManager.GetVirtualFolders()
                .Where(x => x.CollectionType == CollectionTypeOptions)
                .ToArray();

            IEnumerable<BaseItem> recentlyAddedItems = GetItems(user, dtoOptions, folders, isPlayed);
            
            return new QueryResult<BaseItemDto>(Array.ConvertAll(recentlyAddedItems.ToArray(),
                i => m_dtoService.GetBaseItemDto(i, dtoOptions, user)));
        }

        public IEnumerable<IHomeScreenSection> CreateInstances(Guid? userId, int instanceCount)
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

            RecentlyAddedSectionBase instance = (ActivatorUtilities.CreateInstance(m_serviceProvider, GetType(), m_userViewManager, m_userManager, m_libraryManager, m_dtoService) as RecentlyAddedSectionBase)!;
            
            instance.AdditionalData = AdditionalData;
            instance.DisplayText = DisplayText;
            instance.OriginalPayload = originalPayload;
            
            yield return instance;
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

        protected virtual IEnumerable<BaseItem> GetItems(User? user, DtoOptions dtoOptions, VirtualFolderInfo[] folders, bool? isPlayed)
        {
            // Default behaviour is to get the 16 most recently added items from each library that matches, then order that by date created and take 16.
            // The reason we do this is to ensure that we always get 16 items, even if there is only 1 library that matches our type.
            return folders.SelectMany(x => m_libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[]
                {
                    SectionItemKind
                },
                DtoOptions = dtoOptions,
                IsPlayed = isPlayed,
                OrderBy = new [] { (ItemSortBy.DateCreated, SortOrder.Descending) },
                Limit = 16,
                IsMissing = false
            })).DistinctBy(x => x.Id)
            .OrderByDescending(x => GetSortDateForItem(x, user, dtoOptions))
            .Take(16);
        }
        
        protected virtual DateTime GetSortDateForItem(BaseItem item, User? user, DtoOptions dtoOptions)
        {
            return item.DateCreated;
        }
    }
}
