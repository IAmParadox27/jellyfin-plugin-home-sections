using System.Text.Json.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.HomeScreenSections.Model
{
    public class SectionRegisterPayload
    {
        [JsonPropertyName("id")]
        [JsonProperty("id")]
        public required string Id { get; set; }
        
        [JsonPropertyName("displayText")]
        [JsonProperty("displayText")]
        public string? DisplayText { get; set; }
        
        [JsonPropertyName("limit")]
        [JsonProperty("limit")]
        public int? Limit { get; set; }
        
        [JsonPropertyName("route")]
        [JsonProperty("route")]
        public string? Route { get; set; }
        
        [JsonPropertyName("additionalData")]
        [JsonProperty("additionalData")]
        public string? AdditionalData { get; set; }

        /// <summary>
        /// Optional item payload used for the section title link (e.g. collection/playlist DTO).
        /// </summary>
        [JsonPropertyName("originalPayload")]
        [JsonProperty("originalPayload")]
        public JToken? OriginalPayload { get; set; }
        
        [JsonPropertyName("resultsEndpoint")]
        [JsonProperty("resultsEndpoint")]
        public string? ResultsEndpoint { get; set; }
        
        [JsonPropertyName("resultsAssembly")]
        [JsonProperty("resultsAssembly")]
        public string? ResultsAssembly { get; set; }
        
        [JsonPropertyName("resultsClass")]
        [JsonProperty("resultsClass")]
        public string? ResultsClass { get; set; }
        
        [JsonPropertyName("resultsMethod")]
        [JsonProperty("resultsMethod")]
        public string? ResultsMethod { get; set; }
    }
}