using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Identity;

namespace Jellyfin.Plugin.HomeScreenSections.JellyfinVersionSpecific
{
    public static class BecauseYouWatchedHelper
    {
        public static InternalItemsQuery ApplySimilarSettings(this InternalItemsQuery query, BaseItem item)
        {
            query.SimilarTo = item;

            return query;
        }
    }
}