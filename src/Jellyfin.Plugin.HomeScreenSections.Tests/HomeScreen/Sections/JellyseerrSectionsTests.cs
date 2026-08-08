using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using Jellyfin.Plugin.HomeScreenSections.Services;
using Jellyfin.Plugin.HomeScreenSections.Tests.Support;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.HomeScreenSections.Tests.HomeScreen.Sections;

/// <summary>
/// End-to-end tests for the Jellyseerr-backed sections: the sections build their own
/// HttpClient pointed at Instance.Configuration.JellyseerrUrl, so a loopback listener
/// plays Jellyseerr.
/// </summary>
[Collection("Plugin Instance")]
public class JellyseerrSectionsTests : IDisposable
{
    private const string Username = "JellyseerrUser";

    private readonly Mock<IUserManager> _userManager = new();
    private readonly Mock<ILibraryManager> _libraryManager = new();
    private readonly Mock<IDtoService> _dtoService = new();
    private readonly User _user = new(Username, "AuthProvider", "PasswordResetProvider");
    private readonly Guid _userId = Guid.NewGuid();
    private readonly JellyseerrFakeServer _server;
    private readonly ImageCacheService _imageCacheService;

    private string? _originalJellyseerrUrl;
    private string? _originalExternalUrl;
    private string? _originalApiKey;
    private string? _originalLanguages;

    public JellyseerrSectionsTests(PluginFixture fixture)
    {
        _ = fixture;
        _server = JellyseerrFakeServer.Start(Respond);
        _imageCacheService = new ImageCacheService(
            NullLogger<ImageCacheService>.Instance,
            fixture.Paths,
            new HttpClient(FakeHttpMessageHandler.RespondingWithStatus(System.Net.HttpStatusCode.NotFound)));

        _userManager
            .Setup(manager => manager.GetUserById(_userId))
            .Returns(_user);

        TestDtos.StubPassthrough(_dtoService);

        UseFakeJellyseerr();
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        RestoreJellyseerrConfig();
        _server.Dispose();
    }

    private void UseFakeJellyseerr()
    {
        PluginConfiguration config = HomeScreenSectionsPlugin.Instance.Configuration;
        _originalJellyseerrUrl = config.JellyseerrUrl;
        _originalExternalUrl = config.JellyseerrExternalUrl;
        _originalApiKey = config.JellyseerrApiKey;
        _originalLanguages = config.JellyseerrPreferredLanguages;

        config.JellyseerrUrl = _server.BaseUrl;
        config.JellyseerrExternalUrl = string.Empty;
        config.JellyseerrApiKey = "test-key";
        config.JellyseerrPreferredLanguages = "en";
    }

    private void RestoreJellyseerrConfig()
    {
        PluginConfiguration config = HomeScreenSectionsPlugin.Instance.Configuration;
        config.JellyseerrUrl = _originalJellyseerrUrl;
        config.JellyseerrExternalUrl = _originalExternalUrl;
        config.JellyseerrApiKey = _originalApiKey;
        config.JellyseerrPreferredLanguages = _originalLanguages;
    }

    private static (int StatusCode, string Json) Respond(string pathAndQuery)
    {
        if (pathAndQuery.StartsWith("/api/v1/user?", StringComparison.Ordinal))
        {
            return (200, $$"""
                { "results": [ { "id": 7, "jellyfinUsername": "{{Username}}" } ] }
                """);
        }

        if (pathAndQuery.StartsWith("/api/v1/user/7/requests", StringComparison.Ordinal))
        {
            Guid libraryItemId = RequestedLibraryItemId;
            return (200, $$"""
                {
                    "results": [
                        { "media": { "jellyfinMediaId": "{{libraryItemId}}" } },
                        { "media": { "jellyfinMediaId": null } },
                        { "media": null }
                    ]
                }
                """);
        }

        if (pathAndQuery.StartsWith("/api/v1/discover/", StringComparison.Ordinal))
        {
            JArray results = [];
            for (int index = 0; index < 22; index++)
            {
                results.Add(new JObject
                {
                    ["id"] = 100 + index,
                    ["mediaType"] = "movie",
                    ["title"] = $"Discover Item {index}",
                    ["originalLanguage"] = "en",
                    ["releaseDate"] = "2026-05-01",
                    ["posterPath"] = $"/poster{index}.jpg",
                    ["vote_average"] = 7.5
                });
            }

            // Filtered: wrong language.
            results.Add(new JObject
            {
                ["id"] = 900,
                ["title"] = "Foreign Item",
                ["originalLanguage"] = "fr",
                ["releaseDate"] = "2026-05-01"
            });
            // Skipped: already has media info (exists in Jellyfin).
            results.Add(new JObject
            {
                ["id"] = 901,
                ["title"] = "Already Requested",
                ["originalLanguage"] = "en",
                ["mediaInfo"] = new JObject { ["id"] = 1 }
            });

            return (200, new JObject { ["results"] = results }.ToString());
        }

        return (404, "{}");
    }

