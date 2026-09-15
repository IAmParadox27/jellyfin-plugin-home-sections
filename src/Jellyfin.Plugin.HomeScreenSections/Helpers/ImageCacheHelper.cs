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
            if (string.IsNullOrEmpty(sourceUrl))
            {
                return string.Empty;
            }

            try
            {
                PluginConfiguration? config = HomeScreenSectionsPlugin.Instance?.Configuration;
                int cacheTimeout = config?.CacheTimeoutSeconds ?? 86400;

                string? cacheKey = imageCacheService.GetOrCacheImageCore(sourceUrl, cacheTimeout, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

                if (!string.IsNullOrEmpty(cacheKey))
                {
                    return $"/HomeScreen/CachedImage/{cacheKey}";
                }

                logger?.LogWarning("Failed to cache image from {SourceUrl}, using original URL", sourceUrl);
                return sourceUrl;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error caching image from {SourceUrl}", sourceUrl);
                return sourceUrl;
            }
        }

        public static ValueTask<string> GetCachedImageUrlAsync(
            ImageCacheService imageCacheService,
            string? sourceUrl,
            CancellationToken cancellationToken,
            ILogger? logger = null)
        {
            if (string.IsNullOrEmpty(sourceUrl))
            {
                return new ValueTask<string>(string.Empty);
            }

            try
            {
                PluginConfiguration? config = HomeScreenSectionsPlugin.Instance?.Configuration;
                int cacheTimeout = config?.CacheTimeoutSeconds ?? 86400;
                ValueTask<string?> cacheKey = imageCacheService.GetOrCacheImageCore(sourceUrl, cacheTimeout, cancellationToken);
                if (cacheKey.IsCompletedSuccessfully)
                {
                    return new ValueTask<string>(GetImageUrl(cacheKey.Result, sourceUrl, logger));
                }

                return AwaitCachedImageUrlAsync(cacheKey, sourceUrl, cancellationToken, logger);
            }
            catch (Exception ex)
            {
                // Keep exceptions asynchronous, including caller cancellation and logger failures.
                return AwaitCachedImageUrlAsync(ValueTask.FromException<string?>(ex), sourceUrl, cancellationToken, logger);
            }
        }

        private static async ValueTask<string> AwaitCachedImageUrlAsync(ValueTask<string?> cacheKey, string sourceUrl, CancellationToken cancellationToken, ILogger? logger)
        {
            try
            {
                return GetImageUrl(await cacheKey, sourceUrl, logger);
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

        private static string GetImageUrl(string? cacheKey, string sourceUrl, ILogger? logger)
        {
            if (!string.IsNullOrEmpty(cacheKey))
            {
                return $"/HomeScreen/CachedImage/{cacheKey}";
            }

            logger?.LogWarning("Failed to cache image from {SourceUrl}, using original URL", sourceUrl);
            return sourceUrl;
        }
    }
}
