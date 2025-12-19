namespace Jellyfin.Plugin.HomeScreenSections.Library
{
    public interface ITranslationManager
    {
        void Initialize();
        string Translate(string key, string desiredLanguage, string fallbackText, TranslationMetadata? metadata = null);
    }
}