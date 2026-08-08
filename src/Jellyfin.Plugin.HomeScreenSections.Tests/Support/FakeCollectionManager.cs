using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;

namespace Jellyfin.Plugin.HomeScreenSections.Tests.Support;

/// <summary>
/// ICollectionManager fake that carries a private GetCollections(User) method, which is
/// exactly what MiscExtensions/CollectionManagerProxy locate via reflection.
/// </summary>
public sealed class FakeCollectionManager : ICollectionManager
{
    private readonly IEnumerable<BoxSet> _collections;

    public FakeCollectionManager(IEnumerable<BoxSet> collections)
    {
        _collections = collections;
    }

#pragma warning disable CS0067 // Events satisfy ICollectionManager but are never raised here.
    public event EventHandler<CollectionCreatedEventArgs>? CollectionCreated;

    public event EventHandler<CollectionModifiedEventArgs>? ItemsAddedToCollection;

    public event EventHandler<CollectionModifiedEventArgs>? ItemsRemovedFromCollection;
#pragma warning restore CS0067

    public Task<BoxSet> CreateCollectionAsync(CollectionCreationOptions options)
    {
        return Task.FromResult(new BoxSet());
    }

    public Task AddToCollectionAsync(Guid collectionId, IEnumerable<Guid> itemIds)
    {
        return Task.CompletedTask;
    }

    public Task RemoveFromCollectionAsync(Guid collectionId, IEnumerable<Guid> itemIds)
    {
        return Task.CompletedTask;
    }

    public IEnumerable<BaseItem> CollapseItemsWithinBoxSets(IEnumerable<BaseItem> items, User user)
    {
        return items;
    }

    public Task<Folder?> GetCollectionsFolder(bool createIfNeeded)
    {
        return Task.FromResult<Folder?>(null);
    }

    private IEnumerable<BoxSet> GetCollections(User user)
    {
        return _collections;
    }
}
