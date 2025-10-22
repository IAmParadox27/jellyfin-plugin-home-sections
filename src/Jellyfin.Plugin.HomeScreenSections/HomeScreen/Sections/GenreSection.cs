using System.Collections.Concurrent;
using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.JellyfinVersionSpecific;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

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
    
    private ConcurrentDictionary<Guid, (string Genre, int Score)[]> m_userGenreCache = new ConcurrentDictionary<Guid, (string Genre, int Score)[]>();
    private ConcurrentDictionary<Guid, bool> m_usersWithOngoingSearches = new ConcurrentDictionary<Guid, bool>();
    
    public GenreSection(IUserManager userManager, ILibraryManager libraryManager, CollectionManagerProxy collectionManagerProxy,
        IUserDataManager userDataManager, IDtoService dtoService)
    {
        m_userManager = userManager;
        m_libraryManager = libraryManager;
        m_collectionManagerProxy = collectionManagerProxy;
        m_userDataManager = userDataManager;
        m_dtoService = dtoService;
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
        
        InternalItemsQuery? genreMovies = new InternalItemsQuery(user)
        {
            IncludeItemTypes = new[]
            {
                BaseItemKind.Movie
            },
            OrderBy = new[] { (ItemSortBy.Random, SortOrder.Descending) },
            Limit = 16,
            ParentId = Guid.Empty,
            Recursive = true,
            DtoOptions = dtoOptions,
            Genres = new List<string> { genre.Name }
        };

        return new QueryResult<BaseItemDto>(m_dtoService.GetBaseItemDtos(m_libraryManager.GetItemList(genreMovies), dtoOptions, user));
    }

    public IHomeScreenSection CreateInstance(Guid? userId, IEnumerable<IHomeScreenSection>? otherInstances = null)
    {
        User? user = userId is null || userId.Value.Equals(default)
            ? null
            : m_userManager.GetUserById(userId.Value);

        if (user == null)
        {
            throw new Exception();
        }
        
        if ((otherInstances?.Count() ?? 0) == 0)
        {
            // Do the heavy lifting before we add into the cache
            (string Genre, int Score)[] genresToCache = GetGenresForUser(user);
            
            // If this is the "first" for this request, lets do the calculation for all of the genres and cache and ordered list to retrieve from
            m_userGenreCache.TryRemove(userId!.Value, out _);
            
            m_userGenreCache.TryAdd(userId!.Value, genresToCache);
        }

        var userGenreScores = m_userGenreCache[userId!.Value]
            .Where(x => !otherInstances.Any(y => y.AdditionalData == x.Genre))
            .ToArray();
        
        int totalScore = userGenreScores.Sum(x => x.Score);
        Random rnd = new Random();

        string? selectedGenre = null;
        bool foundNew = false;
        do
        {
            int randomScore = 0;
            if (totalScore != 0)
            {
                randomScore = rnd.Next(0, totalScore);
            }

            if (totalScore == 0)
            {
                randomScore = rnd.Next(0, userGenreScores.Length);
                selectedGenre = userGenreScores[randomScore].Genre;
            }
            else
            {
                foreach (var userGenre in userGenreScores)
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

            if (!otherInstances.Any(x => x.AdditionalData == selectedGenre))
            {
                foundNew = true;
            }
        } while (!foundNew);

        GenreSection section = new GenreSection(m_userManager, m_libraryManager, m_collectionManagerProxy, m_userDataManager, m_dtoService)
        {
            AdditionalData = selectedGenre,
            DisplayText = $"{selectedGenre} Movies"
        };
        
        return section;
    }

    private (string Genre, int Score)[] GetGenresForUser(User user)
    {
        if (m_usersWithOngoingSearches.ContainsKey(user.Id))
        {
            while (m_usersWithOngoingSearches.ContainsKey(user.Id))
            {
                // Pause this thread until its done with the current search
                Thread.Sleep(100);
            }

            if (m_userGenreCache.TryGetValue(user.Id, out (string Genre, int Score)[]? cachedGenres))
            {
                return cachedGenres;
            }
        }
        
        m_usersWithOngoingSearches.TryAdd(user.Id, true);
        
        int likedScore = 100;
        int favouriteScore = 150;
        int recentlyWatchedScore = 50;
        int scorePerPlay = 1;
        
        DtoOptions? dtoOptions = new DtoOptions 
        { 
            Fields = new[] 
            { 
                ItemFields.PrimaryImageAspectRatio, 
                ItemFields.MediaSourceCount
            }
        };

        InternalItemsQuery? favoriteOrLikedQuery = new InternalItemsQuery(user)
        {
            IncludeItemTypes = new[]
            {
                BaseItemKind.Movie
            },
            Limit = null,
            ParentId = Guid.Empty,
            Recursive = true,
            IsFavoriteOrLiked = true,
            DtoOptions = dtoOptions,
        };

        IEnumerable<BaseItem>? likedOrFavoritedMovies = m_libraryManager.GetItemList(favoriteOrLikedQuery);

        var scoredGenres = likedOrFavoritedMovies.OfType<Movie>().SelectMany(x =>
        {
            int score = 0;
            var userData = m_userDataManager.GetUserData(user, x);

            score += userData.IsFavorite ? favouriteScore : 0;
            score += (userData.Likes ?? false) ? likedScore : 0;

            return x.Genres.Select(genre => new
            {
                Genre = genre,
                Score = score
            });
        }).GroupBy(x => x.Genre).Select(x => new
        {
            Genre = x.Key,
            Score = x.Sum(y => y.Score)
        }).ToArray();
        
        InternalItemsQuery? recentlyWatchedQuery = new InternalItemsQuery(user)
        {
            IncludeItemTypes = new[]
            {
                BaseItemKind.Movie
            },
            OrderBy = new[] { (ItemSortBy.DatePlayed, SortOrder.Descending), (ItemSortBy.Random, SortOrder.Descending) },
            Limit = 7,
            ParentId = Guid.Empty,
            Recursive = true,
            IsPlayed = true,
            DtoOptions = dtoOptions
        };

        var test = m_libraryManager.GetItemList(recentlyWatchedQuery);
        var recentlyPlayedMovies = test.SelectMany(x =>
        {
            int score = 0;
            var userData = m_userDataManager.GetUserData(user, x);

            if ((userData.LastPlayedDate ?? DateTime.MinValue) > DateTime.Today.Subtract(TimeSpan.FromDays(14)))
            {
                score += recentlyWatchedScore;
            }

            return m_libraryManager.GetGenres(new InternalItemsQuery()
            {
                ItemIds = new[] { x.Id }
            }).Items.Select(genre => new
            {
                Genre = genre.Item.Name,
                Score = score
            });
        }).GroupBy(x => x.Genre).Select(x => new
        {
            Genre = x.Key,
            Score = x.Sum(y => y.Score)
        }).ToArray();

        var allGenres = m_libraryManager.GetGenres(new InternalItemsQuery()
        {
            IncludeItemTypes = new[]
            {
                BaseItemKind.Movie
            },
            User = user
        }).Items.Where(x => x.ItemCounts.MovieCount > 0)
            .Select(x =>
            {
                var items = m_libraryManager.GetItemList(new InternalItemsQuery()
                {
                    IncludeItemTypes = new[]
                    {
                        BaseItemKind.Movie
                    },
                    GenreIds = new[] { x.Item.Id }
                });

                int playCount = items.Sum(y =>
                {
                    var userData = m_userDataManager.GetUserData(user, y);

                    return userData.PlayCount;
                });
                
                int score = playCount * scorePerPlay;
                return new
                {
                    Genre = (x.Item as Genre)?.Name, 
                    Score = score
                };
            }).ToArray();
        
        scoredGenres = scoredGenres
            .Concat(recentlyPlayedMovies)
            .Concat(allGenres)
            .GroupBy(x => x.Genre)
            .Select(x => new { Genre = x.Key, Score = x.Sum(y => y.Score) })
            .ToArray();

        var returnValue = scoredGenres.Select(x => (x.Genre, x.Score)).ToArray();
        
        m_usersWithOngoingSearches.TryRemove(user.Id, out _);
        
        return returnValue;
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