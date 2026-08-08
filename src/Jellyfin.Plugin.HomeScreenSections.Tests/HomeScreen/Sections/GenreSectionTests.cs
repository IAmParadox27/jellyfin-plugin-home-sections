using System.Reflection;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections;
using Jellyfin.Plugin.HomeScreenSections.JellyfinVersionSpecific;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using Jellyfin.Plugin.HomeScreenSections.Tests.Support;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Moq;

namespace Jellyfin.Plugin.HomeScreenSections.Tests.HomeScreen.Sections;

public class GenreSectionTests
{
    private readonly Mock<IUserManager> m_userManager = new();
    private readonly Mock<ILibraryManager> m_libraryManager = new();
    private readonly Mock<ICollectionManager> m_collectionManager = new();
    private readonly Mock<IUserDataManager> m_userDataManager = new();
    private readonly Mock<IDtoService> m_dtoService = new();
    private readonly Mock<IUserViewManager> m_userViewManager = new();

    private GenreSection MakeSection()
    {
        return new GenreSection(
            m_userManager.Object,
            m_libraryManager.Object,
            new CollectionManagerProxy(m_collectionManager.Object),
            m_userDataManager.Object,
            m_dtoService.Object,
            m_userViewManager.Object);
    }

    [Fact]
    public void Section_metadata_exposes_genre_defaults()
    {
        GenreSection section = MakeSection();

        Assert.Equal("Genre", section.Section);
        Assert.Equal("Genre", section.DisplayText);
        Assert.Equal(5, section.Limit);
        Assert.Null(section.Route);
    }

    [Fact]
    public void GetInfo_reports_landscape_and_hide_watched_support()
    {
        GenreSection section = MakeSection();

        HomeScreenSectionInfo info = section.GetInfo();

        Assert.Equal(SectionViewMode.Landscape, info.ViewMode);
        Assert.True(info.AllowHideWatched);
        Assert.Equal(5, info.Limit);
    }

    [Fact]
    public void GetResults_without_additional_data_returns_empty()
    {
        GenreSection section = MakeSection();

        QueryResult<BaseItemDto> result = section.GetResults(new HomeScreenSectionPayload(), new FakeQueryCollection());

        Assert.Empty(result.Items);
    }

    [Fact]
    public void CreateInstances_throws_when_user_missing()
    {
        GenreSection section = MakeSection();

        Assert.Throws<InvalidOperationException>(() => section.CreateInstances(null, 1).ToList());
        Assert.Throws<InvalidOperationException>(() => section.CreateInstances(Guid.Empty, 1).ToList());
    }

    [Fact]
    public void CreateInstances_yields_nothing_when_user_has_no_movie_libraries()
    {
        Guid userId = Guid.NewGuid();
        User user = new("GenreViewer", "AuthProvider", "PasswordResetProvider");
        m_userManager
            .Setup(manager => manager.GetUserById(userId))
            .Returns(user);
        m_libraryManager
            .Setup(manager => manager.GetVirtualFolders())
            .Returns([]);

        GenreSection section = MakeSection();

        Assert.Empty(section.CreateInstances(userId, 3));
    }

    [Fact]
    public void CombineGenreScores_unions_keys_and_sums_scores()
    {
        Dictionary<string, int> playCount = new(StringComparer.Ordinal) { ["Action"] = 10, ["Drama"] = 5 };
        Dictionary<string, int> recent = new(StringComparer.Ordinal) { ["Action"] = 4, ["Comedy"] = 2 };
        Dictionary<string, int> liked = new(StringComparer.Ordinal) { ["Drama"] = 1 };

        (string Genre, int Score)[] combined = ((string Genre, int Score)[])InvokeGenreStatic("CombineGenreScores", playCount, recent, liked)!;

        Dictionary<string, int> byGenre = combined.ToDictionary(x => x.Genre, x => x.Score, StringComparer.Ordinal);
        Assert.Equal(14, byGenre["Action"]);
        Assert.Equal(6, byGenre["Drama"]);
        Assert.Equal(2, byGenre["Comedy"]);
        Assert.Equal(3, combined.Length);
    }

