using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Services;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.HomeScreenSections.Helpers
{
    public static class ImageCacheHelper
    {
        public static string GetCachedImageUrl(
            ImageCacheService imageCacheService, 
            string? sourceUrl, 
            ILogger? logger = null)
        {
            return GetCachedImageUrlAsync(imageCacheService, sourceUrl, CancellationToken.None, logger).GetAwaiter().GetResult();
        }

        public static async Task<string> GetCachedImageUrlAsync(
            ImageCacheService imageCacheService,
            string? sourceUrl,
            CancellationToken cancellationToken,
            ILogger? logger = null)
        {
            if (string.IsNullOrEmpty(sourceUrl))
            {
                return string.Empty;
            }

            try
            {
                PluginConfiguration? config = HomeScreenSectionsPlugin.Instance?.Configuration;
                int cacheTimeout = config?.CacheTimeoutSeconds ?? 86400;

                string? cacheKey = await imageCacheService.GetOrCacheImage(sourceUrl, cacheTimeout, cancellationToken);

                if (!string.IsNullOrEmpty(cacheKey))
                {
                    return $"/HomeScreen/CachedImage/{cacheKey}";
                }

                logger?.LogWarning("Failed to cache image from {SourceUrl}, using original URL", sourceUrl);
                return sourceUrl;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error caching image from {SourceUrl}", sourceUrl);
                return sourceUrl;
            }
        }
    }
}
