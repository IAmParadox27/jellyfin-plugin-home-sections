using System.Collections.Concurrent;
using System.Diagnostics;
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
using MediaBrowser.Model.Library;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

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
        
        UserViewQuery query = new UserViewQuery
        {
            User = user,
            IncludeHidden = false
        };

        VirtualFolderInfo[] folders = m_libraryManager.GetVirtualFolders()
            .Where(x => x.CollectionType == CollectionTypeOptions.movies)
            .FilterToUserPermitted(m_libraryManager, user);
        
        DtoOptions? dtoOptions = new DtoOptions 
        { 
            Fields = new[] 
            { 
                ItemFields.PrimaryImageAspectRatio, 
                ItemFields.MediaSourceCount
            }
        };
        
        IEnumerable<BaseItem>? likedOrFavoritedMovies = folders.SelectMany(x =>
        {
            InternalItemsQuery? favoriteOrLikedQuery = new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[]
                {
                    BaseItemKind.Movie
                },
                Limit = null,
                Recursive = true,
                IsFavoriteOrLiked = true,
                User = user,
                ParentId = Guid.Parse(x.ItemId ?? Guid.Empty.ToString()),
            };

            return m_libraryManager.GetItemList(favoriteOrLikedQuery);
        });

        var scoredGenres = likedOrFavoritedMovies.OfType<Movie>().SelectMany(x =>
        {
            return x.Genres.Select(genre => new
            {
                Genre = genre,
                Score = likedOrFavouriteScore
            });
        }).GroupBy(x => x.Genre).Select(x => new
        {
            Genre = x.Key,
            Score = x.Sum(y => y.Score)
        }).ToArray();
        
        var test = folders.SelectMany(x =>
        {
            InternalItemsQuery? recentlyWatchedQuery = new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[]
                {
                    BaseItemKind.Movie
                },
                OrderBy = new[] { (ItemSortBy.DatePlayed, SortOrder.Descending) },
                Limit = 7,
                ParentId = Guid.Parse(x.ItemId ?? Guid.Empty.ToString()),
                Recursive = true,
                IsPlayed = true,
                DtoOptions = dtoOptions
            };

            return m_libraryManager.GetItemList(recentlyWatchedQuery);
        });
        
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
        
        var allGenres = folders.SelectMany(x => m_libraryManager.GetGenres(new InternalItemsQuery()
        {
            IncludeItemTypes = new[]
            {
                BaseItemKind.Movie
            },
            User = user,
            EnableTotalRecordCount = false,
            Recursive = true,
            ParentId = Guid.Parse(x.ItemId ?? Guid.Empty.ToString()),
        }).Items.Where(y => y.ItemCounts.MovieCount > 0))
            .DistinctBy(x => x.Item.Id)
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