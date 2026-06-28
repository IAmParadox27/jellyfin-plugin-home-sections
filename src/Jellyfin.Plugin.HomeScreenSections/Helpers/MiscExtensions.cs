using System.Reflection;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
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
        IReadOnlyList<BaseItem> collectionFolders = libraryManager.GetItemsResult(new InternalItemsQuery()
        {
            IncludeItemTypes = new[]
            {
                BaseItemKind.CollectionFolder
            }
        }).Items;
        
        IEnumerable<VirtualFolderInfo> filtered = folders
            .Select(x =>
            {
                BaseItem? collectionFolder = collectionFolders.FirstOrDefault(y => y.Name == x.Name);

                return new VirtualFolderInfo()
                {
                    Name = x.Name,
                    CollectionType = x.CollectionType,
                    LibraryOptions = x.LibraryOptions,
                    Locations = x.Locations,
                    PrimaryImageItemId = x.PrimaryImageItemId,
                    RefreshProgress = x.RefreshProgress,
                    RefreshStatus = x.RefreshStatus,
                    ItemId = x.ItemId ?? collectionFolder?.Id.ToString() ?? Guid.Empty.ToString()
                };
            });

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
                    ItemIds = new[] { Guid.Parse(x.ItemId) }
                });

                return items.Any();
            })
            .ToArray();
    }
}
