using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections.Upcoming;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using Jellyfin.Plugin.HomeScreenSections.Services;
using Jellyfin.Plugin.HomeScreenSections.Tests.Support;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Jellyfin.Plugin.HomeScreenSections.Tests.HomeScreen.Sections;

/// <summary>
/// End-to-end tests for the concrete *arr sections: fake calendar JSON flows through
/// ArrApiService into the section's filter/sort/DTO mapping.
/// </summary>
[Collection("Plugin Instance")]
public class UpcomingServiceSectionsTests
{
    private readonly PluginFixture m_fixture;

    public UpcomingServiceSectionsTests(PluginFixture fixture)
    {
        m_fixture = fixture;
    }

    [Fact]
    public void UpcomingMovies_filters_unmonitored_and_downloaded_and_sorts_by_release_date()
    {
        PluginConfiguration config = HomeScreenSectionsPlugin.Instance.Configuration;
        config.Radarr.Url = "http://radarr.test";
        config.Radarr.ApiKey = "test-key";
        config.FilterUpcomingByLibraryAccess = false;
        try
        {
            DateTime digitalSoon = DateTime.UtcNow.AddDays(10);
            DateTime digitalFirst = DateTime.UtcNow.AddDays(5);
            string json = $$"""
                [
                    { "id": 1, "title": "Digital Soon", "monitored": true, "hasFile": false, "year": 2026,
                      "digitalRelease": "{{digitalSoon:O}}",
                      "images": [ { "coverType": "poster", "remoteUrl": "https://img.test/1.jpg" } ] },
                    { "id": 2, "title": "Unmonitored", "monitored": false, "digitalRelease": "{{DateTime.UtcNow.AddDays(3):O}}" },
                    { "id": 3, "title": "Already Have", "monitored": true, "hasFile": true, "digitalRelease": "{{DateTime.UtcNow.AddDays(2):O}}" },
                    { "id": 4, "title": "Cinema Only", "monitored": true, "hasFile": false, "inCinemas": "{{DateTime.UtcNow.AddDays(4):O}}" },
                    { "id": 5, "title": "Digital First", "monitored": true, "hasFile": false, "year": 0,
                      "digitalRelease": "{{digitalFirst:O}}" }
                ]
                """;

            UpcomingMoviesSection section = MakeMoviesSection(json);

            MediaBrowser.Model.Querying.QueryResult<MediaBrowser.Model.Dto.BaseItemDto> result =
                section.GetResults(new HomeScreenSectionPayload { UserId = Guid.NewGuid() }, new FakeQueryCollection());

            // Only monitored, file-less items with a release type enabled in config survive;
            // default config considers digital releases only.
            Assert.Equal(2, result.Items.Count);
            Assert.Equal("Digital First", result.Items[0].Name);
            Assert.Equal("Digital Soon", result.Items[1].Name);
        }
        finally
        {
            config.Radarr.Url = string.Empty;
            config.Radarr.ApiKey = string.Empty;
            config.FilterUpcomingByLibraryAccess = true;
        }
    }

    [Fact]
    public void UpcomingMovies_includes_cinema_only_items_when_cinema_release_enabled()
    {
        PluginConfiguration config = HomeScreenSectionsPlugin.Instance.Configuration;
        config.Radarr.Url = "http://radarr.test";
        config.Radarr.ApiKey = "test-key";
        config.FilterUpcomingByLibraryAccess = false;
        bool originalCinema = config.Radarr.ConsiderCinemaRelease;
        config.Radarr.ConsiderCinemaRelease = true;
        try
        {
            DateTime cinemaDate = DateTime.UtcNow.AddDays(6);
            string json = $$"""
                [
                    { "id": 9, "title": "Cinema Only", "monitored": true, "hasFile": false,
                      "inCinemas": "{{cinemaDate:O}}" }
                ]
                """;

            UpcomingMoviesSection section = MakeMoviesSection(json);

            MediaBrowser.Model.Querying.QueryResult<MediaBrowser.Model.Dto.BaseItemDto> result =
                section.GetResults(new HomeScreenSectionPayload { UserId = Guid.NewGuid() }, new FakeQueryCollection());

            MediaBrowser.Model.Dto.BaseItemDto dto = Assert.Single(result.Items);
            Assert.Equal("Cinema Only", dto.Name);
            Assert.Equal(cinemaDate.Date, dto.PremiereDate!.Value.Date);
        }
        finally
        {
            config.Radarr.ConsiderCinemaRelease = originalCinema;
            config.Radarr.Url = string.Empty;
            config.Radarr.ApiKey = string.Empty;
            config.FilterUpcomingByLibraryAccess = true;
        }
    }

