using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Helpers;
using Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections.Extra;
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

        public virtual object? OriginalPayload { get; set; }

        protected abstract BaseItemKind SectionItemKind { get; }

        protected abstract CollectionType CollectionType { get; }
        
        protected abstract CollectionTypeOptions CollectionTypeOptions { get; }

        protected abstract string? LibraryId { get; }

        protected abstract SectionViewMode DefaultViewMode { get; }
        
        protected IUserViewManager _userViewManager { get; }
        protected IUserManager _userManager { get; }
        protected ILibraryManager _libraryManager { get; }
        protected IDtoService _dtoService { get; }
        private IServiceProvider ServiceProvider { get; }

        protected RecentlyAddedSectionBase(IUserViewManager userViewManager,
            IUserManager userManager,
            ILibraryManager libraryManager,
            IDtoService dtoService,
            IServiceProvider serviceProvider)
        {
            _userViewManager = userViewManager;
            _userManager = userManager;
            _libraryManager = libraryManager;
            _dtoService = dtoService;
            ServiceProvider = serviceProvider;
        }

        public QueryResult<BaseItemDto> GetResults(HomeScreenSectionPayload payload, IQueryCollection queryCollection)
        {
            User? user = _userManager.GetUserById(payload.UserId);

            DtoOptions dtoOptions = new DtoOptions
            {
                Fields = [ItemFields.PrimaryImageAspectRatio,
                    ItemFields.Path,
                    ItemFields.DateCreated],
                ImageTypeLimit = 1,
                ImageTypes = [ImageType.Primary,
                    ImageType.Thumb,
                    ImageType.Backdrop,]
            };
            
            PluginConfiguration? config = HomeScreenSectionsPlugin.Instance?.Configuration;
            SectionSettings? sectionSettings = config?.SectionSettings.FirstOrDefault(x => string.Equals(x.SectionId, Section, StringComparison.Ordinal));
            // If HideWatchedItems is enabled for this section, set isPlayed to false to hide watched items; otherwise, include all.
            bool? isPlayed = sectionSettings?.HideWatchedItems == true ? false : null;
            
            VirtualFolderInfo[] folders = _libraryManager.GetVirtualFolders()
                .Where(x => x.CollectionType == CollectionTypeOptions)
                .FilterToUserPermitted(_libraryManager, user);

            IEnumerable<BaseItem> recentlyAddedItems = GetItems(user, dtoOptions, folders, isPlayed);
            
            return new QueryResult<BaseItemDto>(Array.ConvertAll(recentlyAddedItems.ToArray(),
                i => _dtoService.GetBaseItemDto(i, dtoOptions, user)));
        }

        public IEnumerable<IHomeScreenSection> CreateInstances(Guid? userId, int instanceCount)
        {
            BaseItemDto? originalPayload = LibrarySectionHelper.ResolveLibraryFolderDto(_libraryManager, _userManager, _dtoService, userId, CollectionType, LibraryId);

            RecentlyAddedSectionBase instance = (ActivatorUtilities.CreateInstance(ServiceProvider, GetType(), _userViewManager, _userManager, _libraryManager, _dtoService) as RecentlyAddedSectionBase)!;

            instance.AdditionalData = AdditionalData;
            instance.DisplayText = DisplayText;
            instance.OriginalPayload = originalPayload;

            yield return instance;
        }
        
        public HomeScreenSectionInfo GetInfo()
        {
            return SectionDtoHelper.CreateInfo(this, DefaultViewMode, true);
        }

        protected virtual IEnumerable<BaseItem> GetItems(User? user, DtoOptions dtoOptions, VirtualFolderInfo[] folders, bool? isPlayed)
        {
            // Default behaviour is to get the 16 most recently added items from each library that matches, then order that by date created and take 16.
            // The reason we do this is to ensure that we always get 16 items, even if there is only 1 library that matches our type.
            return folders.SelectMany(x =>
            {
                var item = _libraryManager.GetParentItem(Guid.Parse(x.ItemId), user?.Id);

                if (item is not Folder folder)
                {
                    folder = _libraryManager.GetUserRootFolder();
                }

                return folder.GetItems(new InternalItemsQuery(user)
                {
                    IncludeItemTypes = new[]
                    {
                        SectionItemKind
                    },
                    DtoOptions = dtoOptions,
                    IsPlayed = isPlayed,
                    OrderBy = [(ItemSortBy.DateCreated, SortOrder.Descending)],
                    Limit = 16,
                    IsMissing = false,
                    Recursive = true,
                    ParentId = folder.Id
                }).Items;
            }).DistinctBy(x => x.Id)
            .OrderByDescending(x => GetSortDateForItem(x, user, dtoOptions))
            .Take(16);
        }
        
        protected virtual DateTime GetSortDateForItem(BaseItem item, User? user, DtoOptions dtoOptions)
        {
            return item.DateCreated;
        }
    }
}
