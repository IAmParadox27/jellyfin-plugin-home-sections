using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections;
using Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections.Latest;
using Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections.RecentlyAdded;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using Jellyfin.Plugin.HomeScreenSections.Tests.Support;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Moq;

namespace Jellyfin.Plugin.HomeScreenSections.Tests.HomeScreen.Sections;

/// <summary>
/// Covers the shared Latest/RecentlyAdded section bases through their movies concrete
/// classes. Folder.GetItems is not virtual, so result pipelines are exercised with empty
/// folder sets while CreateInstances/GetInfo are fully mocked.
/// </summary>
public class LatestAndRecentlyAddedSectionTests : IDisposable
{
    private readonly Mock<IUserViewManager> m_userViewManager = new();
    private readonly Mock<IUserManager> m_userManager = new();
    private readonly Mock<ILibraryManager> m_libraryManager = new();
    private readonly Mock<IDtoService> m_dtoService = new();
    private readonly TestServiceProvider m_serviceProvider;
    private readonly FakeApplicationPaths m_paths;
    private readonly User m_user = new("LibraryViewer", "AuthProvider", "PasswordResetProvider");

    public LatestAndRecentlyAddedSectionTests()
    {
        m_paths = new FakeApplicationPaths(Path.Combine(Path.GetTempPath(), "hss-section-tests", Guid.NewGuid().ToString("N")));
        m_serviceProvider = new TestServiceProvider(m_paths);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        TestIO.DeleteBestEffort(m_paths.Root);
    }

    [Fact]
    public void LatestMovies_GetResults_without_libraries_returns_empty()
    {
        LatestMoviesSection section = MakeLatestSection();
        m_libraryManager
            .Setup(manager => manager.GetVirtualFolders())
            .Returns([]);

        QueryResult<BaseItemDto> result = section.GetResults(new HomeScreenSectionPayload { UserId = Guid.NewGuid() }, new FakeQueryCollection());

        Assert.Empty(result.Items);
    }

    [Fact]
    public void LatestMovies_CreateInstances_links_matching_library_folder()
    {
        Guid userId = Guid.NewGuid();
        m_userManager
            .Setup(manager => manager.GetUserById(userId))
            .Returns(m_user);

        Mock<Folder> moviesFolder = new();
        moviesFolder
            .As<ICollectionFolder>()
            .Setup(folder => folder.CollectionType)
            .Returns(CollectionType.movies);

        Mock<Folder> rootFolder = new();
        rootFolder
            .Setup(folder => folder.GetChildren(It.IsAny<User>(), true, It.IsAny<InternalItemsQuery>()))
            .Returns(new BaseItem[] { moviesFolder.Object });

        m_libraryManager
            .Setup(manager => manager.GetUserRootFolder())
            .Returns(rootFolder.Object);

        BaseItemDto folderDto = new BaseItemDto { Id = Guid.NewGuid(), Name = "Movies Library" };
        m_dtoService
            .Setup(service => service.GetBaseItemDto(moviesFolder.Object, It.IsAny<DtoOptions>(), m_user, It.IsAny<BaseItem>()))
            .Returns(folderDto);

        LatestMoviesSection section = MakeLatestSection();
        section.DisplayText = "Renamed Latest";

        List<IHomeScreenSection> instances = [.. section.CreateInstances(userId, 1)];

        LatestMoviesSection instance = Assert.IsType<LatestMoviesSection>(Assert.Single(instances));
        Assert.NotSame(section, instance);
        Assert.Equal("Renamed Latest", instance.DisplayText);
        Assert.Same(folderDto, instance.OriginalPayload);
    }

    [Fact]
    public void LatestMovies_CreateInstances_without_folders_has_no_payload()
    {
        Guid userId = Guid.NewGuid();
        m_userManager
            .Setup(manager => manager.GetUserById(userId))
            .Returns(m_user);

        Mock<Folder> rootFolder = new();
        rootFolder
            .Setup(folder => folder.GetChildren(It.IsAny<User>(), true, It.IsAny<InternalItemsQuery>()))
            .Returns(Array.Empty<BaseItem>());
        m_libraryManager
            .Setup(manager => manager.GetUserRootFolder())
            .Returns(rootFolder.Object);

        LatestMoviesSection section = MakeLatestSection();

        LatestMoviesSection instance = Assert.IsType<LatestMoviesSection>(Assert.Single(section.CreateInstances(userId, 1)));
        Assert.Null(instance.OriginalPayload);
    }