    [Fact]
    public void UpcomingMovies_maps_dto_fields_and_provider_ids()
    {
        PluginConfiguration config = HomeScreenSectionsPlugin.Instance.Configuration;
        config.Radarr.Url = "http://radarr.test";
        config.Radarr.ApiKey = "test-key";
        config.FilterUpcomingByLibraryAccess = false;
        try
        {
            DateTime release = DateTime.UtcNow.AddDays(12);
            string json = $$"""
                [
                    { "id": 42, "title": "Mapped Movie", "monitored": true, "hasFile": false, "year": 2027,
                      "digitalRelease": "{{release:O}}" }
                ]
                """;

            UpcomingMoviesSection section = MakeMoviesSection(json);

            MediaBrowser.Model.Querying.QueryResult<MediaBrowser.Model.Dto.BaseItemDto> result =
                section.GetResults(new HomeScreenSectionPayload { UserId = Guid.NewGuid() }, new FakeQueryCollection());

            MediaBrowser.Model.Dto.BaseItemDto dto = Assert.Single(result.Items);
            Assert.Equal("Mapped Movie", dto.Name);
            Assert.Equal(BaseItemKind.Movie, dto.Type);
            Assert.Equal(2027, dto.ProductionYear);
            Assert.NotNull(dto.PremiereDate);
            Assert.Equal(release.Date, dto.PremiereDate!.Value.Date);

            Assert.Equal("42", dto.ProviderIds["RadarrMovieId"]);
            Assert.Equal(" (2027)", dto.ProviderIds["YearInfo"]);
            Assert.False(string.IsNullOrWhiteSpace(dto.ProviderIds["FormattedDate"]));
            // Image cache returns 404 in this test, so the poster falls back to the placeholder.
            Assert.StartsWith("https://placehold.co/", dto.ProviderIds["RadarrPoster"], StringComparison.Ordinal);
            Assert.Contains("Mapped%20Movie", dto.ProviderIds["RadarrPoster"], StringComparison.Ordinal);

            Assert.NotNull(dto.UserData);
            Assert.Equal("upcoming-movie-42", dto.UserData!.Key);
        }
        finally
        {
            config.Radarr.Url = string.Empty;
            config.Radarr.ApiKey = string.Empty;
            config.FilterUpcomingByLibraryAccess = true;
        }
    }

    [Fact]
    public void UpcomingMovies_poster_is_used_when_remote_image_available()
    {
        PluginConfiguration config = HomeScreenSectionsPlugin.Instance.Configuration;
        config.Radarr.Url = "http://radarr.test";
        config.Radarr.ApiKey = "test-key";
        config.FilterUpcomingByLibraryAccess = false;
        try
        {
            string json = $$"""
                [
                    { "id": 7, "title": "Postered", "monitored": true, "hasFile": false,
                      "digitalRelease": "{{DateTime.UtcNow.AddDays(6):O}}",
                      "images": [ { "coverType": "poster", "remoteUrl": "https://img.test/poster.jpg" } ] }
                ]
                """;

            UpcomingMoviesSection section = MakeMoviesSection(json);

            MediaBrowser.Model.Querying.QueryResult<MediaBrowser.Model.Dto.BaseItemDto> result =
                section.GetResults(new HomeScreenSectionPayload { UserId = Guid.NewGuid() }, new FakeQueryCollection());

            // The image cache fake responds 404, so ImageCacheHelper falls back to the source URL.
            Assert.Equal("https://img.test/poster.jpg", Assert.Single(result.Items).ProviderIds["RadarrPoster"]);
        }
        finally
        {
            config.Radarr.Url = string.Empty;
            config.Radarr.ApiKey = string.Empty;
            config.FilterUpcomingByLibraryAccess = true;
        }
    }

    [Fact]
    public void UpcomingMovies_CreateInstances_copies_display_properties()
    {
        UpcomingMoviesSection section = MakeMoviesSection("[]");
        section.DisplayText = "Renamed";
        section.AdditionalData = "extra";

        List<IHomeScreenSection> instances = [.. section.CreateInstances(Guid.NewGuid(), 1)];

        UpcomingMoviesSection instance = Assert.IsType<UpcomingMoviesSection>(Assert.Single(instances));
        Assert.NotSame(section, instance);
        Assert.Equal("Renamed", instance.DisplayText);
        Assert.Equal("extra", instance.AdditionalData);
        Assert.Equal("UpcomingMovies", instance.Section);
    }

    [Fact]
    public void UpcomingMovies_GetInfo_is_portrait_and_locked()
    {
        UpcomingMoviesSection section = MakeMoviesSection("[]");

        HomeScreenSectionInfo info = section.GetInfo();

        Assert.Equal("UpcomingMovies", info.Section);
        Assert.Equal(SectionViewMode.Portrait, info.ViewMode);
        Assert.False(info.AllowViewModeChange);
        Assert.Equal("upcoming-movies-section", info.ContainerClass);
        Assert.Equal(1, info.Limit);
    }

