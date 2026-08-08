using Jellyfin.Plugin.HomeScreenSections.Helpers;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.HomeScreenSections.Services
{
    public class TranslationManager : ITranslationManager
    {
        private Dictionary<string, JObject> _translationPacks = new(StringComparer.Ordinal);
        private readonly ILogger<ITranslationManager> _logger;

        public TranslationManager(ILogger<ITranslationManager> logger)
        {
            _logger = logger;
        }

        public void Initialize()
        {
            PluginLog.LoadingTranslationFiles(_logger);
            if (_logger.IsEnabled(LogLevel.Trace))
            {
                string resources = string.Join(
                    ',',
                    HomeScreenSectionsPlugin.Instance.GetType().Assembly.GetManifestResourceNames());
                PluginLog.AvailableResources(_logger, resources);
            }
            
            // Get all the json files from the embedded resources
            string[] locJsonFiles = HomeScreenSectionsPlugin.Instance.GetType().Assembly.GetManifestResourceNames()
                .Where(x => x.EndsWith(".json", StringComparison.Ordinal) && x.Contains("_Localization.", StringComparison.Ordinal)).ToArray();

            foreach (string locFile in locJsonFiles)
            {
                PluginLog.LoadingTranslationFile(_logger, locFile);
                using Stream? locStream = HomeScreenSectionsPlugin.Instance.GetType().Assembly.GetManifestResourceStream(locFile);

                if (locStream != null)
                {
                    using TextReader reader = new StreamReader(locStream);

                    string key = locFile.Replace(".json", "").Split('.').Last();

                    JObject pack = JObject.Parse(reader.ReadToEnd());
                    if (_translationPacks.TryAdd(key, pack))
                    {
                        PluginLog.LoadedTranslationFile(_logger, locFile, pack.Count);
                    }
                    else
                    {
                        PluginLog.TranslationFileAlreadyLoaded(_logger, locFile);
                    }
                }
            }
        }

        public string Translate(string key, string desiredLanguage, string fallbackText, TranslationMetadata? metadata = null)
        {
            PluginLog.TranslatingKey(_logger, key, desiredLanguage);
            string languageKey = ResolveLanguageKey(desiredLanguage);
            JObject translationPack = _translationPacks[languageKey];

            string translatedText = LookupTranslation(key, fallbackText, desiredLanguage, languageKey, translationPack, ref metadata);
            if (metadata != null)
            {
                translatedText = ApplyTranslationMetadata(translatedText, desiredLanguage, metadata);
            }

            return translatedText;
        }

        private string ResolveLanguageKey(string desiredLanguage)
        {
            string languageKey = desiredLanguage;
            while (true)
            {
                if (_translationPacks.ContainsKey(languageKey))
                {
                    PluginLog.FoundTranslationPack(_logger, languageKey);
                    return languageKey;
                }

                if (languageKey.Contains('-'))
                {
                    PluginLog.LanguageMissingRemoveRegion(_logger, languageKey);
                    languageKey = languageKey.Split("-")[0];
                    continue;
                }

                PluginLog.LanguageMissingFallbackEnglish(_logger, languageKey);
                return "en";
            }
        }

        private string LookupTranslation(
            string key,
            string fallbackText,
            string desiredLanguage,
            string languageKey,
            JObject translationPack,
            ref TranslationMetadata? metadata)
        {
            string fullTextKey = fallbackText.Replace(" ", "").Replace("-", "");
            if (!string.Equals(key, fullTextKey, StringComparison.Ordinal) && translationPack.ContainsKey(fullTextKey))
            {
                PluginLog.FoundFullTextTranslation(_logger, fullTextKey, languageKey);
                metadata = null;
                return translationPack.Value<string>(fullTextKey)!;
            }

            if (translationPack.ContainsKey(key))
            {
                PluginLog.FoundKeyTranslation(_logger, key, languageKey);
                return translationPack.Value<string>(key)!;
            }

            PluginLog.NoTranslationFound(_logger, key, languageKey);
            string? libreTranslateVersion = LibreTranslateHelper.TranslateAsync(fallbackText, "en", desiredLanguage).GetAwaiter().GetResult();
            return libreTranslateVersion ?? _translationPacks["en"].Value<string>(key) ?? fallbackText;
        }

        private string ApplyTranslationMetadata(string translatedText, string desiredLanguage, TranslationMetadata metadata)
        {
            PluginLog.ApplyingTranslationMetadata(_logger, translatedText);

            string? additionalContent = metadata.AdditionalContent;
            if (metadata.TranslateAdditionalContent && !string.IsNullOrEmpty(additionalContent))
            {
                additionalContent = Translate(additionalContent.Replace(" ", "").Replace("-", ""), desiredLanguage, additionalContent);
            }

            if (metadata.Type == TranslationType.Prefix)
            {
                translatedText = $"{translatedText} {additionalContent}".Trim();
            }
            else if (metadata.Type == TranslationType.Suffix)
            {
                translatedText = $"{additionalContent} {translatedText}".Trim();
            }
            else if (metadata.Type == TranslationType.Pattern)
            {
                translatedText = translatedText.Replace("{0}", additionalContent);
            }

            PluginLog.AppliedTranslationMetadata(_logger, translatedText);
            return translatedText;
        }

        public void UpdateTranslationPack(string language, JObject translationPack)
        {
            _translationPacks[language] = translationPack;
        }

        public IDictionary<string, string>? GetTranslationPack(string language)
        {
            string languageKey = language;

            if (!_translationPacks.ContainsKey(languageKey) && languageKey.Contains('-'))
            {
                languageKey = languageKey.Split("-")[0];
            }

            if (!_translationPacks.ContainsKey(languageKey))
            {
                languageKey = "en";
            }

            if (_translationPacks.TryGetValue(languageKey, out JObject? pack))
            {
                return pack.ToObject<Dictionary<string, string>>();
            }

            return null;
        }
    }
}
