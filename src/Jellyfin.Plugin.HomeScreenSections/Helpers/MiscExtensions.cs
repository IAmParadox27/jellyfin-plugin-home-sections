using System.Reflection;
using Jellyfin.Data;
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
        // Virtual folders can have a null/invalid ItemId; Guid.Parse would throw
        // ArgumentNullException('input') and take down every section that filters folders
        // (upstream #182). Drop them instead, matching the behaviour of the section queries.
        IEnumerable<VirtualFolderInfo> filtered = folders.Where(x => Guid.TryParse(x.ItemId, out _));

        if (user != null)
        {
            Guid[] latestItemExcludes = user.GetPreferenceValues<Guid>(PreferenceKind.LatestItemExcludes);
            filtered = filtered.Where(x => !latestItemExcludes.Contains(Guid.Parse(x.ItemId)));
        }

        return filtered
            .Where(x =>
            {
                IEnumerable<BaseItem> items = libraryManager.GetItemList(new InternalItemsQuery(user)
                {
                    ItemIds = [Guid.Parse(x.ItemId)]
                });

                return items.Any();
            })
            .ToArray();
    }
}
