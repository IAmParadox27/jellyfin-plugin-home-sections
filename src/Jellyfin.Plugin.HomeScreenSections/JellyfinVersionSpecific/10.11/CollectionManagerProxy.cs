using System.Reflection;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities.Movies;

namespace Jellyfin.Plugin.HomeScreenSections.JellyfinVersionSpecific
{
    public class CollectionManagerProxy
    {
        private readonly ICollectionManager _collectionManager;

        public CollectionManagerProxy(ICollectionManager collectionManager)
        {
            _collectionManager = collectionManager;
        }

        public IEnumerable<BoxSet> GetCollections(User user)
        {
            return _collectionManager.GetType()
                .GetMethod("GetCollections", BindingFlags.Instance | BindingFlags.NonPublic)?
                .Invoke(_collectionManager, new object?[]
                {
                    user
                }) as IEnumerable<BoxSet> ?? Enumerable.Empty<BoxSet>();
        }
    }
}