    private static Guid RequestedLibraryItemId { get; } = Guid.NewGuid();

    [Fact]
    public void Discover_returns_mapped_items_and_applies_language_and_media_filters()
    {
        DiscoverSection section = new DiscoverSection(_userManager.Object, _imageCacheService);

        QueryResult<BaseItemDto> result = section.GetResults(new HomeScreenSectionPayload { UserId = _userId }, new FakeQueryCollection());

        // 22 english items pass; the fr item and the mediaInfo item are dropped.
        Assert.Equal(22, result.Items.Count);
        Assert.Equal(22, result.TotalRecordCount);

        BaseItemDto first = result.Items[0];
        Assert.Equal("Discover Item 0", first.Name);
        Assert.NotNull(first.PremiereDate);
        Assert.Equal(7.5f, first.CommunityRating);
        Assert.Equal("100", first.ProviderIds!["Jellyseerr"]);
        Assert.Equal(_server.BaseUrl, first.ProviderIds["JellyseerrRoot"]);
        Assert.Contains("poster0.jpg", first.ProviderIds["JellyseerrPoster"], StringComparison.Ordinal);
    }

    [Fact]
    public void Discover_uses_external_url_for_links_when_configured()
    {
        HomeScreenSectionsPlugin.Instance.Configuration.JellyseerrExternalUrl = "https://requests.example.com/";
        DiscoverSection section = new DiscoverSection(_userManager.Object, _imageCacheService);

        QueryResult<BaseItemDto> result = section.GetResults(new HomeScreenSectionPayload { UserId = _userId }, new FakeQueryCollection());

        Assert.Equal("https://requests.example.com/", result.Items[0].ProviderIds!["JellyseerrRoot"]);
    }

    [Fact]
    public void Discover_includes_other_preferred_languages()
    {
        HomeScreenSectionsPlugin.Instance.Configuration.JellyseerrPreferredLanguages = "en, fr";
        DiscoverSection section = new DiscoverSection(_userManager.Object, _imageCacheService);

        QueryResult<BaseItemDto> result = section.GetResults(new HomeScreenSectionPayload { UserId = _userId }, new FakeQueryCollection());

        // The fr item is allowed now; only the mediaInfo item stays skipped.
        Assert.Equal(23, result.Items.Count);
    }

    [Fact]
    public void Discover_returns_empty_when_jellyseerr_not_configured()
    {
        HomeScreenSectionsPlugin.Instance.Configuration.JellyseerrUrl = string.Empty;
        DiscoverSection section = new DiscoverSection(_userManager.Object, _imageCacheService);

        Assert.Empty(section.GetResults(new HomeScreenSectionPayload { UserId = _userId }, new FakeQueryCollection()).Items);
    }

    [Fact]
    public void Discover_returns_empty_when_user_unknown()
    {
        _userManager.Setup(manager => manager.GetUserById(_userId)).Returns((User?)null);
        DiscoverSection section = new DiscoverSection(_userManager.Object, _imageCacheService);

        Assert.Empty(section.GetResults(new HomeScreenSectionPayload { UserId = _userId }, new FakeQueryCollection()).Items);
    }

    [Theory]
    [InlineData("DiscoverMovies", "/api/v1/discover/movies")]
    [InlineData("DiscoverTV", "/api/v1/discover/tv")]
    public void Discover_subclasses_hit_their_own_endpoints(string sectionKind, string expectedEndpoint)
    {
        DiscoverSection section = string.Equals(sectionKind, "DiscoverMovies", StringComparison.Ordinal)
            ? new DiscoverMoviesSection(_userManager.Object, _imageCacheService)
            : new DiscoverTvSection(_userManager.Object, _imageCacheService);

        QueryResult<BaseItemDto> result = section.GetResults(new HomeScreenSectionPayload { UserId = _userId }, new FakeQueryCollection());

        Assert.Equal(22, result.Items.Count);
        Assert.Contains(_server.RequestsReceived, request => request.StartsWith(expectedEndpoint, StringComparison.Ordinal));
    }

