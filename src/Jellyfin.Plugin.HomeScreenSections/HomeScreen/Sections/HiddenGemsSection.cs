using Jellyfin.Extensions;
using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Helpers;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using Jellyfin.Plugin.HomeScreenSections.Services;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections
{
    internal class HiddenGemsSection : IHomeScreenSection
    {
        private const double MinCommunityRating = 7.5;

        public string? Section => "HiddenGems";

        public string? DisplayText { get; set; } = "Hidden Gems";

        public string? AdminDescription => "Highly rated movies and shows (7.5+) you haven't watched yet. Picks a random subset each time it loads, so the exact items shown may differ from this preview.";

        public int? Limit => 1;

        public string? Route => null;

        public string? AdditionalData { get; set; }

        public object? OriginalPayload => null;

        private IUserManager UserManager { get; set; }

        private IDtoService DtoService { get; set; }

        private ILibraryManager LibraryManager { get; set; }

        private PerUserComputedStatsCache StatsCache { get; set; }

        public HiddenGemsSection(
            IUserManager userManager,
            IDtoService dtoService,
            ILibraryManager libraryManager,
            PerUserComputedStatsCache statsCache)
        {
            UserManager = userManager;
            DtoService = dtoService;
            LibraryManager = libraryManager;
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

            // Only the candidate pool is cached - the 16 shown are still shuffled fresh each request for variety.
            if (!StatsCache.TryGetOrCompute(
                user.Id,
                "hidden-gems-candidates",
                TimeSpan.FromHours(24),
                () => ComputeCandidateIds(user),
                out Guid[] candidateIds))
            {
                return new QueryResult<BaseItemDto>();
            }

            List<Guid> shuffledIds = candidateIds.ToList();
            shuffledIds.Shuffle();
            Guid[] itemIds = shuffledIds.Take(16).ToArray();

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

        private Guid[] ComputeCandidateIds(User user)
        {
            VirtualFolderInfo[] folders = LibraryManager.GetVirtualFolders()
                .Where(x => x.CollectionType == CollectionTypeOptions.movies || x.CollectionType == CollectionTypeOptions.tvshows || x.IsMixedFolder(LibraryManager))
                .FilterToUserPermitted(LibraryManager, user);

            return folders.SelectMany(x =>
            {
                BaseItem? item = LibraryManager.GetParentItem(Guid.Parse(x.ItemId), user.Id);

                if (item is not Folder folder)
                {
                    folder = LibraryManager.GetUserRootFolder();
                }

                return folder.GetItems(new InternalItemsQuery(user)
                {
                    ParentId = Guid.Parse(x.ItemId ?? Guid.Empty.ToString()),
                    IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
                    Recursive = true,
                    IsPlayed = false,
                    MinCommunityRating = MinCommunityRating,
                    OrderBy = new[] { (ItemSortBy.Random, SortOrder.Ascending) },
                    Limit = 48,
                    DtoOptions = new DtoOptions { Fields = Array.Empty<ItemFields>(), EnableImages = false }
                }).Items;
            }).Select(x => x.Id).Distinct().ToArray();
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