    [Fact]
    public void BuildPlayCountScores_sums_play_counts_per_genre()
    {
        Movie actionMovie = new Movie { Id = Guid.NewGuid(), Genres = ["Action", "Thriller"] };
        Movie dramaMovie = new Movie { Id = Guid.NewGuid(), Genres = ["Drama"] };

        Dictionary<Guid, UserItemData?> userDataCache = new()
        {
            [actionMovie.Id] = new UserItemData { Key = "a", PlayCount = 3 },
            [dramaMovie.Id] = new UserItemData { Key = "d", PlayCount = 2 }
        };

        Dictionary<string, int> scores = (Dictionary<string, int>)InvokeGenreStatic(
            "BuildPlayCountScores", new List<Movie> { actionMovie, dramaMovie }, userDataCache, 10)!;

        Assert.Equal(30, scores["Action"]);
        Assert.Equal(30, scores["Thriller"]);
        Assert.Equal(20, scores["Drama"]);
    }

    [Fact]
    public void BuildRecentlyWatchedScores_only_counts_movies_played_within_cutoff()
    {
        Movie recent = new Movie { Id = Guid.NewGuid(), Genres = ["Sci-Fi"] };
        Movie old = new Movie { Id = Guid.NewGuid(), Genres = ["Horror"] };
        Movie unknown = new Movie { Id = Guid.NewGuid(), Genres = ["Mystery"] };

        Dictionary<Guid, UserItemData?> userDataCache = new()
        {
            [recent.Id] = new UserItemData { Key = "r", LastPlayedDate = DateTime.Now },
            [old.Id] = new UserItemData { Key = "o", LastPlayedDate = DateTime.Today.Subtract(TimeSpan.FromDays(30)) },
            [unknown.Id] = null
        };

        Dictionary<string, int> scores = (Dictionary<string, int>)InvokeGenreStatic(
            "BuildRecentlyWatchedScores", new List<Movie> { recent, old, unknown }, userDataCache, 5)!;

        Assert.Equal(5, scores["Sci-Fi"]);
        Assert.False(scores.ContainsKey("Horror"));
        Assert.False(scores.ContainsKey("Mystery"));
    }

    [Fact]
    public void SelectGenreByWeight_returns_the_only_scored_genre()
    {
        (string Genre, int Score)[] scores = [("Action", 100)];
        (string Genre, int Score)[] available = [("Action", 100)];

        string? selected = (string?)InvokeGenreStatic("SelectGenreByWeight", scores, available, new Random(1));

        Assert.Equal("Action", selected);
    }

    [Fact]
    public void SelectGenreByWeight_falls_back_to_uniform_pick_when_all_scores_zero()
    {
        (string Genre, int Score)[] scores = [("A", 0), ("B", 0)];
        (string Genre, int Score)[] available = [("A", 0), ("B", 0)];

        string? selected = (string?)InvokeGenreStatic("SelectGenreByWeight", scores, available, new Random(42));

        Assert.Contains(selected, s_twoGenres, StringComparer.Ordinal);
    }

    [Fact]
    public void PickWeightedGenres_yields_genres_from_the_input_set()
    {
        (string Genre, int Score)[] scores = [("A", 10), ("B", 20), ("C", 30)];

        List<string> picked = InvokePickWeightedGenres(scores, 2);

        // The picker is seeded from the clock and may repeat genres, so assert the stable
        // invariants: at least one pick, never more than requested, and only known genres.
        Assert.InRange(picked.Count, 1, 2);
        Assert.All(picked, g => Assert.Contains(g, s_threeGenres, StringComparer.Ordinal));
    }

    [Fact]
    public void PickWeightedGenres_stops_at_requested_instance_count()
    {
        (string Genre, int Score)[] scores = [("A", 10), ("B", 20)];

        List<string> picked = InvokePickWeightedGenres(scores, 5);

        Assert.InRange(picked.Count, 1, 5);
        Assert.All(picked, g => Assert.Contains(g, s_twoGenres, StringComparer.Ordinal));
    }

    private static readonly string[] s_twoGenres = ["A", "B"];
    private static readonly string[] s_threeGenres = ["A", "B", "C"];

    private static object? InvokeGenreStatic(string name, params object?[] args)
    {
        MethodInfo method = typeof(GenreSection).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Private static '{name}' not found on {nameof(GenreSection)}.");
        return method.Invoke(null, args);
    }

    private static List<string> InvokePickWeightedGenres((string Genre, int Score)[] scores, int instanceCount)
    {
        object? enumerable = InvokeGenreStatic("PickWeightedGenres", scores, instanceCount);
        return ((System.Collections.IEnumerable)enumerable!).Cast<string>().ToList();
    }
}
