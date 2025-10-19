using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.HomeScreenSections.Model.Dto
{
    public class RadarrCalendarDto : ArrDtoBase
    {
        [JsonPropertyName("year")]
        public int Year { get; set; }

        [JsonPropertyName("digitalRelease")]
        public DateTime? DigitalRelease { get; set; }

        [JsonPropertyName("hasFile")]
        public bool HasFile { get; set; }
    }
}