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
            Fields = new List<ItemFields>
            {
                ItemFields.PrimaryImageAspectRatio,
                ItemFields.MediaSourceCount
            },
            ImageTypeLimit = 1,
            ImageTypes = new List<ImageType>
            {
                ImageType.Primary,
                ImageType.Thumb,
                ImageType.Backdrop
            }
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
