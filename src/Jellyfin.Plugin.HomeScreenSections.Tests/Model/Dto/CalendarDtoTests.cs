using System.Text.Json;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;

namespace Jellyfin.Plugin.HomeScreenSections.Tests.Model.Dto;

public class CalendarDtoTests
{
    private static readonly JsonSerializerOptions s_arrOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void LidarrCalendarDto_HasFile_is_false_without_statistics()
    {
        LidarrCalendarDto dto = new LidarrCalendarDto();
        Assert.False(dto.HasFile);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(1024, true)]
    public void LidarrCalendarDto_HasFile_reflects_size_on_disk(long sizeOnDisk, bool expected)
    {
        LidarrCalendarDto dto = new LidarrCalendarDto
        {
            Statistics = new LidarrStatisticsDto { SizeOnDisk = sizeOnDisk }
        };

        Assert.Equal(expected, dto.HasFile);
    }

    [Fact]
    public void ReadarrCalendarDto_HasFile_is_false_without_statistics()
    {
        ReadarrCalendarDto dto = new ReadarrCalendarDto();
        Assert.False(dto.HasFile);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(512, true)]
    public void ReadarrCalendarDto_HasFile_reflects_size_on_disk(long sizeOnDisk, bool expected)
    {
        ReadarrCalendarDto dto = new ReadarrCalendarDto
        {
            Statistics = new ReadarrStatisticsDto { SizeOnDisk = sizeOnDisk }
        };

        Assert.Equal(expected, dto.HasFile);
    }

    [Fact]
    public void RadarrCalendarDto_deserializes_from_arr_calendar_json()
    {
        const string json = """
            {
                "id": 42,
                "title": "Test Movie",
                "monitored": true,
                "year": 2026,
                "inCinemas": "2026-07-01T00:00:00Z",
                "physicalRelease": "2026-09-15T00:00:00Z",
                "digitalRelease": "2026-08-20T00:00:00Z",
                "hasFile": false,
                "path": "/movies/test-movie",
                "images": [ { "coverType": "poster", "remoteUrl": "https://example.com/poster.jpg" } ]
            }
            """;

        RadarrCalendarDto? dto = JsonSerializer.Deserialize<RadarrCalendarDto>(json, s_arrOptions);

        Assert.NotNull(dto);
        Assert.Equal(42, dto!.Id);
        Assert.Equal("Test Movie", dto.Title);
        Assert.True(dto.Monitored);
        Assert.Equal(2026, dto.Year);
        Assert.Equal(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), dto.InCinemas);
        Assert.Equal(new DateTime(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc), dto.PhysicalRelease);
        Assert.Equal(new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc), dto.DigitalRelease);
        Assert.False(dto.HasFile);
        Assert.Equal("/movies/test-movie", dto.Path);
        ArrImageDto image = Assert.Single(dto.Images!);
        Assert.Equal("poster", image.CoverType);
        Assert.Equal("https://example.com/poster.jpg", image.RemoteUrl);
    }

    [Fact]
    public void SonarrCalendarDto_deserializes_series_and_episode_fields()
    {
        const string json = """
            {
                "id": 7,
                "title": "Episode Title",
                "seriesId": 3,
                "seasonNumber": 2,
                "episodeNumber": 5,
                "airDateUtc": "2026-08-09T20:00:00Z",
                "hasFile": true,
                "series": { "id": 3, "title": "Show Name", "path": "/tv/show" }
            }
            """;

        SonarrCalendarDto? dto = JsonSerializer.Deserialize<SonarrCalendarDto>(json, s_arrOptions);

        Assert.NotNull(dto);
        Assert.Equal(7, dto!.Id);
        Assert.Equal(3, dto.SeriesId);
        Assert.Equal(2, dto.SeasonNumber);
        Assert.Equal(5, dto.EpisodeNumber);
        Assert.Equal(new DateTime(2026, 8, 9, 20, 0, 0, DateTimeKind.Utc), dto.AirDateUtc);
        Assert.True(dto.HasFile);
        Assert.NotNull(dto.Series);
        Assert.Equal("Show Name", dto.Series!.Title);
        Assert.Equal(3, dto.Series.Id);
        Assert.Equal("/tv/show", dto.Series.Path);
    }

    [Fact]
    public void LidarrCalendarDto_deserializes_artist_and_statistics()
    {
        const string json = """
            {
                "id": 11,
                "title": "Album",
                "releaseDate": "2026-10-02T00:00:00Z",
                "albumType": "Album",
                "artist": { "artistName": "Artist", "path": "/music/artist" },
                "statistics": { "sizeOnDisk": 999 }
            }
            """;

        LidarrCalendarDto? dto = JsonSerializer.Deserialize<LidarrCalendarDto>(json, s_arrOptions);

        Assert.NotNull(dto);
        Assert.Equal(new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc), dto!.ReleaseDate);
        Assert.Equal("Album", dto.AlbumType);
        Assert.Equal("Artist", dto.Artist?.ArtistName);
        Assert.Equal("/music/artist", dto.Artist?.Path);
        Assert.True(dto.HasFile);
    }

    [Fact]
    public void ReadarrCalendarDto_deserializes_author_and_series_title()
    {
        const string json = """
            {
                "id": 5,
                "title": "Book",
                "seriesTitle": "The Series",
                "releaseDate": "2027-01-15T00:00:00Z",
                "author": { "authorName": "Writer", "path": "/books/writer" }
            }
            """;

        ReadarrCalendarDto? dto = JsonSerializer.Deserialize<ReadarrCalendarDto>(json, s_arrOptions);

        Assert.NotNull(dto);
        Assert.Equal("The Series", dto!.SeriesTitle);
        Assert.Equal(new DateTime(2027, 1, 15, 0, 0, 0, DateTimeKind.Utc), dto.ReleaseDate);
        Assert.Equal("Writer", dto.Author?.AuthorName);
        Assert.False(dto.HasFile);
    }

    [Fact]
    public void CachedImageDto_defaults_to_jpeg_and_empty_strings()
    {
        CachedImageDto dto = new CachedImageDto();

        Assert.Equal(string.Empty, dto.CacheKey);
        Assert.Equal(string.Empty, dto.SourceUrl);
        Assert.Equal(string.Empty, dto.FilePath);
        Assert.Equal("image/jpeg", dto.ContentType);
    }
}