    [Fact]
    public void UpcomingShows_reads_sonarr_calendar_and_maps_episodes()
    {
        PluginConfiguration config = HomeScreenSectionsPlugin.Instance.Configuration;
        config.Sonarr.Url = "http://sonarr.test";
        config.Sonarr.ApiKey = "test-key";
        config.FilterUpcomingByLibraryAccess = false;
        try
        {
            DateTime airDate = DateTime.UtcNow.AddDays(9);
            string json = $$"""
                [
                    { "id": 5, "title": "The Episode", "monitored": true, "hasFile": false,
                      "seriesId": 2, "seasonNumber": 1, "episodeNumber": 3,
                      "airDateUtc": "{{airDate:O}}",
                      "series": { "id": 2, "title": "The Show" } }
                ]
                """;

            UpcomingShowsSection section = MakeShowsSection(json);

            MediaBrowser.Model.Querying.QueryResult<MediaBrowser.Model.Dto.BaseItemDto> result =
                section.GetResults(new HomeScreenSectionPayload { UserId = Guid.NewGuid() }, new FakeQueryCollection());

            MediaBrowser.Model.Dto.BaseItemDto dto = Assert.Single(result.Items);
            Assert.Equal(BaseItemKind.Episode, dto.Type);
            Assert.Equal("The Show", dto.SeriesName);
            Assert.NotNull(dto.ProviderIds);
            Assert.Equal("5", dto.ProviderIds!["SonarrEpisodeId"]);
        }
        finally
        {
            config.Sonarr.Url = string.Empty;
            config.Sonarr.ApiKey = string.Empty;
            config.FilterUpcomingByLibraryAccess = true;
        }
    }

    private UpcomingMoviesSection MakeMoviesSection(string calendarJson)
    {
        return new UpcomingMoviesSection(
            new Mock<IUserManager>().Object,
            new Mock<ILibraryManager>().Object,
            new Mock<IDtoService>().Object,
            MakeArrService(calendarJson),
            MakeImageCacheService(),
            NullLogger<UpcomingMoviesSection>.Instance);
    }

    private UpcomingShowsSection MakeShowsSection(string calendarJson)
    {
        return new UpcomingShowsSection(
            new Mock<IUserManager>().Object,
            new Mock<ILibraryManager>().Object,
            new Mock<IDtoService>().Object,
            MakeArrService(calendarJson),
            MakeImageCacheService(),
            NullLogger<UpcomingShowsSection>.Instance);
    }

    [Fact]
    public void UpcomingMusic_filters_and_maps_lidarr_albums()
    {
        PluginConfiguration config = HomeScreenSectionsPlugin.Instance.Configuration;
        config.Lidarr.Url = "http://lidarr.test";
        config.Lidarr.ApiKey = "test-key";
        config.FilterUpcomingByLibraryAccess = false;
        try
        {
            DateTime release = DateTime.UtcNow.AddDays(8);
            string json = $$"""
                [
                    { "id": 3, "title": "The Album", "monitored": true, "albumType": "Album",
                      "releaseDate": "{{release:O}}",
                      "artist": { "artistName": "The Band" },
                      "images": [ { "coverType": "cover", "remoteUrl": "https://img.test/cover.jpg" } ] },
                    { "id": 4, "title": "Already Owned", "monitored": true,
                      "releaseDate": "{{DateTime.UtcNow.AddDays(2):O}}",
                      "statistics": { "sizeOnDisk": 123 }, "artist": { "artistName": "X" } },
                    { "id": 5, "title": "Not Monitored", "monitored": false,
                      "releaseDate": "{{DateTime.UtcNow.AddDays(1):O}}", "artist": { "artistName": "Y" } }
                ]
                """;

            UpcomingMusicSection section = MakeMusicSection(json);

            MediaBrowser.Model.Querying.QueryResult<MediaBrowser.Model.Dto.BaseItemDto> result =
                section.GetResults(new HomeScreenSectionPayload { UserId = Guid.NewGuid() }, new FakeQueryCollection());

            MediaBrowser.Model.Dto.BaseItemDto dto = Assert.Single(result.Items);
            Assert.Equal("The Album", dto.Name);
            Assert.Equal(Jellyfin.Data.Enums.BaseItemKind.MusicAlbum, dto.Type);
            Assert.Equal("The Band - Album", dto.Overview);
            Assert.Equal("3", dto.ProviderIds!["LidarrAlbumId"]);
            Assert.Equal("https://img.test/cover.jpg", dto.ProviderIds["LidarrPoster"]);
            Assert.Equal("upcoming-album-3", dto.UserData!.Key);
        }
        finally
        {
            config.Lidarr.Url = string.Empty;
            config.Lidarr.ApiKey = string.Empty;
            config.FilterUpcomingByLibraryAccess = true;
        }
    }

