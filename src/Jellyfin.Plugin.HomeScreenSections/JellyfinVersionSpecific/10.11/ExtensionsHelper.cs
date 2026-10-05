using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Plugin.HomeScreenSections.JellyfinVersionSpecific
{
    public static class ExtensionsHelper
    {
        public static bool IsPlayedVersionSpecific(this BaseItem item, User user)
        {
            return item.IsPlayed(user, null);
        }
        
        public static List<TResult> QueryResultToList<TArray, TResult>(this IReadOnlyList<TArray> queryResult) where TResult : class, TArray
        {
            return queryResult.Select(x => x as TResult).Where(x => x != null).Select(x => x!).ToList();
        }

        public static bool SupportsBatchPeopleByItems(this ILibraryManager libraryManager) => false;

        public static IReadOnlyDictionary<Guid, IReadOnlyList<PersonInfo>> GetPeopleByItemsVersionSpecific(this ILibraryManager libraryManager, IReadOnlyList<Guid> itemIds)
        {
            throw new NotSupportedException("Jellyfin 10.11 has no batch people-by-item API; check SupportsBatchPeopleByItems first.");
        }
    }
}