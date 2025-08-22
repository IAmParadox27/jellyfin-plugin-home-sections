using System.Reflection;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities.Movies;

namespace Jellyfin.Plugin.HomeScreenSections.Helpers;

public static class MiscExtensions
{
    public static IEnumerable<BoxSet> GetCollections(this ICollectionManager collectionManager, User user)
    {
        return collectionManager.GetType()
            .GetMethod("GetCollections", BindingFlags.Instance | BindingFlags.NonPublic)?
            .Invoke(collectionManager, new object?[]
            {
                user
            }) as IEnumerable<BoxSet> ?? Enumerable.Empty<BoxSet>();
    }
}