    [Fact]
    public void LatestMovies_GetInfo_uses_landscape_and_allows_hide_watched()
    {
        LatestMoviesSection section = MakeLatestSection();

        HomeScreenSectionInfo info = section.GetInfo();

        Assert.Equal("LatestMovies", info.Section);
        Assert.Equal("movies", info.Route);
        Assert.Equal(SectionViewMode.Landscape, info.ViewMode);
        Assert.True(info.AllowHideWatched);
    }

    [Fact]
    public void RecentlyAddedMovies_GetResults_without_libraries_returns_empty()
    {
        RecentlyAddedMoviesSection section = MakeRecentlyAddedSection();
        m_libraryManager
            .Setup(manager => manager.GetVirtualFolders())
            .Returns([]);

        QueryResult<BaseItemDto> result = section.GetResults(new HomeScreenSectionPayload { UserId = Guid.NewGuid() }, new FakeQueryCollection());

        Assert.Empty(result.Items);
    }

    [Fact]
    public void RecentlyAddedMovies_CreateInstances_copies_metadata_and_payload()
    {
        Guid userId = Guid.NewGuid();
        m_userManager
            .Setup(manager => manager.GetUserById(userId))
            .Returns(m_user);

        Mock<Folder> moviesFolder = new();
        moviesFolder
            .As<ICollectionFolder>()
            .Setup(folder => folder.CollectionType)
            .Returns(CollectionType.movies);

        Mock<Folder> rootFolder = new();
        rootFolder
            .Setup(folder => folder.GetChildren(It.IsAny<User>(), true, It.IsAny<InternalItemsQuery>()))
            .Returns(new BaseItem[] { moviesFolder.Object });
        m_libraryManager
            .Setup(manager => manager.GetUserRootFolder())
            .Returns(rootFolder.Object);

        BaseItemDto folderDto = new BaseItemDto { Id = Guid.NewGuid(), Name = "Movies" };
        m_dtoService
            .Setup(service => service.GetBaseItemDto(moviesFolder.Object, It.IsAny<DtoOptions>(), m_user, It.IsAny<BaseItem>()))
            .Returns(folderDto);

        RecentlyAddedMoviesSection section = MakeRecentlyAddedSection();

        List<IHomeScreenSection> instances = [.. section.CreateInstances(userId, 1)];

        RecentlyAddedMoviesSection instance = Assert.IsType<RecentlyAddedMoviesSection>(Assert.Single(instances));
        Assert.NotSame(section, instance);
        Assert.Equal("Recently Added Movies", instance.DisplayText);
        Assert.Equal("movies", instance.AdditionalData);
        Assert.Same(folderDto, instance.OriginalPayload);
    }

    [Fact]
    public void RecentlyAddedMovies_GetInfo_reports_route_and_view_mode()
    {
        RecentlyAddedMoviesSection section = MakeRecentlyAddedSection();

        HomeScreenSectionInfo info = section.GetInfo();

        Assert.Equal("RecentlyAddedMovies", info.Section);
        Assert.Equal("movies", info.Route);
        Assert.Equal(SectionViewMode.Landscape, info.ViewMode);
        Assert.True(info.AllowHideWatched);
    }

    private LatestMoviesSection MakeLatestSection()
    {
        return new LatestMoviesSection(
            m_userViewManager.Object,
            m_userManager.Object,
            m_libraryManager.Object,
            m_dtoService.Object,
            m_serviceProvider);
    }

    private RecentlyAddedMoviesSection MakeRecentlyAddedSection()
    {
        return new RecentlyAddedMoviesSection(
            m_userViewManager.Object,
            m_userManager.Object,
            m_libraryManager.Object,
            m_dtoService.Object,
            m_serviceProvider);
    }
}
