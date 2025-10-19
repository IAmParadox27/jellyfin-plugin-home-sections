using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.HomeScreenSections.Model.Dto
{
    public class ReadarrCalendarDto : ArrDtoBase
    {
        [JsonPropertyName("seriesTitle")]
        public string? SeriesTitle { get; set; }

        [JsonPropertyName("releaseDate")]
        public DateTime? ReleaseDate { get; set; }

        [JsonPropertyName("author")]
        public ReadarrAuthorDto? Author { get; set; }

        [JsonPropertyName("statistics")]
        public ReadarrStatisticsDto? Statistics { get; set; }

        // Helper property to check if book has files
        public bool HasFile => Statistics?.SizeOnDisk > 0;
    }

    public class ReadarrAuthorDto
    {
        [JsonPropertyName("authorName")]
        public string? AuthorName { get; set; }
    }

    public class ReadarrStatisticsDto
    {
        [JsonPropertyName("sizeOnDisk")]
        public long SizeOnDisk { get; set; }
    }
}