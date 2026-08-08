using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Jellyfin.Plugin.HomeScreenSections.Helpers;
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
        private readonly ConcurrentDictionary<string, CachedImageDto> m_imageCache = new(StringComparer.Ordinal);
        private bool _indexLoaded;
#pragma warning disable MA0158 // Lock not available on net8 target (10.10.7)
        private readonly object _indexLock = new();
#pragma warning restore MA0158

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
        }

        private void EnsureIndexLoaded()
        {
            if (_indexLoaded)
            {
                return;
            }

            lock (_indexLock)
            {
                if (_indexLoaded)
                {
                    return;
                }

                LoadCacheIndex();
                _indexLoaded = true;
            }
        }

        public async Task<string?> GetOrCacheImage(string sourceUrl, int cacheTimeoutSeconds)
        {
            if (string.IsNullOrEmpty(sourceUrl))
            {
                return null;
            }

            EnsureIndexLoaded();
            string cacheKey = GenerateCacheKey(sourceUrl);

            if (IsValidCacheKey(cacheKey))
            {
                PluginLog.UsingCachedImage(m_logger, cacheKey);
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
                BestEffortIO.TryDeleteFile(
                    cachedInfo.FilePath,
                    ex => PluginLog.FailedDeleteExpiredCacheFile(m_logger, ex, cachedInfo.FilePath));
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
                PluginLog.EvictedCacheEntries(m_logger, oldestKeys.Count);
            }
        }

        private async Task<string?> DownloadAndCacheImage(string sourceUrl, string cacheKey, int cacheTimeoutSeconds)
        {
            try
            {
                PluginLog.DownloadingImage(m_logger, sourceUrl);

                using HttpResponseMessage response = await m_httpClient.GetAsync(sourceUrl);
                if (!response.IsSuccessStatusCode)
                {
                    PluginLog.ImageDownloadFailed(m_logger, sourceUrl, response.StatusCode);
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
                PluginLog.CachedImage(m_logger, cacheKey, sourceUrl);
                return cacheKey;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                PluginLog.ImageCacheError(m_logger, ex, sourceUrl);
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
            EnsureIndexLoaded();
            if (!m_imageCache.TryGetValue(cacheKey, out CachedImageDto? cachedInfo))
            {
                PluginLog.CacheMiss(m_logger, cacheKey);
                return (null, null);
            }
            if (cachedInfo.ExpiresAt < DateTime.UtcNow)
            {
                PluginLog.CacheExpired(m_logger, cacheKey);
                m_imageCache.TryRemove(cacheKey, out _);
                return (null, null);
            }
            if (!File.Exists(cachedInfo.FilePath))
            {
                PluginLog.CacheFileMissing(m_logger, cacheKey);
                m_imageCache.TryRemove(cacheKey, out _);
                return (null, null);
            }

            byte[]? data = BestEffortIO.TryReadAllBytes(
                cachedInfo.FilePath,
                ex => PluginLog.CacheReadError(m_logger, ex, cacheKey));
            return data == null ? (null, null) : (data, cachedInfo.ContentType);
        }

        public void ClearExpiredCache()
        {
            EnsureIndexLoaded();
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
                        BestEffortIO.TryDeleteFile(
                            cachedInfo.FilePath,
                            ex => PluginLog.FailedDeleteExpiredCacheFile(m_logger, ex, cachedInfo.FilePath));
                        if (!File.Exists(cachedInfo.FilePath))
                        {
                            PluginLog.DeletedExpiredCacheFile(m_logger, cachedInfo.FilePath);
                        }
                    }
                }
            }

            if (expiredKeys.Count > 0)
            {
                SaveCacheIndex();
                PluginLog.ClearedExpiredCacheEntries(m_logger, expiredKeys.Count);
            }
        }

        public void ClearAllCache()
        {
            EnsureIndexLoaded();
            foreach (CachedImageDto cachedInfo in m_imageCache.Values)
            {
                if (File.Exists(cachedInfo.FilePath))
                {
                    BestEffortIO.TryDeleteFile(
                        cachedInfo.FilePath,
                        ex => PluginLog.FailedDeleteCacheFile(m_logger, ex, cachedInfo.FilePath));
                }
            }

            m_imageCache.Clear();
            SaveCacheIndex();
            PluginLog.ClearedAllCacheEntries(m_logger);
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
                    PluginLog.ImageDecodeFailed(m_logger);
                    return [];
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
            catch (ArgumentException ex)
            {
                PluginLog.ImageProcessError(m_logger, ex);
                return [];
            }
            catch (InvalidOperationException ex)
            {
                PluginLog.ImageProcessError(m_logger, ex);
                return [];
            }
            catch (NullReferenceException ex)
            {
                PluginLog.ImageProcessError(m_logger, ex);
                return [];
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
                PluginLog.ImageResizeFailed(m_logger, originalBitmap.Width, originalBitmap.Height);
                return null;
            }

            PluginLog.ImageResized(m_logger, originalBitmap.Width, originalBitmap.Height, newWidth, newHeight);
            
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

            string? json = BestEffortIO.TryReadAllText(
                indexPath,
                ex => PluginLog.CacheIndexLoadError(m_logger, ex));
            if (json == null)
            {
                return;
            }

            try
            {
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
                            BestEffortIO.TryDeleteFile(entry.FilePath);
                        }
                    }
                    PluginLog.LoadedCacheIndex(m_logger, m_imageCache.Count);
                }
            }
            catch (System.Text.Json.JsonException ex)
            {
                PluginLog.CacheIndexLoadError(m_logger, ex);
            }
            catch (NotSupportedException ex)
            {
                PluginLog.CacheIndexLoadError(m_logger, ex);
            }
        }

        private static readonly System.Text.Json.JsonSerializerOptions s_cacheIndexJsonOptions = new()
        {
            WriteIndented = true
        };

        private void SaveCacheIndex()
        {
            string indexPath = Path.Combine(m_cacheDirectory, "cache-index.json");
            
            try
            {
                CachedImageDto[] entries = m_imageCache.Values.ToArray();
                string json = System.Text.Json.JsonSerializer.Serialize(entries, s_cacheIndexJsonOptions);
                BestEffortIO.TryWriteAllText(
                    indexPath,
                    json,
                    ex => PluginLog.CacheIndexSaveError(m_logger, ex));
            }
            catch (System.Text.Json.JsonException ex)
            {
                PluginLog.CacheIndexSaveError(m_logger, ex);
            }
            catch (NotSupportedException ex)
            {
                PluginLog.CacheIndexSaveError(m_logger, ex);
            }
        }
    }
}
