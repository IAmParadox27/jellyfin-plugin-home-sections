using System.Net;
using System.Security.Cryptography;
using System.Text;
using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Services;
using Jellyfin.Plugin.HomeScreenSections.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace Jellyfin.Plugin.HomeScreenSections.Tests.Services;

/// <summary>
/// ImageCacheService reads MaxImageCacheEntries/MaxImageWidth/ImageJpegQuality from
/// Instance.Configuration, so it runs inside the plugin fixture collection.
/// </summary>
[Collection("Plugin Instance")]
public class ImageCacheServiceTests
{
    private readonly PluginFixture _fixture;

    public ImageCacheServiceTests(PluginFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Constructor_creates_cache_directory()
    {
        _ = MakeService(FakeHttpMessageHandler.RespondingWithStatus(HttpStatusCode.OK));

        Assert.True(Directory.Exists(Path.Combine(_fixture.Paths.CachePath, "HomeScreenSections", "Images")));
    }

    [Fact]
    public void GetCachedImage_returns_nothing_for_unknown_key()
    {
        ImageCacheService service = MakeService(FakeHttpMessageHandler.RespondingWithStatus(HttpStatusCode.OK));

        (byte[]? data, string? contentType) = service.GetCachedImage("does-not-exist");

        Assert.Null(data);
        Assert.Null(contentType);
    }

    [Fact]
    public async Task GetOrCacheImage_returns_null_for_empty_source()
    {
        ImageCacheService service = MakeService(FakeHttpMessageHandler.RespondingWithStatus(HttpStatusCode.OK));

        Assert.Null(await service.GetOrCacheImage(string.Empty, 3600));
    }

    [Fact]
    public async Task GetOrCacheImage_returns_null_when_download_fails()
    {
        ImageCacheService service = MakeService(FakeHttpMessageHandler.RespondingWithStatus(HttpStatusCode.NotFound));

        string? result = await service.GetOrCacheImage("http://images.test/missing.jpg", 3600);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetOrCacheImage_caches_undecodable_bytes_verbatim_and_serves_them_back()
    {
        // Not a real image: SkiaSharp cannot decode it, so the original payload is stored as-is.
        const string sourceUrl = "http://images.test/poster.bin";
        byte[] payload = "not-an-image-but-fine-to-cache"u8.ToArray();
        FakeHttpMessageHandler handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(payload)
            {
                Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream") }
            }
        });
        ImageCacheService service = MakeService(handler);

        string? cacheKey = await service.GetOrCacheImage(sourceUrl, 3600);

        Assert.NotNull(cacheKey);
        Assert.Equal(ExpectedKey(sourceUrl), cacheKey);

        (byte[]? data, string? contentType) = service.GetCachedImage(cacheKey!);
        Assert.Equal(payload, data);
        Assert.Equal("application/octet-stream", contentType);
    }

