using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Services;
using Jellyfin.Plugin.HomeScreenSections.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.HomeScreenSections.Tests.Services;

/// <summary>
/// Translate's cache-miss path dereferences HomeScreenSectionsPlugin.Instance through
/// LibreTranslateHelper, so these tests run inside the plugin fixture collection.
/// </summary>
[Collection("Plugin Instance")]
public class TranslationMissPathTests
{

    public TranslationMissPathTests(PluginFixture fixture)
    {
        _ = fixture;
    }

    [Fact]
    public void Translate_missing_key_falls_back_to_fallback_text_when_no_translation_service_configured()
    {
        TranslationManager manager = new TranslationManager(NullLogger<ITranslationManager>.Instance);
        manager.UpdateTranslationPack("en", JObject.Parse("""
            {
                "Known": "Known Value"
            }
            """));

        // LibreTranslateUrl is empty in the fixture config, so the remote lookup yields nothing
        // and the fallback text is returned untouched.
        string result = manager.Translate("UnknownKey", "de", "Fallback Text");

        Assert.Equal("Fallback Text", result);
    }

    [Fact]
    public void Translate_missing_key_prefers_english_pack_value_over_fallback()
    {
        TranslationManager manager = new TranslationManager(NullLogger<ITranslationManager>.Instance);
        manager.UpdateTranslationPack("en", JObject.Parse("""
            {
                "UnknownKey": "English Value"
            }
            """));
        manager.UpdateTranslationPack("de", JObject.Parse("{}"));

        string result = manager.Translate("UnknownKey", "de", "Fallback Text");

        Assert.Equal("English Value", result);
    }
}
