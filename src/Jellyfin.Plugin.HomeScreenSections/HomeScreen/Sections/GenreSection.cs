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
    public object? OriginalPayload => null;

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

        DtoOptions? dtoOptions = new DtoOptions
        {
            Fields = new[]
            {
                ItemFields.PrimaryImageAspectRatio,
                ItemFields.MediaSourceCount
            }
        };

        VirtualFolderInfo[] folders = m_libraryManager.GetVirtualFolders()
            .Where(x => x.CollectionType == CollectionTypeOptions.movies)
            .FilterToUserPermitted(m_libraryManager, user);

        var movies = folders.SelectMany(x =>
        {
            InternalItemsQuery? genreMovies = new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[]
                {
                    BaseItemKind.Movie
                },
                OrderBy = new[] { (ItemSortBy.Random, SortOrder.Descending) },
                ParentId = Guid.Parse(x.ItemId ?? Guid.Empty.ToString()),
                Recursive = true,
                Limit = 24,
                DtoOptions = dtoOptions,
                Genres = new List<string> { genre.Name }
            };

            return m_libraryManager.GetItemList(genreMovies);
        }).GroupBy(x => x.Id).Select(x => x.First()).ToList();

        movies.Shuffle();

        return new QueryResult<BaseItemDto>(m_dtoService.GetBaseItemDtos(movies.Take(16).ToArray(), dtoOptions, user));
    }

    public IEnumerable<IHomeScreenSection> CreateInstances(Guid? userId, int instanceCount)
    {
        User? user = userId is null || userId.Value.Equals(default)
            ? null
            : m_userManager.GetUserById(userId.Value);

        if (user == null)
        {
            throw new Exception();
        }

        // Do the heavy lifting before we add into the cache
        (string Genre, int Score)[] userGenreScores = GetGenresForUser(user);

        if (userGenreScores.Length == 0)
        {
            yield break;
        }

        Random rnd = new Random();

        List<string> pickedGenres = new List<string>();

        (string Genre, int Score)[] availableGenres = userGenreScores.ToArray();
        while (pickedGenres.Count < instanceCount && availableGenres.Length > 0)
        {
            string? selectedGenre = null;

            availableGenres = userGenreScores.Where(x => !pickedGenres.Contains(x.Genre)).ToArray();

            if (availableGenres.Length == 0)
            {
                break;
            }

            int totalScore = availableGenres.Sum(x => x.Score);

            int randomScore = 0;
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

            if (selectedGenre != null)
            {
                pickedGenres.Add(selectedGenre);

                yield return new GenreSection(m_userManager, m_libraryManager, m_collectionManagerProxy, m_userDataManager, m_dtoService, m_userViewManager)
                {
                    AdditionalData = selectedGenre,
                    DisplayText = $"{selectedGenre} Movies"
                };
            }
        }
    }

    private (string Genre, int Score)[] GetGenresForUser(User user)
    {
        int likedOrFavouriteScore = 125;
        int recentlyWatchedScore = 50;
        int scorePerPlay = 1;

        VirtualFolderInfo[] folders = m_libraryManager.GetVirtualFolders()
            .Where(x => x.CollectionType == CollectionTypeOptions.movies)
            .FilterToUserPermitted(m_libraryManager, user);

        // Build a list of parent folder IDs for querying
        Guid[] folderIds = folders
            .Select(x => Guid.Parse(x.ItemId ?? Guid.Empty.ToString()))
            .Where(x => x != Guid.Empty)
            .ToArray();

        if (folderIds.Length == 0)
        {
            return Array.Empty<(string, int)>();
        }

        // === QUERY 1: Get all played movies in a single query ===
        // This replaces the N+1 pattern of querying per-genre then per-movie
        var allPlayedMovies = folderIds.SelectMany(folderId =>
            m_libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie },
                Recursive = true,
                IsPlayed = true,
                ParentId = folderId,
            })).OfType<Movie>().ToList();

        // Fetch user data for all played movies at once, then cache in a dictionary
        var userDataCache = new Dictionary<Guid, UserItemData?>();
        foreach (var movie in allPlayedMovies)
        {
            userDataCache[movie.Id] = m_userDataManager.GetUserData(user, movie);
        }

        // === Calculate play count scores per genre from cached data ===
        var playCountByGenre = allPlayedMovies
            .SelectMany(movie => movie.Genres.Select(genre => new
            {
                Genre = genre,
                PlayCount = userDataCache.TryGetValue(movie.Id, out var ud) ? ud?.PlayCount ?? 0 : 0
            }))
            .GroupBy(x => x.Genre)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(x => x.PlayCount) * scorePerPlay
            );

        // === Calculate recently watched scores (last 14 days) ===
        var cutoffDate = DateTime.Today.Subtract(TimeSpan.FromDays(14));
        var recentlyWatchedByGenre = allPlayedMovies
            .Where(movie =>
            {
                if (userDataCache.TryGetValue(movie.Id, out var ud) && ud != null)
                {
                    return (ud.LastPlayedDate ?? DateTime.MinValue) > cutoffDate;
                }
                return false;
            })
            .SelectMany(movie => movie.Genres)
            .GroupBy(genre => genre)
            .ToDictionary(
                g => g.Key,
                g => g.Count() * recentlyWatchedScore
            );

        // === QUERY 2: Get favorited/liked movies ===
        var likedOrFavoritedMovies = folderIds.SelectMany(folderId =>
            m_libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie },
                Recursive = true,
                IsFavoriteOrLiked = true,
                User = user,
                ParentId = folderId,
            })).OfType<Movie>().ToList();

        var likedByGenre = likedOrFavoritedMovies
            .SelectMany(movie => movie.Genres)
            .GroupBy(genre => genre)
            .ToDictionary(
                g => g.Key,
                g => g.Count() * likedOrFavouriteScore
            );

        // === Combine all genre scores ===
        var allGenreNames = playCountByGenre.Keys
            .Concat(recentlyWatchedByGenre.Keys)
            .Concat(likedByGenre.Keys)
            .Distinct();

        var result = allGenreNames.Select(genre =>
        {
            int score = 0;
            if (playCountByGenre.TryGetValue(genre, out var playScore))
                score += playScore;
            if (recentlyWatchedByGenre.TryGetValue(genre, out var recentScore))
                score += recentScore;
            if (likedByGenre.TryGetValue(genre, out var likedScore))
                score += likedScore;

            return (Genre: genre, Score: score);
        }).ToArray();

        return result;
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
}
