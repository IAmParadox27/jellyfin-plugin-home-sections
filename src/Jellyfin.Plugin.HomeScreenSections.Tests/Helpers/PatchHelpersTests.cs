using System.Reflection;
using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Helpers;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.HomeScreenSections.Tests.Helpers;

public class PatchHelpersTests
{
    private static void InvokeReplace(JArray sectionsArr, IReadOnlyList<HomeScreenSectionInfo> sections)
    {
        MethodInfo method = typeof(PatchHelpers)
            .GetMethod("ReplaceStreamyfinSections", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ReplaceStreamyfinSections not found.");
        method.Invoke(null, [sectionsArr, sections]);
    }

    private static HomeScreenSectionInfo MakeInfo(string section, string displayText, SectionViewMode? viewMode, string? additionalData = null)
    {
        return new HomeScreenSectionInfo
        {
            Section = section,
            DisplayText = displayText,
            ViewMode = viewMode,
            AdditionalData = additionalData
        };
    }

    [Fact]
    public void ReplaceStreamyfinSections_clears_existing_entries_and_skips_native_sections()
    {
        JArray sectionsArr = JArray.Parse("[ { \"title\": \"old\" } ]");
        IReadOnlyList<HomeScreenSectionInfo> sections =
        [
            MakeInfo("Discover", "Discover", SectionViewMode.Landscape),
            MakeInfo("DiscoverMovies", "Discover Movies", SectionViewMode.Landscape),
            MakeInfo("UpcomingMovies", "Upcoming Movies", SectionViewMode.Portrait),
            MakeInfo("MyMedia", "My Media", SectionViewMode.Landscape)
        ];

        InvokeReplace(sectionsArr, sections);

        // All of these are Streamyfin-native rows and must be skipped, leaving the array empty.
        Assert.Empty(sectionsArr);
    }

    [Fact]
    public void ReplaceStreamyfinSections_maps_sections_to_streamyfin_shape()
    {
        JArray sectionsArr = new JArray();
        IReadOnlyList<HomeScreenSectionInfo> sections =
        [
            MakeInfo("ContinueWatching", "Continue Watching", SectionViewMode.Landscape, "extra-data"),
            MakeInfo("Genre", "Genre", SectionViewMode.Portrait, null)
        ];

        InvokeReplace(sectionsArr, sections);

        Assert.Equal(2, sectionsArr.Count);

        JObject landscape = (JObject)sectionsArr[0];
        Assert.Equal("Continue Watching", landscape.Value<string>("title"));
        Assert.Equal("horizontal", landscape.Value<string>("orientation"));
        Assert.Equal("/HomeScreen/Section/ContinueWatching", landscape["custom"]!.Value<string>("endpoint"));
        Assert.Equal("extra-data", landscape["custom"]!["query"]!.Value<string>("additionalData"));
        Assert.Equal("en", landscape["custom"]!["query"]!.Value<string>("language"));

        JObject portrait = (JObject)sectionsArr[1];
        Assert.Equal("vertical", portrait.Value<string>("orientation"));
        Assert.Equal("/HomeScreen/Section/Genre", portrait["custom"]!.Value<string>("endpoint"));
    }
}
