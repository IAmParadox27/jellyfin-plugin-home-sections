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
        private readonly ILogger<ImageCacheService> _logger;
        private readonly IApplicationPaths _applicationPaths;
        private readonly HttpClient _httpClient;
        private readonly string _cacheDirectory;
        // In-memory cache for quick lookups
        private readonly ConcurrentDictionary<string, CachedImageDto> _imageCache = new(StringComparer.Ordinal);
        private bool _indexLoaded;
#pragma warning disable MA0158 // Lock not available on net8 target (10.10.7)
        private readonly object _indexLock = new();
#pragma warning restore MA0158

        public ImageCacheService(
            ILogger<ImageCacheService> logger,
            IApplicationPaths applicationPaths,
            HttpClient httpClient)
        {
            _logger = logger;
            _applicationPaths = applicationPaths;
            _httpClient = httpClient;
            _cacheDirectory = Path.Combine(applicationPaths.CachePath, "HomeScreenSections", "Images");
            Directory.CreateDirectory(_cacheDirectory);
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
                PluginLog.UsingCachedImage(_logger, cacheKey);
                return cacheKey;
            }

            if (_imageCache.ContainsKey(cacheKey))
            {
                CleanupCacheEntry(cacheKey);
            }
            if (_imageCache.Count >= HomeScreenSectionsPlugin.Instance.Configuration.MaxImageCacheEntries)
            {
                EvictOldEntries();
            }
            return await DownloadAndCacheImage(sourceUrl, cacheKey, cacheTimeoutSeconds);
        }

        private bool IsValidCacheKey(string cacheKey)
        {
            if (!_imageCache.TryGetValue(cacheKey, out CachedImageDto? cachedInfo))
            {
                return false;
            }
            return cachedInfo.ExpiresAt > DateTime.UtcNow && File.Exists(cachedInfo.FilePath);
        }

        private void CleanupCacheEntry(string cacheKey)
        {
            if (!_imageCache.TryRemove(cacheKey, out CachedImageDto? cachedInfo))
            {
                return;
            }
            if (File.Exists(cachedInfo.FilePath))
            {
                BestEffortIO.TryDeleteFile(
                    cachedInfo.FilePath,
                    ex => PluginLog.FailedDeleteExpiredCacheFile(_logger, ex, cachedInfo.FilePath));
            }
        }

        private void EvictOldEntries()
        {
            List<string> oldestKeys = _imageCache.Values
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
                PluginLog.EvictedCacheEntries(_logger, oldestKeys.Count);
            }
        }

        private async Task<string?> DownloadAndCacheImage(string sourceUrl, string cacheKey, int cacheTimeoutSeconds)
        {
            try
            {
                PluginLog.DownloadingImage(_logger, sourceUrl);

                using HttpResponseMessage response = await _httpClient.GetAsync(sourceUrl);
                if (!response.IsSuccessStatusCode)
                {
                    PluginLog.ImageDownloadFailed(_logger, sourceUrl, response.StatusCode);
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
                PluginLog.CachedImage(_logger, cacheKey, sourceUrl);
                return cacheKey;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                PluginLog.ImageCacheError(_logger, ex, sourceUrl);
                return null;
            }
        }

        private string SaveImageToDisk(string cacheKey, byte[] imageData, string contentType)
        {
            string extension = GetExtensionFromContentType(contentType);
            string filePath = Path.Combine(_cacheDirectory, $"{cacheKey}{extension}");
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
            _imageCache[cacheKey] = newCacheInfo;
            SaveCacheIndex();
        }

        public (byte[]? data, string? contentType) GetCachedImage(string cacheKey)
        {
            EnsureIndexLoaded();
            if (!_imageCache.TryGetValue(cacheKey, out CachedImageDto? cachedInfo))
            {
                PluginLog.CacheMiss(_logger, cacheKey);
                return (null, null);
            }
            if (cachedInfo.ExpiresAt < DateTime.UtcNow)
            {
                PluginLog.CacheExpired(_logger, cacheKey);
                _imageCache.TryRemove(cacheKey, out _);
                return (null, null);
            }
            if (!File.Exists(cachedInfo.FilePath))
            {
                PluginLog.CacheFileMissing(_logger, cacheKey);
                _imageCache.TryRemove(cacheKey, out _);
                return (null, null);
            }

            byte[]? data = BestEffortIO.TryReadAllBytes(
                cachedInfo.FilePath,
                ex => PluginLog.CacheReadError(_logger, ex, cacheKey));
            return data == null ? (null, null) : (data, cachedInfo.ContentType);
        }

        public void ClearExpiredCache()
        {
            EnsureIndexLoaded();
            List<string> expiredKeys = _imageCache
                .Where(kvp => kvp.Value.ExpiresAt < DateTime.UtcNow)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (string key in expiredKeys)
            {
                if (_imageCache.TryRemove(key, out CachedImageDto? cachedInfo))
                {
                    if (File.Exists(cachedInfo.FilePath))
                    {
                        BestEffortIO.TryDeleteFile(
                            cachedInfo.FilePath,
                            ex => PluginLog.FailedDeleteExpiredCacheFile(_logger, ex, cachedInfo.FilePath));
                        if (!File.Exists(cachedInfo.FilePath))
                        {
                            PluginLog.DeletedExpiredCacheFile(_logger, cachedInfo.FilePath);
                        }
                    }
                }
            }

            if (expiredKeys.Count > 0)
            {
                SaveCacheIndex();
                PluginLog.ClearedExpiredCacheEntries(_logger, expiredKeys.Count);
            }
        }

        public void ClearAllCache()
        {
            EnsureIndexLoaded();
            foreach (CachedImageDto cachedInfo in _imageCache.Values)
            {
                if (File.Exists(cachedInfo.FilePath))
                {
                    BestEffortIO.TryDeleteFile(
                        cachedInfo.FilePath,
                        ex => PluginLog.FailedDeleteCacheFile(_logger, ex, cachedInfo.FilePath));
                }
            }

            _imageCache.Clear();
            SaveCacheIndex();
            PluginLog.ClearedAllCacheEntries(_logger);
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
                    PluginLog.ImageDecodeFailed(_logger);
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
                PluginLog.ImageProcessError(_logger, ex);
                return [];
            }
            catch (InvalidOperationException ex)
            {
                PluginLog.ImageProcessError(_logger, ex);
                return [];
            }
            catch (NullReferenceException ex)
            {
                PluginLog.ImageProcessError(_logger, ex);
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
                PluginLog.ImageResizeFailed(_logger, originalBitmap.Width, originalBitmap.Height);
                return null;
            }

            PluginLog.ImageResized(_logger, originalBitmap.Width, originalBitmap.Height, newWidth, newHeight);
            
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
            string indexPath = Path.Combine(_cacheDirectory, "cache-index.json");

            if (!File.Exists(indexPath))
            {
                return;
            }

            string? json = BestEffortIO.TryReadAllText(
                indexPath,
                ex => PluginLog.CacheIndexLoadError(_logger, ex));
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
                            _imageCache[entry.CacheKey] = entry;
                        }
                        else if (File.Exists(entry.FilePath))
                        {
                            BestEffortIO.TryDeleteFile(entry.FilePath);
                        }
                    }
                    PluginLog.LoadedCacheIndex(_logger, _imageCache.Count);
                }
            }
            catch (System.Text.Json.JsonException ex)
            {
                PluginLog.CacheIndexLoadError(_logger, ex);
            }
            catch (NotSupportedException ex)
            {
                PluginLog.CacheIndexLoadError(_logger, ex);
            }
        }

        private static readonly System.Text.Json.JsonSerializerOptions s_cacheIndexJsonOptions = new()
        {
            WriteIndented = true
        };

        private void SaveCacheIndex()
        {
            string indexPath = Path.Combine(_cacheDirectory, "cache-index.json");
            
            try
            {
                CachedImageDto[] entries = _imageCache.Values.ToArray();
                string json = System.Text.Json.JsonSerializer.Serialize(entries, s_cacheIndexJsonOptions);
                BestEffortIO.TryWriteAllText(
                    indexPath,
                    json,
                    ex => PluginLog.CacheIndexSaveError(_logger, ex));
            }
            catch (System.Text.Json.JsonException ex)
            {
                PluginLog.CacheIndexSaveError(_logger, ex);
            }
            catch (NotSupportedException ex)
            {
                PluginLog.CacheIndexSaveError(_logger, ex);
            }
        }
    }
}
