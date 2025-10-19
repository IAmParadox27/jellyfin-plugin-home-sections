using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.HomeScreenSections.Model
{
    public class SectionRegisterPayload
    {
        [JsonPropertyName("id")]
        public required string Id { get; set; }
        
        [JsonPropertyName("displayText")]
        public string? DisplayText { get; set; }
        
        [JsonPropertyName("enableByDefault")]
        public bool? EnableByDefault { get; set; }
        
        [JsonPropertyName("info")]
        public SectionInfo? Info { get; set; }
        
        [JsonPropertyName("limit")]
        public int? Limit { get; set; }
        
        [JsonPropertyName("route")]
        public string? Route { get; set; }
        
        [JsonPropertyName("additionalData")]
        public string? AdditionalData { get; set; }
        
        [JsonPropertyName("resultsEndpoint")]
        public string? ResultsEndpoint { get; set; }
        
        [JsonPropertyName("resultsAssembly")]
        public string? ResultsAssembly { get; set; }
        
        [JsonPropertyName("resultsClass")]
        public string? ResultsClass { get; set; }
        
        [JsonPropertyName("resultsMethod")]
        public string? ResultsMethod { get; set; }
        
        [JsonPropertyName("configurationOptions")]
        public string? ConfigurationOptions { get; set; }
    }
}