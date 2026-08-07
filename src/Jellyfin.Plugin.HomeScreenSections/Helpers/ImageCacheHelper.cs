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

                string? cacheKey = imageCacheService.GetOrCacheImage(sourceUrl, cacheTimeout)
                    .GetAwaiter()
                    .GetResult();

                if (!string.IsNullOrEmpty(cacheKey))
                {
                    return $"/HomeScreen/CachedImage/{cacheKey}";
                }

                if (logger != null)
                {
                    PluginLog.ImageCacheFallback(logger, sourceUrl);
                }
                return sourceUrl;
            }
            catch (HttpRequestException ex)
            {
                if (logger != null)
                {
                    PluginLog.ImageCacheHelperError(logger, ex, sourceUrl);
                }
                return sourceUrl;
            }
            catch (IOException ex)
            {
                if (logger != null)
                {
                    PluginLog.ImageCacheHelperError(logger, ex, sourceUrl);
                }
                return sourceUrl;
            }
            catch (TaskCanceledException ex)
            {
                if (logger != null)
                {
                    PluginLog.ImageCacheHelperError(logger, ex, sourceUrl);
                }
                return sourceUrl;
            }
            catch (InvalidOperationException ex)
            {
                if (logger != null)
                {
                    PluginLog.ImageCacheHelperError(logger, ex, sourceUrl);
                }
                return sourceUrl;
            }
        }
    }
}
