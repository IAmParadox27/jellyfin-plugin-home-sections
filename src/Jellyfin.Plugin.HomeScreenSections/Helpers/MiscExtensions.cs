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

    public static bool IsMixedFolder(this VirtualFolderInfo folder, ILibraryManager libraryManager)
    {
        if (HomeScreenSectionsPlugin.Instance.CollectionFolderMixedStatus.TryGetValue(folder.Name, out bool mixedFolder))
        {
            return mixedFolder;
        }
        
        IReadOnlyList<BaseItem> collectionFolders = libraryManager.GetItemsResult(new InternalItemsQuery()
        {
            IncludeItemTypes = new[]
            {
                BaseItemKind.CollectionFolder
            }
        }).Items;
        
        BaseItem? collectionFolder = collectionFolders.FirstOrDefault(x => x.Name == folder.Name);

        bool hasEpisodes = libraryManager.GetItemsResult(new InternalItemsQuery()
        {
            IncludeItemTypes = new[]
            {
                BaseItemKind.Episode
            },
            Limit = 1,
            ParentId = collectionFolder?.Id ?? Guid.Empty,
            Recursive = true,
            EnableTotalRecordCount = false
        }).Items.Any();
        bool hasMovies = libraryManager.GetItemsResult(new InternalItemsQuery()
        {
            IncludeItemTypes = new[]
            {
                BaseItemKind.Movie
            },
            Limit = 1,
            ParentId = collectionFolder?.Id ?? Guid.Empty,
            Recursive = true,
            EnableTotalRecordCount = false
        }).Items.Any();
        
        return hasEpisodes && hasMovies;
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
                InternalItemsQuery query = new InternalItemsQuery(user)
                {
                    ItemIds = new[] { Guid.Parse(x.ItemId) },
                    GroupByPresentationUniqueKey = false
                };
                string[] allowedTags = query.IncludeInheritedTags;
                if (allowedTags.Length > 0)
                {
                    query.IncludeInheritedTags = Array.Empty<string>();
                    query.IncludeItemTypes = new[] { BaseItemKind.CollectionFolder };
                }

                IEnumerable<BaseItem> items = libraryManager.GetItemList(query);
                if (allowedTags.Length > 0 && !items.Any(item => item.GetType() == typeof(CollectionFolder)
                    && item.GetParents().FirstOrDefault() is UserRootFolder or AggregateFolder or UserView))
                {
                    // Only native root collections are exempt from the allowed-tag check.
                    query.IncludeInheritedTags = allowedTags;
                    query.IncludeItemTypes = Array.Empty<BaseItemKind>();
                    items = libraryManager.GetItemList(query);
                }

#if NET10_0_OR_GREATER
                return items.Any(item => user == null || item.IsVisible(user));
#else
                return items.Any();
#endif
            })
            .ToArray();
    }
}
