using System.Collections;
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
using Microsoft.Extensions.Logging;

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

        private IUserViewManager UserViewManager { get; set; }

        public WatchAgainSection(
            ICollectionManager collectionManager,
            IUserManager userManager,
            IDtoService dtoService,
            IUserDataManager userDataManager,
            ITVSeriesManager tvSeriesManager,
            ILibraryManager libraryManager,
            CollectionManagerProxy collectionManagerProxy,
            IUserViewManager userViewManager)
        {
            CollectionManager = collectionManager;
            UserManager = userManager;
            DtoService = dtoService;
            UserDataManager = userDataManager;
            TVSeriesManager = tvSeriesManager;
            LibraryManager = libraryManager;
            CollectionManagerProxy = collectionManagerProxy;
            UserViewManager = userViewManager;
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
            var cutoffDate = DateTime.Now.Subtract(TimeSpan.FromDays(28));

            List<(BaseItem Item, DateTime? LastPlayed)> results = new List<(BaseItem, DateTime?)>();

            // === Process Movies ===
            {
                VirtualFolderInfo[] movieFolders = LibraryManager.GetVirtualFolders()
                    .Where(x => x.CollectionType == CollectionTypeOptions.movies)
                    .FilterToUserPermitted(LibraryManager, user);

                var playedMovies = movieFolders.SelectMany(x =>
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
                VirtualFolderInfo[] tvFolders = LibraryManager.GetVirtualFolders()
                    .Where(x => x.CollectionType == CollectionTypeOptions.tvshows)
                    .FilterToUserPermitted(LibraryManager, user);

                var candidateShows = tvFolders.SelectMany(x =>
                {
                    var item = LibraryManager.GetParentItem(Guid.Parse(x.ItemId), user?.Id);

                    if (item is not Folder folder)
                    {
                        folder = LibraryManager.GetUserRootFolder();
                    }

                    return folder.GetItems(new InternalItemsQuery(user)
                    {
                        IncludeItemTypes = new[]
                        {
                            BaseItemKind.Series
                        },
                        DtoOptions = dtoOptions,
                        EnableTotalRecordCount = false
                    }).Items;
                })
                .DistinctBy(x => x.Id)
                .OfType<Series>()
                .Where(x =>
                {
                    string? seriesKey = x.GetPresentationUniqueKey();

                    InternalItemsQuery query = new InternalItemsQuery(user)
                    {
                        AncestorWithPresentationUniqueKey = null,
                        SeriesPresentationUniqueKey = seriesKey,
                        IncludeItemTypes = new[] { BaseItemKind.Episode },
                        DtoOptions = dtoOptions,
                        IsMissing = false,
                        IsVirtualItem = false,
                        EnableTotalRecordCount = false,
                        IsPlayed = false,
                        MaxPremiereDate = DateTime.Now.Subtract(TimeSpan.FromDays(28)),
                        //Recursive = true,
                        Limit = 1
                    };
                    
                    var earliestUnwatchedEpisode = LibraryManager.GetItemList(query).FirstOrDefault();

                    return earliestUnwatchedEpisode == null;
                })
                .Where(x =>
                {
                    string? seriesKey = x.GetPresentationUniqueKey();

                    InternalItemsQuery query = new InternalItemsQuery(user)
                    {
                        AncestorWithPresentationUniqueKey = null,
                        SeriesPresentationUniqueKey = seriesKey,
                        IncludeItemTypes = new[] { BaseItemKind.Episode },
                        OrderBy = new[] { (ItemSortBy.DateCreated, SortOrder.Descending) },
                        DtoOptions = dtoOptions,
                        IsMissing = false,
                        IsVirtualItem = false,
                        EnableTotalRecordCount = false,
                        //Recursive = true,
                        MaxPremiereDate = DateTime.Now.Subtract(TimeSpan.FromDays(28)),
                        Limit = 4
                    };
                    
                    var episodeCount = LibraryManager.GetItemList(query).Count;

                    return episodeCount >= 3;
                })
                .Select(x =>
                {
                    string? seriesKey = x.GetPresentationUniqueKey();

                    InternalItemsQuery query = new InternalItemsQuery(user)
                    {
                        AncestorWithPresentationUniqueKey = null,
                        SeriesPresentationUniqueKey = seriesKey,
                        IncludeItemTypes = new[] { BaseItemKind.Episode },
                        OrderBy = new[] { (ItemSortBy.DateCreated, SortOrder.Descending) },
                        DtoOptions = dtoOptions,
                        IsMissing = false,
                        IsVirtualItem = false,
                        IsPlayed = true,
                        EnableTotalRecordCount = false,
                        //Recursive = true,
                        MaxPremiereDate = DateTime.Now.Subtract(TimeSpan.FromDays(28)),
                    };
                    
                    var episodes = LibraryManager.GetItemList(query);
                    
                    var lastWatchTime = (episodes.Count == 0 ? null : episodes.Max(y => UserDataManager.GetUserData(user, y)?.LastPlayedDate)) ?? DateTime.MinValue;
                    
                    return new
                    {
                        Series = x,
                        LastPlayedDate = lastWatchTime
                    };
                })
                .Where(x => x.LastPlayedDate < cutoffDate);
                
                candidateShows = candidateShows.OrderBy(x => x.LastPlayedDate);
                candidateShows = candidateShows.Take(50);
                
                var random2 = new Random();
                candidateShows = candidateShows
                    .OrderBy(x => random2.Next())
                .ToList();

                var selectedSeriesCandidates = candidateShows.Take((int)Math.Max(results.Count / 2.0f, 16.0f));
                
                // Filter candidates to only fully-played series
                foreach (var candidate in selectedSeriesCandidates)
                {
                    BaseItem? firstEpisode = LibraryManager.GetItemList(new InternalItemsQuery(user)
                    {
                        AncestorWithPresentationUniqueKey = null,
                        SeriesPresentationUniqueKey = candidate.Series.GetPresentationUniqueKey(),
                        IncludeItemTypes = new[] { BaseItemKind.Episode },
                        OrderBy = new[] { (ItemSortBy.PremiereDate, SortOrder.Ascending) },
                        DtoOptions = dtoOptions,
                        IsMissing = false,
                        IsVirtualItem = false,
                        EnableTotalRecordCount = false,
                        Limit = 1
                    }).FirstOrDefault();
                    
                    results.Add((firstEpisode ?? candidate.Series, candidate.LastPlayedDate)); // TODO: Convert to first episode
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
                ViewMode = SectionViewMode.Landscape,
                PluginConfigurationOptions = (this as IHomeScreenSection).GetPluginConfigurationOptions().ToArray()
            };
        }
    }
}
