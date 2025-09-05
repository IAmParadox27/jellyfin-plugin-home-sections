using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.HomeScreenSections.Model
{
    public class SectionInfo
    {
        [JsonPropertyName("description")]
        public string? Description { get; set; }
        
        [JsonPropertyName("adminNotes")]
        public string? AdminNotes { get; set; }
        
        [JsonPropertyName("versionControl")]
        public VersionControlInfo? VersionControl { get; set; }
        
        [JsonPropertyName("featureRequestUrl")]
        public string? FeatureRequestUrl { get; set; }
    }
    
    public class VersionControlInfo
    {
        [JsonPropertyName("platform")]
        public string Platform { get; set; } = string.Empty;
        
        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;
        
        [JsonPropertyName("repository")]
        public string Repository { get; set; } = string.Empty;
        
        [JsonPropertyName("includeIssuesLink")]
        public bool IncludeIssuesLink { get; set; } = false;
        
        [JsonPropertyName("featureRequestTag")]
        public string? FeatureRequestTag { get; set; }
        
        [JsonPropertyName("repositoryUrl")]
        public string? RepositoryUrl { get; set; }
        
        [JsonPropertyName("issuesUrl")]
        public string? IssuesUrl { get; set; }
    }
}
