using Jellyfin.Plugin.HomeScreenSections.Helpers;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.HomeScreenSections.Services
{
    public class TranslationManager : ITranslationManager
    {
        private Dictionary<string, JObject> m_translationPacks = new Dictionary<string, JObject>();
        private readonly ILogger<ITranslationManager> m_logger;

        public TranslationManager(ILogger<ITranslationManager> logger)
        {
            m_logger = logger;
        }

        public void Initialize()
        {
            m_logger.LogTrace("Loading translation files");
            m_logger.LogTrace($"Available resources: {string.Join(',', HomeScreenSectionsPlugin.Instance.GetType().Assembly.GetManifestResourceNames())}");
            
            // Get all the json files from the embedded resources
            string[] locJsonFiles = HomeScreenSectionsPlugin.Instance.GetType().Assembly.GetManifestResourceNames()
                .Where(x => x.EndsWith(".json") && x.Contains("_Localization.")).ToArray();

            foreach (string locFile in locJsonFiles)
            {
                m_logger.LogTrace($"Loading translation file: {locFile}");
                using Stream? locStream = HomeScreenSectionsPlugin.Instance.GetType().Assembly.GetManifestResourceStream(locFile);

                if (locStream != null)
                {
                    using TextReader reader = new StreamReader(locStream);

                    string key = locFile.Replace(".json", "").Split('.').Last();
                    m_translationPacks.Add(key, JObject.Parse(reader.ReadToEnd()));
                    
                    m_logger.LogTrace($"Loaded translation file: {locFile} with {m_translationPacks[key].Count} keys");
                }
            }
        }

        public string Translate(string key, string desiredLanguage, string fallbackText, TranslationMetadata? metadata = null)
        {
            m_logger.LogTrace($"Translating key '{key}' to language '{desiredLanguage}'");
            
            bool languageFound = false;
            string languageKey = desiredLanguage;

            do
            {
                // If we don't have the language, but it has a region remove the region and just grab the language and see if we 
                // have a blanket translation for that language.
                if (!m_translationPacks.ContainsKey(languageKey) && languageKey.Contains("-"))
                {
                    m_logger.LogTrace($"Language '{languageKey}' doesn't exist, removing region and trying again");
                    languageKey = languageKey.Split("-")[0];
                }
                // If we don't then fallback to english so we don't get keys being sent to the client
                else if (!m_translationPacks.ContainsKey(languageKey))
                {
                    m_logger.LogTrace($"Language '{languageKey}' doesn't exist, falling back to english");
                    languageKey = "en";
                }
                // If we have it then we're done.
                else if (m_translationPacks.ContainsKey(languageKey))
                {
                    m_logger.LogTrace($"Found translation pack for language '{languageKey}'");
                    languageFound = true;
                }
            } while (!languageFound);

            JObject translationPack = m_translationPacks[languageKey];

            string translatedText = "";
            if (translationPack.ContainsKey(key))
            {
                m_logger.LogTrace($"Found translation for key '{key}' in language '{languageKey}'");
                translatedText = translationPack.Value<string>(key)!;
            }
            else
            {
                m_logger.LogTrace($"No translation found for key '{key}' in language '{languageKey}', falling back to previous routes");
                // If Libre is disabled this will be null
                string? libreTranslateVersion = LibreTranslateHelper.TranslateAsync(fallbackText, "en", desiredLanguage).GetAwaiter().GetResult();
                
                translatedText = libreTranslateVersion ?? m_translationPacks["en"].Value<string>(key) ?? fallbackText;
            }

            if (metadata != null)
            {
                m_logger.LogTrace($"Applying metadata to translated text: {translatedText}");

                string? additionalContent = metadata.AdditionalContent;
                if (metadata.TranslateAdditionalContent && !string.IsNullOrEmpty(additionalContent))
                {
                    additionalContent = Translate(additionalContent, desiredLanguage, additionalContent, null);
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
                
                m_logger.LogTrace($"Applied metadata to translated text: {translatedText}");
            }
            
            return translatedText;
        }
    }
}