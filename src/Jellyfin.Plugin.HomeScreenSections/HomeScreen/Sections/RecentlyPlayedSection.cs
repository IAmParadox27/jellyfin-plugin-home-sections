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
    internal class RecentlyPlayedSection : IHomeScreenSection
    {
        public string? Section => "RecentlyPlayed";

        public string? DisplayText { get; set; } = "Recently Played";

        public string? AdminDescription => "Everything the user has finished watching, most recent first - no cutoff. Unlike Watch Again, this includes anything watched, not just items due for a rewatch. Use the Scope option below to show whatever was most recently played by anyone on the server instead of just this user.";

        public int? Limit => 1;

        public string? Route => null;

        public string? AdditionalData { get; set; }

        public object? OriginalPayload => null;

        private IUserManager UserManager { get; set; }

        private IDtoService DtoService { get; set; }

        private ILibraryManager LibraryManager { get; set; }

        private IUserDataManager UserDataManager { get; set; }

        private ServerWideStatsCache ServerWideStatsCache { get; set; }

        public RecentlyPlayedSection(
            IUserManager userManager,
            IDtoService dtoService,
            ILibraryManager libraryManager,
            IUserDataManager userDataManager,
            ServerWideStatsCache serverWideStatsCache)
        {
            UserManager = userManager;
            DtoService = dtoService;
            LibraryManager = libraryManager;
            UserDataManager = userDataManager;
            ServerWideStatsCache = serverWideStatsCache;
        }

        public IEnumerable<PluginConfigurationOption> GetPluginConfigurationOptions()
        {
            yield return PluginConfigurationHelper.CreateDropdown(
                "scope",
                "Scope",
                "Show what just this user has watched recently, or whatever was most recently played by anyone on the server.",
                new Dictionary<string, string>
                {
                    { "peruser", "Per-User" },
                    { "serverwide", "Server-Wide" }
                },
                "AdminRecentlyPlayedScopeDropdown",
                "peruser",
                true);
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

            string scope = HomeScreenSectionPayload.GetEffectiveStringConfig(Section ?? string.Empty, "scope", "peruser");

            if (scope == "serverwide")
            {
                // Empty until ServerWideStatsRefreshTask has run once.
                return ServerWideStatsCache.RecentlyPlayedItemIds.Count > 0
                    ? BuildResultFromCachedIds(ServerWideStatsCache.RecentlyPlayedItemIds, dtoOptions, user)
                    : new QueryResult<BaseItemDto>();
            }

            VirtualFolderInfo[] folders = LibraryManager.GetVirtualFolders()
                .Where(x => x.CollectionType == CollectionTypeOptions.movies || x.CollectionType == CollectionTypeOptions.tvshows || x.IsMixedFolder(LibraryManager))
                .FilterToUserPermitted(LibraryManager, user);

            var playedItems = folders.SelectMany(x =>
            {
                var item = LibraryManager.GetParentItem(Guid.Parse(x.ItemId), user?.Id);

                if (item is not Folder folder)
                {
                    folder = LibraryManager.GetUserRootFolder();
                }

                return folder.GetItems(new InternalItemsQuery(user)
                {
                    ParentId = Guid.Parse(x.ItemId ?? Guid.Empty.ToString()),
                    IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Episode },
                    IsPlayed = true,
                    Recursive = true,
                    DtoOptions = new DtoOptions { Fields = Array.Empty<ItemFields>(), EnableImages = false }
                }).Items;
            }).GroupBy(x => x.Id).Select(x => x.First()).ToList();

            var ordered = playedItems
                .Select(item => (Item: item, LastPlayed: UserDataManager.GetUserData(user, item)?.LastPlayedDate))
                .Where(x => x.LastPlayed.HasValue)
                .OrderByDescending(x => x.LastPlayed!.Value)
                .Take(16)
                .ToList();

            var itemIds = ordered.Select(r => r.Item.Id).ToArray();
            var fullItems = LibraryManager.GetItemList(new InternalItemsQuery
            {
                ItemIds = itemIds,
                DtoOptions = dtoOptions
            });

            var orderedItems = itemIds
                .Select(id => fullItems.FirstOrDefault(i => i.Id == id))
                .Where(i => i != null)
                .ToList();

            return new QueryResult<BaseItemDto>(DtoService.GetBaseItemDtos(orderedItems!, dtoOptions, user));
        }

        private QueryResult<BaseItemDto> BuildResultFromCachedIds(IReadOnlyList<Guid> itemIds, DtoOptions dtoOptions, User user)
        {
            // Passing the user keeps library access checks in place.
            var fullItems = LibraryManager.GetItemList(new InternalItemsQuery(user)
            {
                ItemIds = itemIds.ToArray(),
                DtoOptions = dtoOptions
            });

            var orderedItems = itemIds
                .Select(id => fullItems.FirstOrDefault(i => i.Id == id))
                .Where(i => i != null)
                .Take(16)
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
                ViewMode = SectionViewMode.Landscape,
                PluginConfigurationOptions = GetPluginConfigurationOptions().ToArray()
            };
        }
    }
}
