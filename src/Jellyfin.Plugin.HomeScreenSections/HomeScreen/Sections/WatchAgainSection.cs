using Jellyfin.Extensions;
using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Helpers;
using Jellyfin.Plugin.HomeScreenSections.JellyfinVersionSpecific;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.TV;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections
{
    internal class WatchAgainSection : IHomeScreenSection
    {
        public string? Section => "WatchAgain";

        public string? DisplayText { get; set; } = "Watch It Again";

        public int? Limit => 1;

        public string? Route => null;

        public string? AdditionalData { get; set; }

        public object? OriginalPayload => null;

        private ICollectionManager CollectionManager { get; set; }

        private IUserManager UserManager { get; set; }

        private IDtoService DtoService { get; set; }

        private IUserDataManager UserDataManager { get; set; }

        private ITVSeriesManager TVSeriesManager { get; set; }

        private ILibraryManager LibraryManager { get; set; }

        private CollectionManagerProxy CollectionManagerProxy { get; set; }

        public WatchAgainSection(
            ICollectionManager collectionManager,
            IUserManager userManager,
            IDtoService dtoService,
            IUserDataManager userDataManager,
            ITVSeriesManager tvSeriesManager,
            ILibraryManager libraryManager,
            CollectionManagerProxy collectionManagerProxy)
        {
            CollectionManager = collectionManager;
            UserManager = userManager;
            DtoService = dtoService;
            UserDataManager = userDataManager;
            TVSeriesManager = tvSeriesManager;
            LibraryManager = libraryManager;
            CollectionManagerProxy = collectionManagerProxy;
        }

        public QueryResult<BaseItemDto> GetResults(HomeScreenSectionPayload payload, IQueryCollection queryCollection)
        {
            DtoOptions? dtoOptions = new DtoOptions
            {
                Fields = new List<ItemFields>
                {
                    ItemFields.PrimaryImageAspectRatio
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
            var cutoffDate = DateTime.Now.Subtract(TimeSpan.FromDays(28));

            List<(BaseItem Item, DateTime? LastPlayed)> results = new List<(BaseItem, DateTime?)>();

            // === Process Box Sets ===
            {
                VirtualFolderInfo[] folders = LibraryManager.GetVirtualFolders()
                    .Where(x => x.CollectionType == CollectionTypeOptions.boxsets)
                    .FilterToUserPermitted(LibraryManager, user);

                var boxSets = folders.SelectMany(x =>
                {
                    return LibraryManager.GetItemList(new InternalItemsQuery(user)
                    {
                        ParentId = Guid.Parse(x.ItemId ?? Guid.Empty.ToString()),
                        Recursive = true,
                        IncludeItemTypes = new[] { BaseItemKind.BoxSet },
                        Limit = 50,
                        DtoOptions = dtoOptions
                    });
                }).OfType<BoxSet>().ToArray();

                foreach (var boxSet in boxSets)
                {
                    var boxSetUserData = UserDataManager.GetUserData(user, boxSet);

                    if (boxSetUserData?.Played != true)
                        continue;
                    if (boxSetUserData?.LastPlayedDate >= cutoffDate)
                        continue;

                    var children = boxSet.GetChildren(user, true, new InternalItemsQuery(user)).ToList();
                    var movies = children.OfType<Movie>().ToList();

                    if (movies.Count <= 1)
                        continue;

                    results.Add((boxSet, boxSetUserData?.LastPlayedDate));
                }
            }

            // === Process Movies ===
            {
                var playedMovies = LibraryManager.GetItemList(new InternalItemsQuery(user)
                {
                    IncludeItemTypes = new[] { BaseItemKind.Movie },
                    IsPlayed = true,
                    Recursive = true,
                    DtoOptions = new DtoOptions { Fields = Array.Empty<ItemFields>(), EnableImages = false }
                }).OfType<Movie>().ToList();

                foreach (var movie in playedMovies)
                {
                    var userData = UserDataManager.GetUserData(user, movie);
                    if (userData?.LastPlayedDate != null && userData.LastPlayedDate < cutoffDate)
                    {
                        results.Add((movie, userData.LastPlayedDate));
                    }
                }
            }

            // === Process TV Series ===
            // Phase 1: Get candidates from played episodes
            {
                var playedEpisodes = LibraryManager.GetItemList(new InternalItemsQuery(user)
                {
                    IncludeItemTypes = new[] { BaseItemKind.Episode },
                    IsPlayed = true,
                    OrderBy = new[] { (ItemSortBy.DatePlayed, SortOrder.Ascending) },
                    Limit = 1000,
                    IsVirtualItem = false,
                    Recursive = true,
                    DtoOptions = new DtoOptions { Fields = Array.Empty<ItemFields>(), EnableImages = false }
                }).OfType<Episode>().ToList();

                // Group by series and get candidates
                var candidates = playedEpisodes
                    .Where(ep => ep.Series != null)
                    .GroupBy(ep => ep.Series!.Id)
                    .Select(g => new
                    {
                        Series = g.First().Series!,
                        PlayedCount = g.Count(),
                        LastPlayedDate = g.Max(ep =>
                        {
                            var ud = UserDataManager.GetUserData(user, ep);
                            return ud?.LastPlayedDate;
                        })
                    })
                    .Where(x => x.LastPlayedDate < cutoffDate)
                    .Where(x => x.PlayedCount >= 3)
                    .OrderBy(x => x.LastPlayedDate)
                    .Take(50)
                    .ToList();

                // Phase 2: Single batch query for unplayed episodes across all candidates
                var candidateSeriesIds = candidates.Select(c => c.Series.Id).ToArray();

                var unplayedEpisodes = LibraryManager.GetItemList(new InternalItemsQuery(user)
                {
                    IncludeItemTypes = new[] { BaseItemKind.Episode },
                    AncestorIds = candidateSeriesIds,
                    IsPlayed = false,
                    IsVirtualItem = false,
                    DtoOptions = new DtoOptions { Fields = Array.Empty<ItemFields>(), EnableImages = false }
                }).OfType<Episode>().ToList();

                // Get set of series IDs that have unplayed episodes
                var seriesWithUnplayed = unplayedEpisodes
                    .Where(ep => ep.Series != null)
                    .Select(ep => ep.Series!.Id)
                    .ToHashSet();

                // Filter candidates to only fully-played series
                foreach (var candidate in candidates)
                {
                    if (!seriesWithUnplayed.Contains(candidate.Series.Id))
                    {
                        results.Add((candidate.Series, candidate.LastPlayedDate));
                    }

                    if (results.Count >= 16)
                        break;
                }
            }

            // Shuffle results for variety, then take top 16
            var random = new Random();
            var shuffledResults = results
                .OrderBy(x => random.Next())
                .Take(16)
                .ToList();

            // Fetch full items with images
            var itemIds = shuffledResults.Select(r => r.Item.Id).ToArray();
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
                ViewMode = SectionViewMode.Landscape
            };
        }
    }
}
