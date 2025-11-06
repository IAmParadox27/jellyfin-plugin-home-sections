using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Jellyfin.Plugin.HomeScreenSections.Services
{
    public class ImageCacheService
    {
        private readonly ILogger<ImageCacheService> m_logger;
        private readonly IApplicationPaths m_applicationPaths;
        private readonly HttpClient m_httpClient;
        private readonly string m_cacheDirectory;
        // In-memory cache for quick lookups
        private readonly ConcurrentDictionary<string, CachedImageDto> m_imageCache = new();

        public ImageCacheService(
            ILogger<ImageCacheService> logger,
            IApplicationPaths applicationPaths,
            HttpClient httpClient)
        {
            m_logger = logger;
            m_applicationPaths = applicationPaths;
            m_httpClient = httpClient;
            m_cacheDirectory = Path.Combine(applicationPaths.CachePath, "HomeScreenSections", "Images");
            Directory.CreateDirectory(m_cacheDirectory);
            // Load existing cache entries on startup
            LoadCacheIndex();
        }

        public async Task<string?> GetOrCacheImage(string sourceUrl, int cacheTimeoutSeconds)
        {
            if (string.IsNullOrEmpty(sourceUrl))
            {
                return null;
            }
            string cacheKey = GenerateCacheKey(sourceUrl);

            if (IsValidCacheKey(cacheKey))
            {
                m_logger.LogDebug("Using cached image for {CacheKey}", cacheKey);
                return cacheKey;
            }

            if (m_imageCache.ContainsKey(cacheKey))
            {
                CleanupCacheEntry(cacheKey);
            }
            if (m_imageCache.Count >= HomeScreenSectionsPlugin.Instance.Configuration.MaxImageCacheEntries)
            {
                EvictOldEntries();
            }
            return await DownloadAndCacheImage(sourceUrl, cacheKey, cacheTimeoutSeconds);
        }

        private bool IsValidCacheKey(string cacheKey)
        {
            if (!m_imageCache.TryGetValue(cacheKey, out CachedImageDto? cachedInfo))
            {
                return false;
            }
            return cachedInfo.ExpiresAt > DateTime.UtcNow && File.Exists(cachedInfo.FilePath);
        }

        private void CleanupCacheEntry(string cacheKey)
        {
            if (!m_imageCache.TryRemove(cacheKey, out CachedImageDto? cachedInfo))
            {
                return;
            }
            if (File.Exists(cachedInfo.FilePath))
            {
                try
                {
                    File.Delete(cachedInfo.FilePath);
                }
                catch (Exception ex)
                {
                    m_logger.LogWarning(ex, "Failed to delete expired cache file {FilePath}", cachedInfo.FilePath);
                }
            }
        }

        private void EvictOldEntries()
        {
            List<string> oldestKeys = m_imageCache.Values
                .OrderBy(x => x.CachedAt)
                .Take(HomeScreenSectionsPlugin.Instance.Configuration.MaxImageCacheEntries / 10)
                .Select(x => x.CacheKey)
                .ToList();

            foreach (string key in oldestKeys)
            {
                CleanupCacheEntry(key);
            }
            
            if (oldestKeys.Count > 0)
            {
                SaveCacheIndex();
                m_logger.LogDebug("Evicted {Count} old cache entries", oldestKeys.Count);
            }
        }

        private async Task<string?> DownloadAndCacheImage(string sourceUrl, string cacheKey, int cacheTimeoutSeconds)
        {
            try
            {
                m_logger.LogDebug("Downloading image from {SourceUrl}", sourceUrl);

                using HttpResponseMessage response = await m_httpClient.GetAsync(sourceUrl);
                if (!response.IsSuccessStatusCode)
                {
                    m_logger.LogWarning("Failed to download image from {SourceUrl}, status: {StatusCode}",
                    sourceUrl, response.StatusCode);
                    return null;
                }

                byte[] imageData = await response.Content.ReadAsByteArrayAsync();
                string contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
                byte[] processedImageData = ProcessImage(imageData);
                if (processedImageData.Length > 0)
                {
                    imageData = processedImageData;
                    contentType = "image/jpeg";
                }
                
                string filePath = SaveImageToDisk(cacheKey, imageData, contentType);
                StoreCacheInfo(cacheKey, sourceUrl, filePath, contentType, cacheTimeoutSeconds);
                m_logger.LogDebug("Cached image {CacheKey} from {SourceUrl}", cacheKey, sourceUrl);
                return cacheKey;
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "Error downloading and caching image from {SourceUrl}", sourceUrl);
                return null;
            }
        }

        private string SaveImageToDisk(string cacheKey, byte[] imageData, string contentType)
        {
            string extension = GetExtensionFromContentType(contentType);
            string filePath = Path.Combine(m_cacheDirectory, $"{cacheKey}{extension}");
            File.WriteAllBytes(filePath, imageData);
            return filePath;
        }

        private void StoreCacheInfo(string cacheKey, string sourceUrl, string filePath, string contentType, int cacheTimeoutSeconds)
        {
            CachedImageDto newCacheInfo = new()
            {
                CacheKey = cacheKey,
                SourceUrl = sourceUrl,
                FilePath = filePath,
                ContentType = contentType,
                CachedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddSeconds(cacheTimeoutSeconds)
            };
            m_imageCache[cacheKey] = newCacheInfo;
            SaveCacheIndex();
        }

        public (byte[]? data, string? contentType) GetCachedImage(string cacheKey)
        {
            if (!m_imageCache.TryGetValue(cacheKey, out CachedImageDto? cachedInfo))
            {
                m_logger.LogDebug("Cache miss for key {CacheKey}", cacheKey);
                return (null, null);
            }
            if (cachedInfo.ExpiresAt < DateTime.UtcNow)
            {
                m_logger.LogDebug("Cache expired for key {CacheKey}", cacheKey);
                m_imageCache.TryRemove(cacheKey, out _);
                return (null, null);
            }
            if (!File.Exists(cachedInfo.FilePath))
            {
                m_logger.LogWarning("Cache file missing for key {CacheKey}", cacheKey);
                m_imageCache.TryRemove(cacheKey, out _);
                return (null, null);
            }

            try
            {
                byte[] data = File.ReadAllBytes(cachedInfo.FilePath);
                return (data, cachedInfo.ContentType);
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "Error reading cached image {CacheKey}", cacheKey);
                return (null, null);
            }
        }

        public void ClearExpiredCache()
        {
            List<string> expiredKeys = m_imageCache
                .Where(kvp => kvp.Value.ExpiresAt < DateTime.UtcNow)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (string key in expiredKeys)
            {
                if (m_imageCache.TryRemove(key, out CachedImageDto? cachedInfo))
                {
                    if (File.Exists(cachedInfo.FilePath))
                    {
                        try
                        {
                            File.Delete(cachedInfo.FilePath);
                            m_logger.LogDebug("Deleted expired cache file {FilePath}", cachedInfo.FilePath);
                        }
                        catch (Exception ex)
                        {
                            m_logger.LogWarning(ex, "Failed to delete expired cache file {FilePath}", cachedInfo.FilePath);
                        }
                    }
                }
            }

            if (expiredKeys.Count > 0)
            {
                SaveCacheIndex();
                m_logger.LogInformation("Cleared {Count} expired cache entries", expiredKeys.Count);
            }
        }

        public void ClearAllCache()
        {
            foreach (CachedImageDto cachedInfo in m_imageCache.Values)
            {
                if (File.Exists(cachedInfo.FilePath))
                {
                    try
                    {
                        File.Delete(cachedInfo.FilePath);
                    }
                    catch (Exception ex)
                    {
                        m_logger.LogWarning(ex, "Failed to delete cache file {FilePath}", cachedInfo.FilePath);
                    }
                }
            }

            m_imageCache.Clear();
            SaveCacheIndex();
            m_logger.LogInformation("Cleared all cache entries");
        }

        private static string GenerateCacheKey(string sourceUrl)
        {
            byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(sourceUrl));
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }
        private byte[] ProcessImage(byte[] imageData)
        {
            try
            {
                using SKBitmap? originalBitmap = SKBitmap.Decode(imageData);
                if (originalBitmap == null)
                {
                    m_logger.LogWarning("Failed to decode image for processing");
                    return Array.Empty<byte>();
                }

                SKBitmap? bitmapToCompress = originalBitmap;
                bool needsDisposal = false;

                if (originalBitmap.Width > HomeScreenSectionsPlugin.Instance.Configuration.MaxImageWidth)
                {
                    SKBitmap? resizedBitmap = ResizeImage(originalBitmap, HomeScreenSectionsPlugin.Instance.Configuration.MaxImageWidth);
                    if (resizedBitmap != null)
                    {
                        bitmapToCompress = resizedBitmap;
                        needsDisposal = true;
                    }
                }

                try
                {
                    return CompressImage(bitmapToCompress);
                }
                finally
                {
                    if (needsDisposal && bitmapToCompress != originalBitmap)
                    {
                        bitmapToCompress?.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "Error processing image");
                return Array.Empty<byte>();
            }
        }
        private SKBitmap? ResizeImage(SKBitmap originalBitmap, int maxWidth)
        {
            int newWidth = maxWidth;
            int newHeight = (int)((float)originalBitmap.Height / originalBitmap.Width * newWidth);
            
            SKBitmap? resizedBitmap = originalBitmap.Resize(
                new SKImageInfo(newWidth, newHeight), 
                SKSamplingOptions.Default);
            
            if (resizedBitmap == null)
            {
                m_logger.LogWarning("Failed to resize image from {OriginalWidth}x{OriginalHeight}",
                    originalBitmap.Width, originalBitmap.Height);
                return null;
            }

            m_logger.LogDebug("Resized image from {OriginalWidth}x{OriginalHeight} to {NewWidth}x{NewHeight}",
                originalBitmap.Width, originalBitmap.Height, newWidth, newHeight);
            
            return resizedBitmap;
        }

        private static byte[] CompressImage(SKBitmap bitmap)
        {
            using SKImage image = SKImage.FromBitmap(bitmap);
            using SKData data = image.Encode(SKEncodedImageFormat.Jpeg, HomeScreenSectionsPlugin.Instance.Configuration.ImageJpegQuality);
            return data.ToArray();
        }

        private static string GetExtensionFromContentType(string contentType)
        {
            return contentType.ToLowerInvariant() switch
            {
                "image/jpeg" => ".jpg",
                "image/png" => ".png",
                "image/gif" => ".gif",
                "image/webp" => ".webp",
                "image/svg+xml" => ".svg",
                _ => ".jpg"
            };
        }

        private void LoadCacheIndex()
        {
            string indexPath = Path.Combine(m_cacheDirectory, "cache-index.json");

            if (!File.Exists(indexPath))
            {
                return;
            }
            try
            {
                string json = File.ReadAllText(indexPath);
                CachedImageDto[]? entries = System.Text.Json.JsonSerializer.Deserialize<CachedImageDto[]>(json);
                
                if (entries != null)
                {
                    foreach (CachedImageDto entry in entries)
                    {
                        if (entry.ExpiresAt > DateTime.UtcNow && File.Exists(entry.FilePath))
                        {
                            m_imageCache[entry.CacheKey] = entry;
                        }
                        else if (File.Exists(entry.FilePath))
                        {
                            try
                            {
                                File.Delete(entry.FilePath);
                            }
                            catch
                            {
                                // Ignore cleanup errors
                            }
                        }
                    }
                    m_logger.LogInformation("Loaded {Count} cached images from index", m_imageCache.Count);
                }
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "Error loading cache index");
            }
        }

        private void SaveCacheIndex()
        {
            string indexPath = Path.Combine(m_cacheDirectory, "cache-index.json");
            
            try
            {
                CachedImageDto[] entries = m_imageCache.Values.ToArray();
                string json = System.Text.Json.JsonSerializer.Serialize(entries, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });
                
                File.WriteAllText(indexPath, json);
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "Error saving cache index");
            }
        }
    }
}
