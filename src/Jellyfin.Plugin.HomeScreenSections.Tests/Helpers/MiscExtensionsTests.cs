using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.HomeScreenSections.Helpers;
using Jellyfin.Plugin.HomeScreenSections.Tests.Support;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Moq;

namespace Jellyfin.Plugin.HomeScreenSections.Tests.Helpers;

public class MiscExtensionsTests
{
    [Fact]
    public void GetCollections_invokes_non_public_implementation()
    {
        BoxSet first = new BoxSet { Name = "Collection One" };
        BoxSet second = new BoxSet { Name = "Collection Two" };
        FakeCollectionManager collectionManager = new FakeCollectionManager([first, second]);
        User user = new("Viewer", "AuthProvider", "PasswordResetProvider");

        List<BoxSet> result = [.. collectionManager.GetCollections(user)];

        Assert.Equal(2, result.Count);
        Assert.Same(first, result[0]);
        Assert.Same(second, result[1]);
    }

    [Fact]
    public void GetCollections_returns_empty_when_implementation_missing()
    {
        // Moq proxies have no non-public GetCollections, so the reflection fallback must yield nothing.
        Mock<ICollectionManager> collectionManager = new();
        User user = new("Viewer", "AuthProvider", "PasswordResetProvider");

        Assert.Empty(collectionManager.Object.GetCollections(user));
    }

    [Fact]
    public void FilterToUserPermitted_keeps_only_folders_backed_by_library_items()
    {
        Guid accessibleId = Guid.NewGuid();
        Guid inaccessibleId = Guid.NewGuid();
        Movie accessibleItem = new Movie { Name = "Accessible" };

        Mock<ILibraryManager> libraryManager = new();
        libraryManager
            .Setup(manager => manager.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns((InternalItemsQuery query) =>
                query.ItemIds != null && query.ItemIds.Contains(accessibleId)
                    ? new BaseItem[] { accessibleItem }
                    : []);

        VirtualFolderInfo[] folders =
        [
            new VirtualFolderInfo { Name = "Movies", ItemId = accessibleId.ToString(), Locations = ["/media/movies"] },
            new VirtualFolderInfo { Name = "Gone", ItemId = inaccessibleId.ToString(), Locations = ["/media/gone"] }
        ];

        VirtualFolderInfo[] result = folders.FilterToUserPermitted(libraryManager.Object, user: null);

        VirtualFolderInfo kept = Assert.Single(result);
        Assert.Equal("Movies", kept.Name);
    }

    [Fact]
    public void FilterToUserPermitted_runs_library_check_per_user()
    {
        Guid accessibleId = Guid.NewGuid();
        Movie accessibleItem = new Movie { Name = "Accessible" };

        Mock<ILibraryManager> libraryManager = new();
        libraryManager
            .Setup(manager => manager.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns((InternalItemsQuery query) =>
                query.ItemIds != null && query.ItemIds.Contains(accessibleId)
                    ? new BaseItem[] { accessibleItem }
                    : []);

        VirtualFolderInfo[] folders =
        [
            new VirtualFolderInfo { Name = "Movies", ItemId = accessibleId.ToString(), Locations = ["/media/movies"] }
        ];
        User user = new("Viewer", "AuthProvider", "PasswordResetProvider");

        VirtualFolderInfo[] result = folders.FilterToUserPermitted(libraryManager.Object, user);

        Assert.Single(result);
    }
}
