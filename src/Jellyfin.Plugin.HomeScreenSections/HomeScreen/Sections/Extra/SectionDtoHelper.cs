using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Library;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections.Extra;

internal static class SectionDtoHelper
{
    public static DtoOptions CreateDefaultDtoOptions()
    {
        return new DtoOptions
        {
            Fields = [ItemFields.PrimaryImageAspectRatio,
                ItemFields.MediaSourceCount],
            ImageTypeLimit = 1,
            ImageTypes = [ImageType.Primary,
                ImageType.Thumb,
                ImageType.Backdrop]
        };
    }

    // ponytail: one helper replaces 10 identical GetInfo() bodies
    public static HomeScreenSectionInfo CreateInfo(IHomeScreenSection section, SectionViewMode viewMode = SectionViewMode.Landscape, bool allowHideWatched = false)
    {
        return new HomeScreenSectionInfo
        {
            Section = section.Section,
            DisplayText = section.DisplayText,
            AdditionalData = section.AdditionalData,
            Route = section.Route,
            Limit = section.Limit ?? 1,
            OriginalPayload = section.OriginalPayload,
            ViewMode = viewMode,
            AllowHideWatched = allowHideWatched
        };
    }

    public static BaseItemKind[] MovieAndSeriesKinds { get; } =
    {
        BaseItemKind.Movie,
        BaseItemKind.Series
    };

    public static BaseItemKind[] MovieSeriesEpisodeKinds { get; } =
    {
        BaseItemKind.Movie,
        BaseItemKind.Series,
        BaseItemKind.Episode
    };

    /// <summary>
    /// Kid-friendly official ratings used for the Kids section filter.
    /// </summary>
    public static string[] KidsOfficialRatings { get; } =
    {
        "G",
        "PG",
        "TV-Y",
        "TV-Y7",
        "TV-G",
        "TV-PG",
        "U",
        "PG-13"
    };
}
