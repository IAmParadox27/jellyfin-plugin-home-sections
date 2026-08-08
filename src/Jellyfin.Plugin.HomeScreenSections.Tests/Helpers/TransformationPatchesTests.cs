using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Helpers;
using Jellyfin.Plugin.HomeScreenSections.Model;
using Jellyfin.Plugin.HomeScreenSections.Tests.Support;

namespace Jellyfin.Plugin.HomeScreenSections.Tests.Helpers;

[Collection("Plugin Instance")]
public class TransformationPatchesTests
{

    public TransformationPatchesTests(PluginFixture fixture)
    {
        _ = fixture;
    }

    [Fact]
    public void LoadSections_injects_replacement_between_load_sections_markers()
    {
        const string source = "var someVar=1,loadSections:function(){}";
        PatchRequestPayload payload = new PatchRequestPayload { Contents = source };

        string result = TransformationPatches.LoadSections(payload);

        Assert.Contains(",originalLoadSections:", result, StringComparison.Ordinal);
        // The injected template keeps the original function reference under the new name.
        Assert.Contains("someVar", result, StringComparison.Ordinal);
        Assert.DoesNotContain("{{this_hook}}", result, StringComparison.Ordinal);
        Assert.DoesNotContain("{{cardbuilder_hook}}", result, StringComparison.Ordinal);
    }

    [Fact]
    public void IndexHtml_injects_stylesheet_and_script_tags()
    {
        PluginConfiguration config = HomeScreenSectionsPlugin.Instance.Configuration;
        bool originalDeveloperMode = config.DeveloperMode;
        config.DeveloperMode = false;
        try
        {
            PatchRequestPayload payload = new PatchRequestPayload
            {
                Contents = "<html><head></head><body></body></html>"
            };

            string result = TransformationPatches.IndexHtml(payload);

            Assert.Contains("home-screen-sections.css", result, StringComparison.Ordinal);
            Assert.Contains("home-screen-sections.js", result, StringComparison.Ordinal);
            Assert.Contains("plugin=\"Jellyfin.Plugin.HomeScreenSections\"", result, StringComparison.Ordinal);
            // Cache parameters include the plugin version and bust counter.
            Assert.Contains($"&c={config.CacheBustCounter}", result, StringComparison.Ordinal);
            Assert.Contains("</head>", result, StringComparison.Ordinal);
            Assert.Contains("</body>", result, StringComparison.Ordinal);
        }
        finally
        {
            config.DeveloperMode = originalDeveloperMode;
        }
    }

    [Fact]
    public void IndexHtml_uses_timestamp_cache_buster_in_developer_mode()
    {
        PluginConfiguration config = HomeScreenSectionsPlugin.Instance.Configuration;
        bool originalDeveloperMode = config.DeveloperMode;
        config.DeveloperMode = true;
        try
        {
            PatchRequestPayload payload = new PatchRequestPayload
            {
                Contents = "<html><head></head><body></body></html>"
            };

            string result = TransformationPatches.IndexHtml(payload);

            Assert.Contains("&t=", result, StringComparison.Ordinal);
            Assert.DoesNotContain("&c=", result, StringComparison.Ordinal);
        }
        finally
        {
            config.DeveloperMode = originalDeveloperMode;
        }
    }
}
