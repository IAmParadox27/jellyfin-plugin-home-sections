using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.HomeScreenSections.Model
{
    public class DiscoverRequestPayload
    {
        [JsonPropertyName("UserId")]
        public Guid UserId { get; set; }
        
        [JsonPropertyName("MediaType")]
        public string MediaType { get; set; }
        
        [JsonPropertyName("MediaId")]
        public int MediaId { get; set; }
    }

    public class JellyseerrRequestPayload
    {
        [JsonPropertyName("mediaType")]
        public string MediaType { get; set; }

        [JsonPropertyName("mediaId")]
        public int MediaId { get; set; }
    }
}