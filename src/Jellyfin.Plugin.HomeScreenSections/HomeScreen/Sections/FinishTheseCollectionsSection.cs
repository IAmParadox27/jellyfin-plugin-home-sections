using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Helpers;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using Jellyfin.Plugin.HomeScreenSections.Services;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections
{
    internal class FinishTheseCollectionsSection : IHomeScreenSection
    {
        public string? Section => "FinishTheseCollections";

        public string? DisplayText { get; set; } = "Finish These Collections";

        public string? AdminDescription => "Box sets/collections the user has started but not finished, ordered by how close they are to completion (closest first).";

        public int? Limit => 1;

        public string? Route => null;

        public string? AdditionalData { get; set; }

        public object? OriginalPayload => null;

        private IUserManager UserManager { get; set; }

        private IDtoService DtoService { get; set; }

        private ILibraryManager LibraryManager { get; set; }

        private IUserDataManager UserDataManager { get; set; }

        private PerUserComputedStatsCache StatsCache { get; set; }

        public FinishTheseCollectionsSection(
            IUserManager userManager,
            IDtoService dtoService,
            ILibraryManager libraryManager,
            IUserDataManager userDataManager,
            PerUserComputedStatsCache statsCache)
        {
            UserManager = userManager;
            DtoService = dtoService;
            LibraryManager = libraryManager;
            UserDataManager = userDataManager;
            StatsCache = statsCache;
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

            // Expensive to redo every load - cache it per user instead.
            if (!StatsCache.TryGetOrCompute(
                user.Id,
                "finish-collections",
                TimeSpan.FromHours(24),
                () => ComputeInProgressCollectionIds(user),
                out Guid[] itemIds))
            {
                return new QueryResult<BaseItemDto>();
            }

            IReadOnlyList<BaseItem> fullItems = LibraryManager.GetItemList(new InternalItemsQuery
            {
                ItemIds = itemIds,
                DtoOptions = dtoOptions
            });

            List<BaseItem?> orderedItems = itemIds
                .Select(id => fullItems.FirstOrDefault(i => i.Id == id))
                .Where(i => i != null)
                .ToList();

            return new QueryResult<BaseItemDto>(DtoService.GetBaseItemDtos(orderedItems!, dtoOptions, user));
        }

        private Guid[] ComputeInProgressCollectionIds(User user)
        {
            VirtualFolderInfo[] boxsetFolders = LibraryManager.GetVirtualFolders()
                .Where(x => x.CollectionType == CollectionTypeOptions.boxsets)
                .FilterToUserPermitted(LibraryManager, user);

            List<BoxSet> boxsets = boxsetFolders.SelectMany(x =>
            {
                BaseItem? item = LibraryManager.GetParentItem(Guid.Parse(x.ItemId), user.Id);

                if (item is not Folder folder)
                {
                    folder = LibraryManager.GetUserRootFolder();
                }

                return folder.GetItems(new InternalItemsQuery(user)
                {
                    ParentId = Guid.Parse(x.ItemId ?? Guid.Empty.ToString()),
                    IncludeItemTypes = new[] { BaseItemKind.BoxSet },
                    Recursive = true,
                    DtoOptions = new DtoOptions { Fields = Array.Empty<ItemFields>(), EnableImages = false }
                }).Items;
            }).OfType<BoxSet>().DistinctBy(x => x.Id).ToList();

            List<(BaseItem BoxSet, double Completion)> inProgress = new List<(BaseItem BoxSet, double Completion)>();

            foreach (BoxSet boxset in boxsets)
            {
                List<BaseItem> children = boxset.GetChildren(user, true, null)
                    .Where(x => x is Movie || x is Series)
                    .ToList();

                if (children.Count == 0)
                {
                    continue;
                }

                int playedCount = children.Count(x => UserDataManager.GetUserData(user, x)?.Played == true);

                if (playedCount == 0 || playedCount >= children.Count)
                {
                    continue;
                }

                inProgress.Add((boxset, (double)playedCount / children.Count));
            }

            return inProgress
                .OrderByDescending(x => x.Completion)
                .Take(16)
                .Select(r => r.BoxSet.Id)
                .ToArray();
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