    [Fact]
    public void UpcomingMusic_GetInfo_is_square_and_locked()
    {
        UpcomingMusicSection section = MakeMusicSection("[]");

        HomeScreenSectionInfo info = section.GetInfo();

        Assert.Equal("UpcomingMusic", info.Section);
        Assert.Equal(SectionViewMode.Square, info.ViewMode);
        Assert.False(info.AllowViewModeChange);
        Assert.Equal("upcoming-music-section", info.ContainerClass);
    }

    [Fact]
    public void UpcomingBooks_filters_and_maps_readarr_books()
    {
        PluginConfiguration config = HomeScreenSectionsPlugin.Instance.Configuration;
        config.Readarr.Url = "http://readarr.test";
        config.Readarr.ApiKey = "test-key";
        config.FilterUpcomingByLibraryAccess = false;
        try
        {
            DateTime release = DateTime.UtcNow.AddDays(20);
            string json = $$"""
                [
                    { "id": 9, "title": "The Book", "monitored": true, "seriesTitle": "Saga",
                      "releaseDate": "{{release:O}}",
                      "author": { "authorName": "The Writer" } },
                    { "id": 10, "title": "Owned", "monitored": true,
                      "releaseDate": "{{DateTime.UtcNow.AddDays(3):O}}",
                      "statistics": { "sizeOnDisk": 5 }, "author": { "authorName": "Z" } }
                ]
                """;

            UpcomingBooksSection section = MakeBooksSection(json);

            MediaBrowser.Model.Querying.QueryResult<MediaBrowser.Model.Dto.BaseItemDto> result =
                section.GetResults(new HomeScreenSectionPayload { UserId = Guid.NewGuid() }, new FakeQueryCollection());

            MediaBrowser.Model.Dto.BaseItemDto dto = Assert.Single(result.Items);
            Assert.Equal("The Book", dto.Name);
            Assert.Equal(Jellyfin.Data.Enums.BaseItemKind.Book, dto.Type);
            Assert.Equal("The Writer - Saga", dto.Overview);
            Assert.Equal("9", dto.ProviderIds!["ReadarrBookId"]);
            Assert.StartsWith("https://placehold.co/", dto.ProviderIds["ReadarrPoster"], StringComparison.Ordinal);
            Assert.Equal("upcoming-book-9", dto.UserData!.Key);
        }
        finally
        {
            config.Readarr.Url = string.Empty;
            config.Readarr.ApiKey = string.Empty;
            config.FilterUpcomingByLibraryAccess = true;
        }
    }

    [Fact]
    public void UpcomingBooks_GetInfo_is_portrait_and_locked()
    {
        UpcomingBooksSection section = MakeBooksSection("[]");

        HomeScreenSectionInfo info = section.GetInfo();

        Assert.Equal("UpcomingBooks", info.Section);
        Assert.Equal(SectionViewMode.Portrait, info.ViewMode);
        Assert.False(info.AllowViewModeChange);
        Assert.Equal("upcoming-books-section", info.ContainerClass);
    }

    private UpcomingMusicSection MakeMusicSection(string calendarJson)
    {
        return new UpcomingMusicSection(
            new Mock<IUserManager>().Object,
            new Mock<ILibraryManager>().Object,
            new Mock<IDtoService>().Object,
            MakeArrService(calendarJson),
            MakeImageCacheService(),
            NullLogger<UpcomingMusicSection>.Instance);
    }

    private UpcomingBooksSection MakeBooksSection(string calendarJson)
    {
        return new UpcomingBooksSection(
            new Mock<IUserManager>().Object,
            new Mock<ILibraryManager>().Object,
            new Mock<IDtoService>().Object,
            MakeArrService(calendarJson),
            MakeImageCacheService(),
            NullLogger<UpcomingBooksSection>.Instance);
    }

    private static ArrApiService MakeArrService(string calendarJson)
    {
        return new ArrApiService(
            NullLogger<ArrApiService>.Instance,
            new HttpClient(FakeHttpMessageHandler.RespondingWithJson(calendarJson)));
    }

    private ImageCacheService MakeImageCacheService()
    {
        return new ImageCacheService(
            NullLogger<ImageCacheService>.Instance,
            m_fixture.Paths,
            new HttpClient(FakeHttpMessageHandler.RespondingWithStatus(System.Net.HttpStatusCode.NotFound)));
    }
}
