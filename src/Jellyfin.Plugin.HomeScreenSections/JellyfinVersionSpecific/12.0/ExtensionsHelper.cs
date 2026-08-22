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

        /// <summary>
        /// Jellyfin 12.0 changed ILibraryManager.GetPeopleItems from
        /// IReadOnlyList&lt;Person&gt; to QueryResult&lt;BaseItem&gt;. Adapt it back so the
        /// shared section code stays version agnostic.
        /// </summary>
        public static List<Person> GetPeopleItemsVersionSpecific(this ILibraryManager libraryManager, InternalPeopleQuery query)
        {
            return libraryManager.GetPeopleItems(query).Items.OfType<Person>().ToList();
        }
    }
}
