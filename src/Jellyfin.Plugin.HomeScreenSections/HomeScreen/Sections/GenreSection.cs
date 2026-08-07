using Jellyfin.Extensions;
using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Helpers;
using Jellyfin.Plugin.HomeScreenSections.JellyfinVersionSpecific;
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

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections;

public class GenreSection : IHomeScreenSection
{
    public string? Section => "Genre";
    public string? DisplayText { get; set; } = "Genre";
    public int? Limit => 5;
    public string? Route => null;
    public string? AdditionalData { get; set; }

    /// <summary>
    /// Genre item used as the section title link target.
    /// </summary>
    public object? OriginalPayload { get; set; }

    public TranslationMetadata? TranslationMetadata { get; private set; }

    private readonly IUserManager m_userManager;
    private readonly ILibraryManager m_libraryManager;
    private readonly CollectionManagerProxy m_collectionManagerProxy;
    private readonly IUserDataManager m_userDataManager;
    private readonly IDtoService m_dtoService;

    private readonly IUserViewManager m_userViewManager;

    public GenreSection(IUserManager userManager, ILibraryManager libraryManager, CollectionManagerProxy collectionManagerProxy,
        IUserDataManager userDataManager, IDtoService dtoService, IUserViewManager userViewManager)
    {
        m_userManager = userManager;
        m_libraryManager = libraryManager;
        m_collectionManagerProxy = collectionManagerProxy;
        m_userDataManager = userDataManager;
        m_dtoService = dtoService;
        m_userViewManager = userViewManager;
    }

    public QueryResult<BaseItemDto> GetResults(HomeScreenSectionPayload payload, IQueryCollection queryCollection)
    {
        if (payload.AdditionalData == null)
        {
            return new QueryResult<BaseItemDto>();
        }

        User? user = m_userManager.GetUserById(payload.UserId);
        Genre genre = m_libraryManager.GetGenre(payload.AdditionalData);
        DtoOptions dtoOptions = CreateDtoOptions();

        var config = HomeScreenSectionsPlugin.Instance?.Configuration;
        var sectionSettings = config?.SectionSettings.FirstOrDefault(x => string.Equals(x.SectionId, Section, StringComparison.Ordinal));
        // If HideWatchedItems is enabled for this section, set isPlayed to false to hide watched items; otherwise, include all.
        bool? isPlayed = sectionSettings?.HideWatchedItems == true ? false : null;

        List<BaseItem> movies = GetMoviesForGenre(user, genre, dtoOptions, isPlayed);
        movies.Shuffle();

        return new QueryResult<BaseItemDto>(m_dtoService.GetBaseItemDtos(movies.Take(16).ToArray(), dtoOptions, user));
    }

    public IEnumerable<IHomeScreenSection> CreateInstances(Guid? userId, int instanceCount)
    {
        User user = (userId is null || userId.Value.Equals(default)
            ? null
            : m_userManager.GetUserById(userId.Value))
            ?? throw new InvalidOperationException("User not found for genre section.");

        // Do the heavy lifting before we add into the cache
        (string Genre, int Score)[] userGenreScores = GetGenresForUser(user);

        if (userGenreScores.Length == 0)
        {
            yield break;
        }

        DtoOptions linkDtoOptions = new DtoOptions
        {
            Fields = new List<ItemFields>
            {
                ItemFields.PrimaryImageAspectRatio,
                ItemFields.DisplayPreferencesId
            }
        };

        foreach (string selectedGenre in PickWeightedGenres(userGenreScores, instanceCount))
        {
            Genre? genreItem = m_libraryManager.GetGenre(selectedGenre);
            yield return new GenreSection(m_userManager, m_libraryManager, m_collectionManagerProxy, m_userDataManager, m_dtoService, m_userViewManager)
            {
                AdditionalData = selectedGenre,
                DisplayText = $"{selectedGenre} Movies",
                OriginalPayload = genreItem != null
                    ? m_dtoService.GetBaseItemDto(genreItem, linkDtoOptions, user)
                    : null,
                TranslationMetadata = new TranslationMetadata()
                {
                    Type = TranslationType.Pattern,
                    AdditionalContent = selectedGenre,
                    TranslateAdditionalContent = true
                }
            };
        }
    }

