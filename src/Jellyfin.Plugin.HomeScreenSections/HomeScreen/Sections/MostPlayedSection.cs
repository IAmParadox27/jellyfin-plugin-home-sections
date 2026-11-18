using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Helpers;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using Jellyfin.Plugin.HomeScreenSections.Services;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections
{
    internal class MostPlayedSection : IHomeScreenSection
    {
        public string? Section => "MostPlayed";

        public string? DisplayText { get; set; } = "Most Played";

        public string? AdminDescription => "Movies and shows ranked by play count. Shows are ranked by their average episode play count (not the total), so a long-running show isn't automatically ranked above a movie just for having more episodes. Deterministic - the order stays consistent between this preview and the real home screen. Use the Scope option below to rank across the whole server instead of just this user.";

        public int? Limit => 1;

        public string? Route => null;

        public string? AdditionalData { get; set; }

        public object? OriginalPayload => null;

        private IUserManager UserManager { get; set; }

        private IDtoService DtoService { get; set; }

        private ILibraryManager LibraryManager { get; set; }

        private IUserDataManager UserDataManager { get; set; }

        private ServerWideStatsCache ServerWideStatsCache { get; set; }

        public MostPlayedSection(
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
                "Rank across just this user's own watch history, or aggregate play counts from every user account on the server.",
                new Dictionary<string, string>
                {
                    { "peruser", "Per-User" },
                    { "serverwide", "Server-Wide" }
                },
                "AdminMostPlayedScopeDropdown",
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
                return ServerWideStatsCache.MostPlayedItemIds.Count > 0
                    ? BuildResultFromCachedIds(ServerWideStatsCache.MostPlayedItemIds, dtoOptions, user)
                    : new QueryResult<BaseItemDto>();
            }

            VirtualFolderInfo[] folders = LibraryManager.GetVirtualFolders()
                .Where(x => x.CollectionType == CollectionTypeOptions.movies || x.CollectionType == CollectionTypeOptions.tvshows || x.IsMixedFolder(LibraryManager))
                .FilterToUserPermitted(LibraryManager, user);

            var scored = new List<(BaseItem Item, double Score)>();

            var playedMovies = folders.SelectMany(x =>
            {
                var item = LibraryManager.GetParentItem(Guid.Parse(x.ItemId), user?.Id);

                if (item is not Folder folder)
                {
                    folder = LibraryManager.GetUserRootFolder();
                }

                return folder.GetItems(new InternalItemsQuery(user)
                {
                    ParentId = Guid.Parse(x.ItemId ?? Guid.Empty.ToString()),
                    IncludeItemTypes = new[] { BaseItemKind.Movie },
                    IsPlayed = true,
                    Recursive = true,
                    DtoOptions = new DtoOptions { Fields = Array.Empty<ItemFields>(), EnableImages = false }
                }).Items;
            }).ToList();

            foreach (var movie in playedMovies)
            {
                var userData = UserDataManager.GetUserData(user, movie);
                scored.Add((movie, userData?.PlayCount ?? 0));
            }

            // Average per episode, so long shows don't beat everything by episode count.
            var playedEpisodes = folders.SelectMany(x =>
            {
                var item = LibraryManager.GetParentItem(Guid.Parse(x.ItemId), user?.Id);

                if (item is not Folder folder)
                {
                    folder = LibraryManager.GetUserRootFolder();
                }

                return folder.GetItems(new InternalItemsQuery(user)
                {
                    ParentId = Guid.Parse(x.ItemId ?? Guid.Empty.ToString()),
                    IncludeItemTypes = new[] { BaseItemKind.Episode },
                    IsPlayed = true,
                    Recursive = true,
                    DtoOptions = new DtoOptions { Fields = Array.Empty<ItemFields>(), EnableImages = false }
                }).Items;
            }).OfType<Episode>().ToList();

            var episodesBySeries = playedEpisodes
                .Where(x => x.Series != null)
                .GroupBy(x => x.Series!.Id);

            foreach (var seriesGroup in episodesBySeries)
            {
                double averagePlayCount = seriesGroup.Average(episode => UserDataManager.GetUserData(user, episode)?.PlayCount ?? 0);
                Series series = seriesGroup.First().Series!;

                scored.Add((series, averagePlayCount));
            }

            var selected = scored
                .Where(x => x.Score > 0)
                .GroupBy(x => x.Item.Id)
                .Select(g => g.OrderByDescending(x => x.Score).First())
                .OrderByDescending(x => x.Score)
                .Take(16)
                .ToList();

            var itemIds = selected.Select(r => r.Item.Id).ToArray();
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
                ViewMode = SectionViewMode.Portrait,
                PluginConfigurationOptions = GetPluginConfigurationOptions().ToArray()
            };
        }
    }
}