    [Fact]
    public void Discover_metadata_reports_portrait_locked_rows()
    {
        DiscoverSection section = new DiscoverSection(_userManager.Object, _imageCacheService);

        HomeScreenSectionInfo info = section.GetInfo();

        Assert.Equal("Discover", info.Section);
        Assert.Equal(SectionViewMode.Portrait, info.ViewMode);
        Assert.False(info.AllowViewModeChange);
        Assert.Same(section, Assert.Single(section.CreateInstances(_userId, 1)));
    }

    [Fact]
    public void MyRequests_returns_library_items_for_pending_requests()
    {
        Guid folderId = Guid.NewGuid();
        Movie requestedMovie = new Movie { Id = RequestedLibraryItemId, Name = "Requested Movie" };

        InternalItemsQuery? capturedQuery = null;
        _libraryManager
            .Setup(manager => manager.GetVirtualFolders())
            .Returns([new MediaBrowser.Model.Entities.VirtualFolderInfo { ItemId = folderId.ToString(), Name = "Movies", Locations = ["/media/movies"] }]);
        _libraryManager
            .Setup(manager => manager.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(query => capturedQuery = query)
            .Returns(new BaseItem[] { requestedMovie });

        MyRequestsSection section = new MyRequestsSection(_userManager.Object, _libraryManager.Object, _dtoService.Object);

        QueryResult<BaseItemDto> result = section.GetResults(new HomeScreenSectionPayload { UserId = _userId }, new FakeQueryCollection());

        Assert.Equal("Requested Movie", Assert.Single(result.Items).Name);
        Assert.Contains(_server.RequestsReceived, request => request.StartsWith("/api/v1/user/7/requests", StringComparison.Ordinal));
        // The library lookup must be constrained to the requested Jellyfin media ids, not the whole library.
        Assert.NotNull(capturedQuery);
        Assert.NotNull(capturedQuery!.ItemIds);
        Assert.Contains(RequestedLibraryItemId, capturedQuery!.ItemIds);
    }

    [Fact]
    public void MyRequests_returns_empty_when_jellyseerr_not_configured()
    {
        HomeScreenSectionsPlugin.Instance.Configuration.JellyseerrUrl = string.Empty;
        MyRequestsSection section = new MyRequestsSection(_userManager.Object, _libraryManager.Object, _dtoService.Object);

        Assert.Empty(section.GetResults(new HomeScreenSectionPayload { UserId = _userId }, new FakeQueryCollection()).Items);
    }

    [Fact]
    public void MyRequests_returns_empty_when_user_unknown()
    {
        _userManager.Setup(manager => manager.GetUserById(_userId)).Returns((User?)null);
        MyRequestsSection section = new MyRequestsSection(_userManager.Object, _libraryManager.Object, _dtoService.Object);

        Assert.Empty(section.GetResults(new HomeScreenSectionPayload { UserId = _userId }, new FakeQueryCollection()).Items);
    }

    [Fact]
    public void MyRequests_returns_empty_when_no_library_items_match()
    {
        _libraryManager
            .Setup(manager => manager.GetVirtualFolders())
            .Returns([]);

        MyRequestsSection section = new MyRequestsSection(_userManager.Object, _libraryManager.Object, _dtoService.Object);

        Assert.Empty(section.GetResults(new HomeScreenSectionPayload { UserId = _userId }, new FakeQueryCollection()).Items);
    }

    [Fact]
    public void MyRequests_GetInfo_reports_landscape_row()
    {
        MyRequestsSection section = new MyRequestsSection(_userManager.Object, _libraryManager.Object, _dtoService.Object);

        HomeScreenSectionInfo info = section.GetInfo();

        Assert.Equal("MyJellyseerrRequests", info.Section);
        Assert.Equal(SectionViewMode.Landscape, info.ViewMode);
        Assert.True(info.AllowViewModeChange);
        Assert.True(info.AllowHideWatched);
    }
}
