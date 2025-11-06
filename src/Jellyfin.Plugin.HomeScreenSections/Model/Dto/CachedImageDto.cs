namespace Jellyfin.Plugin.HomeScreenSections.Model.Dto
{
    public class CachedImageDto
    {
        public string CacheKey { get; set; } = string.Empty;
        public string SourceUrl { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string ContentType { get; set; } = "image/jpeg";
        public DateTime CachedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
    }
}
