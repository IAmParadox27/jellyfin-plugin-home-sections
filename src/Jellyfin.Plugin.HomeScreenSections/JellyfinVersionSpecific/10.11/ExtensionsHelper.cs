using MediaBrowser.Controller.Entities;
using SkiaSharp;

namespace Jellyfin.Plugin.HomeScreenSections.JellyfinVersionSpecific
{
    public static class ExtensionsHelper
    {
        public static bool IsPlayedVersionSpecific(this BaseItem item, User user)
        {
            return item.IsPlayed(user, null);
        }

        public static SKBitmap? ResizeVersionSpecific(this SKBitmap bitmap, SKImageInfo info)
        {
            return bitmap.Resize(info, SKSamplingOptions.Default);
        }
    }
}