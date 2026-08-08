using System.Text.RegularExpressions;
using Jellyfin.Plugin.HomeScreenSections.Tests.Support;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.HomeScreenSections.Tests;

public class LocalizationFilesTests
{
    private static readonly Regex PlaceholderRegex = new(
        @"\{[^}]+\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromMilliseconds(250));

    [Fact]
    public void All_localization_files_are_valid_json_objects()
    {
        string[] files = Directory.GetFiles(RepoPaths.LocalizationDir, "*.json");
        Assert.NotEmpty(files);

        foreach (string file in files)
        {
            string json = File.ReadAllText(file);
            JObject? obj = JObject.Parse(json);
            Assert.NotNull(obj);
            Assert.NotEmpty(obj.Properties());
        }
    }

    [Fact]
    public void Danish_has_same_keys_as_english()
    {
        JObject en = Load("en.json");
        JObject da = Load("da.json");

        string[] enKeys = en.Properties().Select(p => p.Name).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        string[] daKeys = da.Properties().Select(p => p.Name).OrderBy(k => k, StringComparer.Ordinal).ToArray();

        Assert.Equal(enKeys, daKeys);
    }

    [Fact]
    public void Danish_values_are_non_empty_strings()
    {
        JObject da = Load("da.json");

        foreach (JProperty prop in da.Properties())
        {
            Assert.True(prop.Value.Type == JTokenType.String, $"Key '{prop.Name}' must be a string");
            Assert.False(string.IsNullOrWhiteSpace(prop.Value.Value<string>()), $"Key '{prop.Name}' is empty");
        }
    }

    [Fact]
    public void Danish_preserves_english_placeholders()
    {
        JObject en = Load("en.json");
        JObject da = Load("da.json");

        foreach (JProperty prop in en.Properties())
        {
            string enText = prop.Value.Value<string>() ?? string.Empty;
            string daText = da.Value<string>(prop.Name) ?? string.Empty;

            string[] enPlaceholders = PlaceholderRegex.Matches(enText).Select(m => m.Value).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            string[] daPlaceholders = PlaceholderRegex.Matches(daText).Select(m => m.Value).OrderBy(x => x, StringComparer.Ordinal).ToArray();

            Assert.True(
                enPlaceholders.SequenceEqual(daPlaceholders, StringComparer.Ordinal),
                $"Placeholder mismatch for '{prop.Name}': en=[{string.Join(",", enPlaceholders)}] da=[{string.Join(",", daPlaceholders)}]");
        }
    }

    [Fact]
    public void English_contains_required_section_keys()
    {
        JObject en = Load("en.json");
        string[] required =
        [
            "ContinueWatching",
            "BecauseYouWatched",
            "WatchAgain",
            "MyMedia",
            "AdminModularHomeSettings",
            "AdminSave"
        ];

        foreach (string key in required)
        {
            Assert.True(en.ContainsKey(key), $"Missing required key '{key}' in en.json");
        }
    }

    [Theory]
    [InlineData("BecauseYouWatched", "TestShow")]
    [InlineData("DirectedBy", "Someone")]
    [InlineData("Starring", "Actor")]
    [InlineData("Genre", "Comedy")]
    public void Danish_pattern_strings_accept_placeholder_replacement(string key, string value)
    {
        JObject da = Load("da.json");
        string template = da.Value<string>(key)!;
        Assert.Contains("{0}", template, StringComparison.Ordinal);

        // Same replacement path used by TranslationManager for Pattern metadata
        string result = template.Replace("{0}", value, StringComparison.Ordinal);
        Assert.DoesNotContain("{0}", result, StringComparison.Ordinal);
        Assert.Contains(value, result, StringComparison.Ordinal);
    }

    private static JObject Load(string fileName)
    {
        string path = Path.Combine(RepoPaths.LocalizationDir, fileName);
        Assert.True(File.Exists(path), $"Missing localization file: {path}");
        return JObject.Parse(File.ReadAllText(path));
    }
}
