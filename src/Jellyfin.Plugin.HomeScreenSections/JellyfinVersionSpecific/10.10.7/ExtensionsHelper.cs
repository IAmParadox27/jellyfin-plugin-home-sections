using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.HomeScreenSections.JellyfinVersionSpecific
{
    public static class ExtensionsHelper
    {
        public static bool IsPlayedVersionSpecific(this BaseItem item, User user)
        {
            return item.IsPlayed(user);
        }

        /// <summary>
        /// Passthrough: on this Jellyfin version GetPeopleItems already returns the people.
        /// </summary>
        public static List<Person> GetPeopleItemsVersionSpecific(this MediaBrowser.Controller.Library.ILibraryManager libraryManager, MediaBrowser.Controller.Entities.InternalPeopleQuery query)
        {
            return libraryManager.GetPeopleItems(query).ToList();
        }
    }
}