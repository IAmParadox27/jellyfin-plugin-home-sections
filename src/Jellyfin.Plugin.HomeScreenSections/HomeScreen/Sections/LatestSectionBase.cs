using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Helpers;
using Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections.Extra;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.TV;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections
{
    public abstract class LatestSectionBase : IHomeScreenSection
    {
        public abstract string? Section { get; }
        public abstract string? DisplayText { get; set; }
        public virtual int? Limit => 1;
        public virtual string? Route { get; }
        public virtual string? AdditionalData { get; set; }
        public virtual object? OriginalPayload { get; set; }
        public abstract SectionViewMode DefaultViewMode { get; }
        
        protected abstract BaseItemKind SectionItemKind { get; }
        
        protected abstract CollectionType CollectionType { get; }
        
        protected abstract string? LibraryId { get; }
        
        protected abstract CollectionTypeOptions CollectionTypeOptions { get; }
        
        protected IUserViewManager _userViewManager { get; }
        protected IUserManager _userManager { get; }
        protected ILibraryManager _libraryManager { get; }
        protected IDtoService _dtoService { get; }
        protected IServiceProvider _serviceProvider { get; }
        
        protected LatestSectionBase(IUserViewManager userViewManager,
            IUserManager userManager,
            ILibraryManager libraryManager,
            IDtoService dtoService,
            IServiceProvider serviceProvider)
        {
            _userViewManager = userViewManager;
            _userManager = userManager;
            _libraryManager = libraryManager;
            _dtoService = dtoService;
            _serviceProvider = serviceProvider;
        }

        public virtual QueryResult<BaseItemDto> GetResults(HomeScreenSectionPayload payload, IQueryCollection queryCollection)
        {
            DtoOptions dtoOptions = CreateDtoOptions();
            User? user = _userManager.GetUserById(payload.UserId);

            var config = HomeScreenSectionsPlugin.Instance?.Configuration;
            var sectionSettings = config?.SectionSettings.FirstOrDefault(x => string.Equals(x.SectionId, Section, StringComparison.Ordinal));
            // If HideWatchedItems is enabled for this section, set isPlayed to false to hide watched items; otherwise, include all.
            bool? isPlayed = sectionSettings?.HideWatchedItems == true ? false : null;

            VirtualFolderInfo[] folders = _libraryManager.GetVirtualFolders()
                .Where(x => x.CollectionType == CollectionTypeOptions)
                .FilterToUserPermitted(_libraryManager, user);

            List<(BaseItem Item, DateTime? PremiereDate)> selectedItems = SearchLatestItems(user, folders, isPlayed);

            return new QueryResult<BaseItemDto>(Array.ConvertAll(selectedItems.OrderByDescending(x => x.PremiereDate).Select(x => x.Item).ToArray(),
                i => _dtoService.GetBaseItemDto(i, dtoOptions, user)));
        }
        
        public IEnumerable<IHomeScreenSection> CreateInstances(Guid? userId, int instanceCount)
        {
            BaseItemDto? originalPayload = LibrarySectionHelper.ResolveLibraryFolderDto(_libraryManager, _userManager, _dtoService, userId, CollectionType, LibraryId);

            LatestSectionBase sectionBase = CreateInstance();
            sectionBase.DisplayText = DisplayText;
            sectionBase.AdditionalData = AdditionalData;
            sectionBase.OriginalPayload = originalPayload;

            yield return sectionBase;
        }

        public HomeScreenSectionInfo GetInfo()
        {
            return SectionDtoHelper.CreateInfo(this, DefaultViewMode, true);
        }

        protected static DtoOptions CreateDtoOptions()
        {
            DtoOptions dtoOptions = new DtoOptions
            {
                Fields = [ItemFields.PrimaryImageAspectRatio,
                    ItemFields.Path],
                EnableImages = true
            };

            dtoOptions.ImageTypeLimit = 1;
            dtoOptions.ImageTypes = [ImageType.Thumb,
                ImageType.Backdrop,
                ImageType.Primary,];

            return dtoOptions;
        }

        private List<(BaseItem Item, DateTime? PremiereDate)> SearchLatestItems(User? user, VirtualFolderInfo[] folders, bool? isPlayed)
        {
            List<(BaseItem Item, DateTime? PremiereDate)> selectedItems = [];
            int dayIncrement = 30;
            DateTime currentDate = DateTime.Now;
            DateTime stopDate = DateTime.Parse("01/01/1887", System.Globalization.CultureInfo.InvariantCulture); // The first movie ever was 1888 so this should be safe, we never expect to get as far back as this but we need an escape.
            bool continueSearching = true;

            do
            {
                List<(BaseItem Item, DateTime? PremiereDate)> itemsToAdd = QueryItemsInWindow(user, folders, isPlayed, currentDate, dayIncrement)
                    .Where(x => selectedItems.All(y => y.Item.Id != x.Item.Id))
                    .ToList();
                
                selectedItems.AddRange(itemsToAdd);

                if (selectedItems.Count >= 16)
                {
                    continueSearching = false;
                }
                
                currentDate = currentDate.Subtract(TimeSpan.FromDays(dayIncrement));
                
                if (currentDate < stopDate)
                {
                    break;
                }
            } while (continueSearching);

            return selectedItems;
        }

        private List<(BaseItem Item, DateTime? PremiereDate)> QueryItemsInWindow(
            User? user,
            VirtualFolderInfo[] folders,
            bool? isPlayed,
            DateTime currentDate,
            int dayIncrement)
        {
            var latestMovies = folders.Select(x =>
            {
                var item = _libraryManager.GetParentItem(Guid.Parse(x.ItemId), user?.Id);

                if (item is not Folder folder)
                {
                    folder = _libraryManager.GetUserRootFolder();
                }

                var items = folder.GetItems(new InternalItemsQuery(user)
                {
                    IncludeItemTypes = new[]
                    {
                        SectionItemKind
                    },
                    Limit = 16,
                    OrderBy = new[]
                    {
                        (ItemSortBy.PremiereDate, SortOrder.Descending)
                    },
                    IsPlayed = isPlayed,
                    ParentId = Guid.Parse(x.ItemId),
                    Recursive = true,
                    MaxPremiereDate = currentDate,
                    MinPremiereDate = currentDate.Subtract(TimeSpan.FromDays(dayIncrement)),
                    EnableTotalRecordCount = true // This might have to go
                });

                return (Items: items.Items, items.Items.Count, items.TotalRecordCount);
            }).ToArray();
            
            return latestMovies
                .SelectMany(x => x.Items)
                .Select(x => (Item: x, PremiereDate: x.PremiereDate))
                .ToList();
        }
        
        protected abstract LatestSectionBase CreateInstance();
    }
}
