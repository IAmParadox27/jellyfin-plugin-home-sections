using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.HomeScreenSections.Model.Dto
{
    public class LidarrCalendarDto : ArrDtoBase
    {
        [JsonPropertyName("releaseDate")]
        public DateTime? ReleaseDate { get; set; }

        [JsonPropertyName("artist")]
        public LidarrArtistDto? Artist { get; set; }

        [JsonPropertyName("albumType")]
        public string? AlbumType { get; set; }

        [JsonPropertyName("statistics")]
        public LidarrStatisticsDto? Statistics { get; set; }

        // Helper property to check if album has files
        public bool HasFile => Statistics?.SizeOnDisk > 0;
    }

    public class LidarrArtistDto
    {
        [JsonPropertyName("artistName")]
        public string? ArtistName { get; set; }
    }

    public class LidarrStatisticsDto
    {
        [JsonPropertyName("sizeOnDisk")]
        public long SizeOnDisk { get; set; }
    }
}