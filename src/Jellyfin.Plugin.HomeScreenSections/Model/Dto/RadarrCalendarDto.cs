using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.HomeScreenSections.Model.Dto
{
    public class RadarrCalendarDto : ArrDtoBase
    {
        [JsonPropertyName("tmdbId")]
        public int TmdbId { get; set; }

        [JsonPropertyName("imdbId")]
        public string? ImdbId { get; set; }

        [JsonPropertyName("year")]
        public int Year { get; set; }

        [JsonPropertyName("inCinemas")]
        public DateTime? InCinemas { get; set; }

        [JsonPropertyName("physicalRelease")]
        public DateTime? PhysicalRelease { get; set; }

        [JsonPropertyName("digitalRelease")]
        public DateTime? DigitalRelease { get; set; }

        [JsonPropertyName("hasFile")]
        public bool HasFile { get; set; }

        [JsonPropertyName("path")]
        public string? Path { get; set; }
    }
}