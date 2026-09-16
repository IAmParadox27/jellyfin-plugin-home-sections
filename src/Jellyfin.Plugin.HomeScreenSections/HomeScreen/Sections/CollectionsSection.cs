using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Helpers;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections
{
    internal class CollectionsSection : IHomeScreenSection
    {
        public string? Section => "CollectionsSection";

        public string? DisplayText { get; set; } = "Collections";

        public int? Limit => 1;

        public string? Route => null;

        public string? AdditionalData { get; set; }

        public object? OriginalPayload => null;

        private IUserManager UserManager { get; set; }

        private IDtoService DtoService { get; set; }

        private ILibraryManager LibraryManager { get; set; }

        public CollectionsSection(
            IUserManager userManager,
            IDtoService dtoService,
            ILibraryManager libraryManager)
        {
            UserManager = userManager;
            DtoService = dtoService;
            LibraryManager = libraryManager;
        }

        public QueryResult<BaseItemDto> GetResults(HomeScreenSectionPayload payload, IQueryCollection queryCollection)
        {
            DtoOptions? dtoOptions = new DtoOptions
            {
                Fields = new List<ItemFields>
                {
                    ItemFields.PrimaryImageAspectRatio,
                    ItemFields.Path,
                    ItemFields.DateCreated
                },
                ImageTypeLimit = 1,
                ImageTypes = new List<ImageType>
                {
                    ImageType.Thumb,
                    ImageType.Backdrop,
                    ImageType.Primary,
                }
            };

            User user = UserManager.GetUserById(payload.UserId)!;

            VirtualFolderInfo[] boxsetFolders = LibraryManager.GetVirtualFolders()
                .Where(x => x.CollectionType == CollectionTypeOptions.boxsets)
                .FilterToUserPermitted(LibraryManager, user);

            var boxsets = boxsetFolders.SelectMany(x =>
            {
                var item = LibraryManager.GetParentItem(Guid.Parse(x.ItemId), user?.Id);

                if (item is not Folder folder)
                {
                    folder = LibraryManager.GetUserRootFolder();
                }

                return folder.GetItems(new InternalItemsQuery(user)
                {
                    ParentId = Guid.Parse(x.ItemId ?? Guid.Empty.ToString()),
                    IncludeItemTypes = new[] { BaseItemKind.BoxSet },
                    Recursive = true,
                    OrderBy = new[] { (ItemSortBy.DateLastContentAdded, SortOrder.Descending) },
                    DtoOptions = new DtoOptions { Fields = Array.Empty<ItemFields>(), EnableImages = false }
                }).Items;
            })
            .OrderByDescending(x =>
            {
                if (x is BoxSet boxset)
                {
                    return boxset.DateLastMediaAdded;
                }

                return DateTime.MinValue;
            })
            .Take(16)
                .ToList();

            // TODO: Add nice logic to score collections that are either higher or lower watched, or have more movies etc
            
            // Fetch full items with images
            var itemIds = boxsets.Select(r => r.Id).ToArray();
            var fullItems = LibraryManager.GetItemList(new InternalItemsQuery(user)
            {
                ItemIds = itemIds,
                DtoOptions = dtoOptions
            });

            // Maintain order
            var orderedItems = itemIds
                .Select(id => fullItems.FirstOrDefault(i => i.Id == id))
                .Where(i => i != null)
                .ToList();

            return new QueryResult<BaseItemDto>(DtoService.GetBaseItemDtos(orderedItems!, dtoOptions, user));
        }

        public IEnumerable<IHomeScreenSection> CreateInstances(Guid? userId, int instanceCount)
        {
            yield return this;
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
                ViewMode = SectionViewMode.Portrait,
                PluginConfigurationOptions = (this as IHomeScreenSection).GetPluginConfigurationOptions().ToArray()
            };
        }
    }
}
