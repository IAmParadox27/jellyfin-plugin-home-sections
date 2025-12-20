using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.HomeScreenSections.Library
{
    public interface ITranslationManager
    {
        void Initialize();
        string Translate(string key, string desiredLanguage, string fallbackText, TranslationMetadata? metadata = null);
        void UpdateTranslationPack(string language, JObject translationPack);
    }
}