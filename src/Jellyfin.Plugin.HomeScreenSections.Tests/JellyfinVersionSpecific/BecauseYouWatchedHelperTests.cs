using Jellyfin.Plugin.HomeScreenSections.JellyfinVersionSpecific;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;

namespace Jellyfin.Plugin.HomeScreenSections.Tests.JellyfinVersionSpecific;

public class BecauseYouWatchedHelperTests
{
    [Fact]
    public void ApplySimilarSettings_copies_genres_and_tags_from_item()
    {
        Movie movie = new Movie
        {
            Genres = ["Sci-Fi", "Adventure"],
            Tags = ["space", "epic"]
        };
        InternalItemsQuery query = new InternalItemsQuery();

        InternalItemsQuery result = query.ApplySimilarSettings(movie);

        Assert.Same(query, result);
        Assert.Equal(movie.Genres, result.Genres);
        Assert.Equal(movie.Tags, result.Tags);
    }

    [Fact]
    public void ApplySimilarSettings_handles_item_without_genres_or_tags()
    {
        Movie movie = new Movie();
        InternalItemsQuery query = new InternalItemsQuery();

        InternalItemsQuery result = query.ApplySimilarSettings(movie);

        Assert.Same(query, result);
        Assert.Equal(movie.Genres, result.Genres);
        Assert.Equal(movie.Tags, result.Tags);
    }
}