    [Fact]
    public async Task GetOrCacheImage_second_call_is_served_from_cache()
    {
        const string sourceUrl = "http://images.test/cached.bin";
        FakeHttpMessageHandler handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([9, 9, 9])
        });
        ImageCacheService service = MakeService(handler);

        string? first = await service.GetOrCacheImage(sourceUrl, 3600);
        string? second = await service.GetOrCacheImage(sourceUrl, 3600);

        Assert.NotNull(first);
        Assert.Equal(first, second);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ClearAllCache_removes_files_and_index()
    {
        const string sourceUrl = "http://images.test/cleared.bin";
        FakeHttpMessageHandler handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([1, 2, 3])
        });
        ImageCacheService service = MakeService(handler);
        string? cacheKey = await service.GetOrCacheImage(sourceUrl, 3600);
        Assert.NotNull(cacheKey);

        service.ClearAllCache();

        Assert.Null(service.GetCachedImage(cacheKey!).data);
    }

    [Fact]
    public async Task Cache_index_survives_service_restart()
    {
        const string sourceUrl = "http://images.test/persisted.bin";
        byte[] payload = [7, 7, 7];
        FakeHttpMessageHandler handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(payload)
        });

        ImageCacheService firstService = MakeService(handler);
        string? cacheKey = await firstService.GetOrCacheImage(sourceUrl, 3600);
        Assert.NotNull(cacheKey);

        ImageCacheService secondService = MakeService(new FakeHttpMessageHandler(_ =>
            throw new InvalidOperationException("restart must not re-download")));

        (byte[]? data, _) = secondService.GetCachedImage(cacheKey!);
        Assert.Equal(payload, data);
    }

    [Fact]
    public async Task ClearExpiredCache_drops_entries_past_their_timeout()
    {
        const string sourceUrl = "http://images.test/expired.bin";
        FakeHttpMessageHandler handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([5, 5, 5])
        });
        ImageCacheService service = MakeService(handler);

        // Negative timeout: the entry is already expired the moment it is written.
        string? cacheKey = await service.GetOrCacheImage(sourceUrl, -1);
        Assert.NotNull(cacheKey);

        service.ClearExpiredCache();

        Assert.Null(service.GetCachedImage(cacheKey!).data);
    }

    [Fact]
    public async Task GetOrCacheImage_downscales_wide_images_to_jpeg()
    {
        // Wider than MaxImageWidth (600), so the SkiaSharp resize + JPEG re-encode path runs.
        byte[] png = EncodePng(width: 1200, height: 800);
        ImageCacheService service = MakeService(MakeImageHandler(png, "image/png"));

        string? cacheKey = await service.GetOrCacheImage("http://images.test/wide.png", 3600);

        Assert.NotNull(cacheKey);
        (byte[]? data, string? contentType) = service.GetCachedImage(cacheKey!);
        Assert.Equal("image/jpeg", contentType);
        Assert.NotNull(data);
        // JPEG magic bytes confirm the image was re-encoded rather than stored verbatim.
        Assert.Equal((byte)0xFF, data![0]);
        Assert.Equal((byte)0xD8, data[1]);
        // And the resize actually happened: width clamped to the configured MaxImageWidth.
        using SKBitmap decoded = SKBitmap.Decode(data);
        Assert.Equal(HomeScreenSectionsPlugin.Instance.Configuration.MaxImageWidth, decoded.Width);
    }

    [Fact]
    public async Task GetOrCacheImage_reencodes_small_images_without_resize()
    {
        byte[] png = EncodePng(width: 100, height: 50);
        ImageCacheService service = MakeService(MakeImageHandler(png, "image/png"));

        string? cacheKey = await service.GetOrCacheImage("http://images.test/small.png", 3600);

        Assert.NotNull(cacheKey);
        (byte[]? data, string? contentType) = service.GetCachedImage(cacheKey!);
        Assert.Equal("image/jpeg", contentType);
        Assert.NotNull(data);
        Assert.Equal((byte)0xFF, data![0]);
    }

    [Fact]
    public async Task GetOrCacheImage_evicts_oldest_entries_when_cache_is_full()
    {
        PluginConfiguration config = HomeScreenSectionsPlugin.Instance.Configuration;
        int originalMax = config.MaxImageCacheEntries;
        config.MaxImageCacheEntries = 10;
        try
        {
            ImageCacheService service = MakeService(MakeImageHandler([1, 2, 3], "image/jpeg"));

            string? firstKey = null;
            string? lastKey = null;
            for (int index = 0; index < 11; index++)
            {
                string? key = await service.GetOrCacheImage($"http://images.test/img-{index}.jpg", 3600);
                Assert.NotNull(key);
                if (index == 0)
                {
                    firstKey = key;
                }

                if (index == 10)
                {
                    lastKey = key;
                }
            }

            // The oldest entry was evicted to make room; the newest is still served.
            Assert.Null(service.GetCachedImage(firstKey!).data);
            Assert.NotNull(service.GetCachedImage(lastKey!).data);
        }
        finally
        {
            config.MaxImageCacheEntries = originalMax;
        }
    }

    [Fact]
    public async Task GetOrCacheImage_redownloads_expired_entry()
    {
        FakeHttpMessageHandler handler = MakeImageHandler([4, 5, 6], "image/jpeg");
        ImageCacheService service = MakeService(handler);
        const string sourceUrl = "http://images.test/expirable.jpg";

        // Negative timeout -> the entry is already expired the moment it is written.
        string? firstKey = await service.GetOrCacheImage(sourceUrl, -1);
        Assert.NotNull(firstKey);
        Assert.Single(handler.Requests);

        // Re-requesting the expired key must re-download rather than serve the stale entry.
        string? secondKey = await service.GetOrCacheImage(sourceUrl, 3600);

        Assert.NotNull(secondKey);
        Assert.Equal(firstKey, secondKey);
        Assert.Equal(2, handler.Requests.Count);
        Assert.NotNull(service.GetCachedImage(secondKey!).data);
    }

    private static FakeHttpMessageHandler MakeImageHandler(byte[] payload, string mediaType)
    {
        return new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(payload)
            {
                Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType) }
            }
        });
    }

    private static byte[] EncodePng(int width, int height)
    {
        using SKBitmap bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.CornflowerBlue);
        using SKData encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    private ImageCacheService MakeService(FakeHttpMessageHandler handler)
    {
        return new ImageCacheService(
            NullLogger<ImageCacheService>.Instance,
            _fixture.Paths,
            new HttpClient(handler));
    }

    private static string ExpectedKey(string sourceUrl)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceUrl))).ToLowerInvariant();
    }
}
