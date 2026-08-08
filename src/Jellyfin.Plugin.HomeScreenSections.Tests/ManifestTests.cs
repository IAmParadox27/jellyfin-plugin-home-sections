using System.Text.RegularExpressions;
using Jellyfin.Plugin.HomeScreenSections.Tests.Support;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.HomeScreenSections.Tests;

public class ManifestTests
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly Regex VersionRegex = new(@"^\d+\.\d+\.\d+\.\d+$", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, RegexTimeout);
    private static readonly Regex AbiRegex = new(@"^\d+\.\d+\.\d+\.\d+$", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, RegexTimeout);
    private static readonly Regex Md5Regex = new(@"^[0-9A-Fa-f]{32}$", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, RegexTimeout);

    [Fact]
    public void Manifest_exists_and_is_valid_json_array()
    {
        Assert.True(File.Exists(RepoPaths.ManifestPath), $"Missing manifest at {RepoPaths.ManifestPath}");
        JArray arr = JArray.Parse(File.ReadAllText(RepoPaths.ManifestPath));
        Assert.NotEmpty(arr);
    }

    [Fact]
    public void Manifest_plugin_entry_has_required_fields()
    {
        JObject plugin = LoadPlugin();

        Assert.Equal("b8298e01-2697-407a-b44d-aa8dc795e850", plugin.Value<string>("guid"));
        Assert.False(string.IsNullOrWhiteSpace(plugin.Value<string>("name")));
        Assert.False(string.IsNullOrWhiteSpace(plugin.Value<string>("owner")));
        Assert.False(string.IsNullOrWhiteSpace(plugin.Value<string>("category")));

        JArray? versions = plugin["versions"] as JArray;
        Assert.NotNull(versions);
        Assert.NotEmpty(versions!);
    }

    [Fact]
    public void Manifest_versions_have_valid_fields()
    {
        JObject plugin = LoadPlugin();
        JArray versions = (JArray)plugin["versions"]!;

        foreach (JToken token in versions)
        {
            JObject version = (JObject)token;

            string ver = version.Value<string>("version") ?? string.Empty;
            string abi = version.Value<string>("targetAbi") ?? string.Empty;
            string checksum = version.Value<string>("checksum") ?? string.Empty;
            string sourceUrl = version.Value<string>("sourceUrl") ?? string.Empty;

            Assert.True(VersionRegex.IsMatch(ver), $"Invalid version '{ver}'");
            Assert.True(AbiRegex.IsMatch(abi), $"Invalid targetAbi '{abi}'");
            Assert.True(Md5Regex.IsMatch(checksum), $"Invalid checksum '{checksum}'");
            Assert.StartsWith("https://", sourceUrl, StringComparison.Ordinal);
            Assert.EndsWith(".zip", sourceUrl, StringComparison.Ordinal);

            JArray? deps = version["dependencies"] as JArray;
            Assert.NotNull(deps);
            Assert.Contains(deps!, d => string.Equals(d.Value<string>(), "5e87cc92-571a-4d8d-8d98-d2d4147f9f90", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Manifest_includes_package_for_jellyfin_10_11_11()
    {
        JObject plugin = LoadPlugin();
        JArray versions = (JArray)plugin["versions"]!;

        bool has = versions.Any(v =>
            string.Equals(v.Value<string>("targetAbi"), "10.11.11.0", StringComparison.Ordinal) &&
            (v.Value<string>("sourceUrl") ?? string.Empty).Contains("Release-10.11.11.zip", StringComparison.Ordinal));

        Assert.True(has, "Expected a 10.11.11 package entry in manifest.json");
    }

    private static JObject LoadPlugin()
    {
        JArray arr = JArray.Parse(File.ReadAllText(RepoPaths.ManifestPath));
        Assert.NotEmpty(arr);
        return (JObject)arr[0]!;
    }
}
