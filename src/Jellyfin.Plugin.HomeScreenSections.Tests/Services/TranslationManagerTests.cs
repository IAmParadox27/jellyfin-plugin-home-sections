using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.HomeScreenSections.Tests.Services;

public class TranslationManagerTests
{
    private static TranslationManager MakeManager()
    {
        TranslationManager manager = new TranslationManager(NullLogger<ITranslationManager>.Instance);
        manager.UpdateTranslationPack("en", EnglishPack());
        manager.UpdateTranslationPack("de", GermanPack());
        return manager;
    }

    private static JObject EnglishPack()
    {
        return JObject.Parse("""
            {
                "ContinueWatching": "Continue Watching",
                "BecauseYouWatched": "Because You Watched {0}",
                "LatestMovies": "Latest Movies",
                "Genre": "Genre",
                "SciFi": "Sci-Fi"
            }
            """);
    }

    private static JObject GermanPack()
    {
        return JObject.Parse("""
            {
                "ContinueWatching": "Weiter schauen",
                "LatestMovies": "Neueste Filme"
            }
            """);
    }

    [Fact]
    public void Translate_returns_pack_value_for_known_key()
    {
        TranslationManager manager = MakeManager();
        // Fallback differs from the en pack value so the test can only pass via the pack lookup.
        Assert.Equal("Continue Watching", manager.Translate("ContinueWatching", "en", "FALLBACK TEXT"));
    }

    [Fact]
    public void Translate_uses_requested_language_pack()
    {
        TranslationManager manager = MakeManager();
        Assert.Equal("Weiter schauen", manager.Translate("ContinueWatching", "de", "Continue Watching"));
    }

    [Fact]
    public void Translate_strips_region_to_base_language()
    {
        TranslationManager manager = MakeManager();
        Assert.Equal("Weiter schauen", manager.Translate("ContinueWatching", "de-DE", "Continue Watching"));
    }

    [Fact]
    public void Translate_falls_back_to_english_for_missing_language()
    {
        TranslationManager manager = MakeManager();
        Assert.Equal("Continue Watching", manager.Translate("ContinueWatching", "xx", "Continue Watching"));
    }

    [Fact]
    public void Translate_uses_regular_lookup_when_key_equals_full_text_key()
    {
        TranslationManager manager = MakeManager();
        // fallbackText "Latest Movies" derives "LatestMovies", identical to the key — the full-text
        // shortcut must be skipped and the regular key lookup used instead.
        Assert.Equal("Neueste Filme", manager.Translate("LatestMovies", "de", "Latest Movies"));
    }

    [Fact]
    public void Translate_prefers_full_text_key_and_drops_metadata()
    {
        TranslationManager manager = MakeManager();
        manager.UpdateTranslationPack("en", JObject.Parse("""
            {
                "SciFiMovies": "Sci-Fi Movies",
                "Latest": "Latest"
            }
            """));

        // fallbackText "Sci-Fi Movies" derives the key "SciFiMovies", which exists — that wins,
        // and the Pattern metadata must be ignored (no {0} substitution on the result).
        TranslationMetadata metadata = new TranslationMetadata
        {
            Type = TranslationType.Pattern,
            AdditionalContent = "Extra"
        };

        string result = manager.Translate("SomeOtherKey", "en", "Sci-Fi Movies", metadata);

        Assert.Equal("Sci-Fi Movies", result);
    }

    [Fact]
    public void Translate_pattern_metadata_replaces_placeholder()
    {
        TranslationManager manager = MakeManager();
        TranslationMetadata metadata = new TranslationMetadata
        {
            Type = TranslationType.Pattern,
            AdditionalContent = "Interstellar"
        };

        string result = manager.Translate("BecauseYouWatched", "en", "Because You Watched", metadata);

        Assert.Equal("Because You Watched Interstellar", result);
    }

    [Fact]
    public void Translate_prefix_metadata_appends_additional_content()
    {
        TranslationManager manager = MakeManager();
        TranslationMetadata metadata = new TranslationMetadata
        {
            Type = TranslationType.Prefix,
            AdditionalContent = "Comedy"
        };

        Assert.Equal("Genre Comedy", manager.Translate("Genre", "en", "Genre", metadata));
    }

    [Fact]
    public void Translate_suffix_metadata_prepends_additional_content()
    {
        TranslationManager manager = MakeManager();
        TranslationMetadata metadata = new TranslationMetadata
        {
            Type = TranslationType.Suffix,
            AdditionalContent = "Comedy"
        };

        Assert.Equal("Comedy Genre", manager.Translate("Genre", "en", "Genre", metadata));
    }

    [Fact]
    public void Translate_can_translate_additional_content_recursively()
    {
        TranslationManager manager = MakeManager();
        TranslationMetadata metadata = new TranslationMetadata
        {
            Type = TranslationType.Pattern,
            AdditionalContent = "Sci Fi",
            TranslateAdditionalContent = true
        };

        // "Sci Fi" -> key "SciFi" -> "Sci-Fi" in the en pack, then substituted into the pattern.
        string result = manager.Translate("BecauseYouWatched", "en", "Because You Watched", metadata);

        Assert.Equal("Because You Watched Sci-Fi", result);
    }

    [Fact]
    public void UpdateTranslationPack_overwrites_existing_pack()
    {
        TranslationManager manager = MakeManager();
        manager.UpdateTranslationPack("de", JObject.Parse("""
            {
                "ContinueWatching": "Weitersehen"
            }
            """));

        Assert.Equal("Weitersehen", manager.Translate("ContinueWatching", "de", "Continue Watching"));
    }

    [Fact]
    public void GetTranslationPack_returns_dictionary_for_known_language()
    {
        TranslationManager manager = MakeManager();

        IDictionary<string, string>? pack = manager.GetTranslationPack("de");

        Assert.NotNull(pack);
        Assert.Equal("Weiter schauen", pack!["ContinueWatching"]);
    }

    [Fact]
    public void GetTranslationPack_strips_region()
    {
        TranslationManager manager = MakeManager();

        IDictionary<string, string>? pack = manager.GetTranslationPack("de-AT");

        Assert.NotNull(pack);
        Assert.Equal("Neueste Filme", pack!["LatestMovies"]);
    }

    [Fact]
    public void GetTranslationPack_falls_back_to_english()
    {
        TranslationManager manager = MakeManager();

        IDictionary<string, string>? pack = manager.GetTranslationPack("xx");

        Assert.NotNull(pack);
        Assert.Equal("Continue Watching", pack!["ContinueWatching"]);
    }

    [Fact]
    public void GetTranslationPack_returns_null_when_no_fallback_available()
    {
        TranslationManager manager = new TranslationManager(NullLogger<ITranslationManager>.Instance);
        manager.UpdateTranslationPack("de", GermanPack());

        Assert.Null(manager.GetTranslationPack("xx"));
    }

    [Fact]
    public void TranslationMetadata_defaults_to_full_text_without_additional_content()
    {
        TranslationMetadata metadata = new TranslationMetadata();

        Assert.Equal(TranslationType.FullText, metadata.Type);
        Assert.Null(metadata.AdditionalContent);
        Assert.False(metadata.TranslateAdditionalContent);
    }
}
