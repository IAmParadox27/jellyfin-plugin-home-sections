using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.HomeScreenSections.Model.Dto
{
    public class SonarrCalendarDto : ArrDtoBase
    {
        [JsonPropertyName("seriesId")]
        public int SeriesId { get; set; }

        [JsonPropertyName("seasonNumber")]
        public int SeasonNumber { get; set; }

        [JsonPropertyName("episodeNumber")]
        public int EpisodeNumber { get; set; }

        [JsonPropertyName("airDateUtc")]
        public DateTime? AirDateUtc { get; set; }

        [JsonPropertyName("hasFile")]
        public bool HasFile { get; set; }

        [JsonPropertyName("series")]
        public SonarrSeriesDto? Series { get; set; }
    }

    public class SonarrSeriesDto
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("images")]
        public ArrImageDto[]? Images { get; set; }

        [JsonPropertyName("id")]
        public int Id { get; set; }
    }
}