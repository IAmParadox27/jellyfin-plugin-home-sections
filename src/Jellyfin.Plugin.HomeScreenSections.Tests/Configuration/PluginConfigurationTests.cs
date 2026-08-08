using Jellyfin.Plugin.HomeScreenSections.Configuration;

namespace Jellyfin.Plugin.HomeScreenSections.Tests.Configuration;

public class PluginConfigurationTests
{
    [Fact]
    public void Defaults_match_documented_plugin_behaviour()
    {
        PluginConfiguration config = new PluginConfiguration();

        Assert.False(config.Enabled);
        Assert.False(config.LazyLoadEnabled);
        Assert.Equal(10, config.NumSectionsPerPage);
        Assert.True(config.AllowUserOverride);
        Assert.Equal(string.Empty, config.LibreTranslateUrl);
        Assert.Equal(string.Empty, config.JellyseerrUrl);
        Assert.Equal("en", config.JellyseerrPreferredLanguages);
        Assert.Equal("YYYY/MM/DD", config.DateFormat);
        Assert.Equal("/", config.DateDelimiter);
        Assert.True(config.FilterUpcomingByLibraryAccess);
        Assert.False(config.DeveloperMode);
        Assert.Equal(0, config.CacheBustCounter);
        Assert.Equal(86400, config.CacheTimeoutSeconds);
        Assert.False(config.OverrideStreamyfinHome);
        Assert.Equal(10000, config.MaxImageCacheEntries);
        Assert.Equal(600, config.MaxImageWidth);
        Assert.Equal(85, config.ImageJpegQuality);
        Assert.Empty(config.SectionSettings);
    }

    [Fact]
    public void Default_library_ids_are_empty()
    {
        PluginConfiguration config = new PluginConfiguration();

        Assert.Equal(string.Empty, config.DefaultMoviesLibraryId);
        Assert.Equal(string.Empty, config.DefaultTVShowsLibraryId);
        Assert.Equal(string.Empty, config.DefaultMusicLibraryId);
        Assert.Equal(string.Empty, config.DefaultBooksLibraryId);
        Assert.Equal(string.Empty, config.DefaultMusicVideosLibraryId);
    }

    [Fact]
    public void Arr_timeframes_default_per_service()
    {
        PluginConfiguration config = new PluginConfiguration();

        Assert.Equal(1, config.Sonarr.UpcomingTimeframeValue);
        Assert.Equal(TimeframeUnit.Weeks, config.Sonarr.UpcomingTimeframeUnit);

        Assert.Equal(3, config.Radarr.UpcomingTimeframeValue);
        Assert.Equal(TimeframeUnit.Months, config.Radarr.UpcomingTimeframeUnit);

        Assert.Equal(6, config.Lidarr.UpcomingTimeframeValue);
        Assert.Equal(TimeframeUnit.Months, config.Lidarr.UpcomingTimeframeUnit);

        Assert.Equal(1, config.Readarr.UpcomingTimeframeValue);
        Assert.Equal(TimeframeUnit.Years, config.Readarr.UpcomingTimeframeUnit);
    }

    [Fact]
    public void ArrConfig_defaults_to_digital_release_only()
    {
        ArrConfig arrConfig = new ArrConfig();

        Assert.Equal(string.Empty, arrConfig.ApiKey);
        Assert.Equal(string.Empty, arrConfig.Url);
        Assert.False(arrConfig.ConsiderCinemaRelease);
        Assert.False(arrConfig.ConsiderPhysicalRelease);
        Assert.True(arrConfig.ConsiderDigitalRelease);
    }

    [Fact]
    public void SectionSettings_defaults_to_landscape_and_empty_id()
    {
        SectionSettings settings = new SectionSettings();

        Assert.Equal(string.Empty, settings.SectionId);
        Assert.False(settings.Enabled);
        Assert.False(settings.AllowUserOverride);
        Assert.Equal(0, settings.LowerLimit);
        Assert.Equal(0, settings.UpperLimit);
        Assert.Equal(0, settings.OrderIndex);
        Assert.Equal(SectionViewMode.Landscape, settings.ViewMode);
        Assert.False(settings.HideWatchedItems);
    }

    [Theory]
    [InlineData(SectionViewMode.Portrait, 0)]
    [InlineData(SectionViewMode.Landscape, 1)]
    [InlineData(SectionViewMode.Square, 2)]
    [InlineData(SectionViewMode.Small, 3)]
    public void SectionViewMode_values_are_stable(SectionViewMode mode, int expected)
    {
        // Persisted section settings serialize view modes numerically, so the ordering must not change.
        Assert.Equal(expected, (int)mode);
    }

    [Theory]
    [InlineData(TimeframeUnit.Days, 0)]
    [InlineData(TimeframeUnit.Weeks, 1)]
    [InlineData(TimeframeUnit.Months, 2)]
    [InlineData(TimeframeUnit.Years, 3)]
    public void TimeframeUnit_values_are_stable(TimeframeUnit unit, int expected)
    {
        Assert.Equal(expected, (int)unit);
    }
}
