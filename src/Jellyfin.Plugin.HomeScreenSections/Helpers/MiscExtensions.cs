using System.Reflection;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

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

    public static VirtualFolderInfo[] FilterToUserPermitted(this IEnumerable<VirtualFolderInfo> folders, ILibraryManager libraryManager, User? user)
    {
        return folders
            .Where(x =>
            {
                IEnumerable<BaseItem> items = libraryManager.GetItemList(new InternalItemsQuery(user)
                {
                    ItemIds = new[] { Guid.Parse(x.ItemId) }
                });

                return items.Any();
            })
            .ToArray();
    }
}