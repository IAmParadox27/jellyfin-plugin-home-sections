using MediaBrowser.Controller.Entities;
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
    }
}