    private (string Genre, int Score)[] GetGenresForUser(User user)
    {
        const int likedOrFavouriteScore = 125;
        const int recentlyWatchedScore = 50;
        const int scorePerPlay = 1;

        Guid[] folderIds = GetMovieFolderIds(user);
        if (folderIds.Length == 0)
        {
            return Array.Empty<(string, int)>();
        }

        List<Movie> allPlayedMovies = GetPlayedMovies(user, folderIds);
        Dictionary<Guid, UserItemData?> userDataCache = BuildUserDataCache(user, allPlayedMovies);

        Dictionary<string, int> playCountByGenre = BuildPlayCountScores(allPlayedMovies, userDataCache, scorePerPlay);
        Dictionary<string, int> recentlyWatchedByGenre = BuildRecentlyWatchedScores(allPlayedMovies, userDataCache, recentlyWatchedScore);
        Dictionary<string, int> likedByGenre = BuildLikedScores(user, folderIds, likedOrFavouriteScore);

        return CombineGenreScores(playCountByGenre, recentlyWatchedByGenre, likedByGenre);
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
            AllowHideWatched = true
        };
    }

    private static DtoOptions CreateDtoOptions()
    {
        return new DtoOptions
        {
            Fields = new[]
            {
                ItemFields.PrimaryImageAspectRatio,
                ItemFields.MediaSourceCount
            }
        };
    }

    private List<BaseItem> GetMoviesForGenre(User? user, Genre genre, DtoOptions dtoOptions, bool? isPlayed)
    {
        VirtualFolderInfo[] folders = m_libraryManager.GetVirtualFolders()
            .Where(x => x.CollectionType == CollectionTypeOptions.movies)
            .FilterToUserPermitted(m_libraryManager, user);

        return folders.SelectMany(x =>
        {
            var item = m_libraryManager.GetParentItem(Guid.Parse(x.ItemId), user?.Id);

            if (item is not Folder folder)
            {
                folder = m_libraryManager.GetUserRootFolder();
            }

            return folder.GetItems(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[]
                {
                    BaseItemKind.Movie
                },
                OrderBy = new[] { (ItemSortBy.Random, SortOrder.Descending) },
                ParentId = Guid.Parse(x.ItemId ?? Guid.Empty.ToString()),
                Recursive = true,
                Limit = 24,
                IsPlayed = isPlayed,
                DtoOptions = dtoOptions,
                Genres = new List<string> { genre.Name }
            }).Items;
        }).GroupBy(x => x.Id).Select(x => x.First()).ToList();
    }

    private static IEnumerable<string> PickWeightedGenres((string Genre, int Score)[] userGenreScores, int instanceCount)
    {
        Random rnd = new Random();
        List<string> pickedGenres = new List<string>();
        (string Genre, int Score)[] availableGenres = userGenreScores.ToArray();

        while (pickedGenres.Count < instanceCount && availableGenres.Length > 0)
        {
            availableGenres = userGenreScores.Where(x => !pickedGenres.Contains(x.Genre)).ToArray();
            if (availableGenres.Length == 0)
            {
                break;
            }

            string? selectedGenre = SelectGenreByWeight(userGenreScores, availableGenres, rnd);
            if (selectedGenre != null)
            {
                pickedGenres.Add(selectedGenre);
                yield return selectedGenre;
            }
        }
    }

    private static string? SelectGenreByWeight(
        (string Genre, int Score)[] userGenreScores,
        (string Genre, int Score)[] availableGenres,
        Random rnd)
    {
        string? selectedGenre = null;
        int totalScore = availableGenres.Sum(x => x.Score);
        int randomScore;

        if (totalScore > 0)
        {
            randomScore = rnd.Next(0, totalScore);
        }
        else
        {
            randomScore = rnd.Next(0, userGenreScores.Length);
            selectedGenre = userGenreScores[randomScore].Genre;
        }

        if (totalScore > 0)
        {
            foreach ((string Genre, int Score) userGenre in userGenreScores)
            {
                randomScore -= userGenre.Score;

                if (randomScore < 0)
                {
                    selectedGenre = userGenre.Genre;
                    break;
                }
            }

            if (selectedGenre == null)
            {
                selectedGenre = userGenreScores.Last().Genre;
            }
        }

        return selectedGenre;
    }

    private Guid[] GetMovieFolderIds(User user)
    {
        VirtualFolderInfo[] folders = m_libraryManager.GetVirtualFolders()
            .Where(x => x.CollectionType == CollectionTypeOptions.movies)
            .FilterToUserPermitted(m_libraryManager, user);

        return folders
            .Select(x => Guid.Parse(x.ItemId ?? Guid.Empty.ToString()))
            .Where(x => x != Guid.Empty)
            .ToArray();
    }

    private List<Movie> GetPlayedMovies(User user, Guid[] folderIds)
    {
        return folderIds.SelectMany(folderId =>
        {
            var item = m_libraryManager.GetParentItem(folderId, user?.Id);

            if (item is not Folder folder)
            {
                folder = m_libraryManager.GetUserRootFolder();
            }

            return folder.GetItems(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie },
                Recursive = true,
                IsPlayed = true,
                ParentId = folderId,
            }).Items;
        }).OfType<Movie>().ToList();
    }

    private Dictionary<Guid, UserItemData?> BuildUserDataCache(User user, List<Movie> allPlayedMovies)
    {
        var userDataCache = new Dictionary<Guid, UserItemData?>();
        foreach (var movie in allPlayedMovies)
        {
            userDataCache[movie.Id] = m_userDataManager.GetUserData(user, movie);
        }

        return userDataCache;
    }

    private static Dictionary<string, int> BuildPlayCountScores(
        List<Movie> allPlayedMovies,
        Dictionary<Guid, UserItemData?> userDataCache,
        int scorePerPlay)
    {
        return allPlayedMovies
            .SelectMany(movie => movie.Genres.Select(genre => new
            {
                Genre = genre,
                PlayCount = userDataCache.TryGetValue(movie.Id, out var ud) ? ud?.PlayCount ?? 0 : 0
            }))
            .GroupBy(x => x.Genre, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(x => x.PlayCount) * scorePerPlay,
                StringComparer.Ordinal
            );
    }

    private static Dictionary<string, int> BuildRecentlyWatchedScores(
        List<Movie> allPlayedMovies,
        Dictionary<Guid, UserItemData?> userDataCache,
        int recentlyWatchedScore)
    {
        var cutoffDate = DateTime.Today.Subtract(TimeSpan.FromDays(14));
        return allPlayedMovies
            .Where(movie =>
            {
                if (userDataCache.TryGetValue(movie.Id, out var ud) && ud != null)
                {
                    return (ud.LastPlayedDate ?? DateTime.MinValue) > cutoffDate;
                }
                return false;
            })
            .SelectMany(movie => movie.Genres)
            .GroupBy(genre => genre, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Count() * recentlyWatchedScore,
                StringComparer.Ordinal
            );
    }

    private Dictionary<string, int> BuildLikedScores(User user, Guid[] folderIds, int likedOrFavouriteScore)
    {
        List<Movie> likedOrFavoritedMovies = folderIds.SelectMany(folderId =>
        {
            var item = m_libraryManager.GetParentItem(folderId, user?.Id);

            if (item is not Folder folder)
            {
                folder = m_libraryManager.GetUserRootFolder();
            }

            return folder.GetItems(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie },
                Recursive = true,
                IsFavoriteOrLiked = true,
                User = user,
                ParentId = folderId,
            }).Items;
        }).OfType<Movie>().ToList();

        return likedOrFavoritedMovies
            .SelectMany(movie => movie.Genres)
            .GroupBy(genre => genre, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Count() * likedOrFavouriteScore,
                StringComparer.Ordinal
            );
    }

    private static (string Genre, int Score)[] CombineGenreScores(
        Dictionary<string, int> playCountByGenre,
        Dictionary<string, int> recentlyWatchedByGenre,
        Dictionary<string, int> likedByGenre)
    {
        var allGenreNames = playCountByGenre.Keys
            .Concat(recentlyWatchedByGenre.Keys)
            .Concat(likedByGenre.Keys)
            .Distinct(StringComparer.Ordinal);

        return allGenreNames.Select(genre =>
        {
            int score = 0;
            if (playCountByGenre.TryGetValue(genre, out var playScore))
            {
                score += playScore;
            }
            if (recentlyWatchedByGenre.TryGetValue(genre, out var recentScore))
            {
                score += recentScore;
            }
            if (likedByGenre.TryGetValue(genre, out var likedScore))
            {
                score += likedScore;
            }

            return (Genre: genre, Score: score);
        }).ToArray();
    }
}
