using Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections.Extra;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Plugin.HomeScreenSections.Tests.HomeScreen.Sections.Extra;

public class SectionDtoHelperTests
{
    [Fact]
    public void CreateDefaultDtoOptions_requests_aspect_ratio_and_media_source_count()
    {
        DtoOptions options = SectionDtoHelper.CreateDefaultDtoOptions();

        Assert.Contains(ItemFields.PrimaryImageAspectRatio, options.Fields);
        Assert.Contains(ItemFields.MediaSourceCount, options.Fields);
        Assert.Equal(1, options.ImageTypeLimit);
        Assert.Contains(ImageType.Primary, options.ImageTypes);
        Assert.Contains(ImageType.Thumb, options.ImageTypes);
        Assert.Contains(ImageType.Backdrop, options.ImageTypes);
    }

    [Fact]
    public void MovieAndSeriesKinds_contains_exactly_movie_and_series()
    {
        Assert.Equal([BaseItemKind.Movie, BaseItemKind.Series], SectionDtoHelper.MovieAndSeriesKinds);
    }

    [Fact]
    public void MovieSeriesEpisodeKinds_adds_episode()
    {
        Assert.Equal(
            [BaseItemKind.Movie, BaseItemKind.Series, BaseItemKind.Episode],
            SectionDtoHelper.MovieSeriesEpisodeKinds);
    }

    [Fact]
    public void KidsOfficialRatings_covers_expected_rating_schemes()
    {
        string[] ratings = SectionDtoHelper.KidsOfficialRatings;

        Assert.Contains("G", ratings, StringComparer.Ordinal);
        Assert.Contains("PG", ratings, StringComparer.Ordinal);
        Assert.Contains("TV-Y", ratings, StringComparer.Ordinal);
        Assert.Contains("TV-Y7", ratings, StringComparer.Ordinal);
        Assert.Contains("TV-G", ratings, StringComparer.Ordinal);
        Assert.Contains("TV-PG", ratings, StringComparer.Ordinal);
        Assert.Contains("U", ratings, StringComparer.Ordinal);
        Assert.Contains("PG-13", ratings, StringComparer.Ordinal);
        Assert.DoesNotContain("R", ratings, StringComparer.Ordinal);
        Assert.DoesNotContain("TV-MA", ratings, StringComparer.Ordinal);
    }
}
