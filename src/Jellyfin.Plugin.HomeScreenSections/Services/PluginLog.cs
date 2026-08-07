using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.HomeScreenSections.Services;

/// <summary>
/// High-performance structured logging (LoggerMessage source generators).
/// Avoids params object[] allocation / CA1873 on hot and cold log paths.
/// </summary>
internal static partial class PluginLog
{
    // --- TranslationManager ---
    [LoggerMessage(EventId = 1000, Level = LogLevel.Trace, Message = "Available resources: {Resources}")]
    public static partial void AvailableResources(ILogger logger, string resources);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Trace, Message = "Loading translation file: {LocFile}")]
    public static partial void LoadingTranslationFile(ILogger logger, string locFile);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Trace, Message = "Loaded translation file: {LocFile} with {KeyCount} keys")]
    public static partial void LoadedTranslationFile(ILogger logger, string locFile, int keyCount);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Trace, Message = "Translation file '{LocFile}' already loaded, ignoring")]
    public static partial void TranslationFileAlreadyLoaded(ILogger logger, string locFile);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Trace, Message = "Translating key '{Key}' to language '{DesiredLanguage}'")]
    public static partial void TranslatingKey(ILogger logger, string key, string desiredLanguage);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Trace, Message = "Language '{LanguageKey}' doesn't exist, removing region and trying again")]
    public static partial void LanguageMissingRemoveRegion(ILogger logger, string languageKey);

    [LoggerMessage(EventId = 1006, Level = LogLevel.Trace, Message = "Language '{LanguageKey}' doesn't exist, falling back to english")]
    public static partial void LanguageMissingFallbackEnglish(ILogger logger, string languageKey);

    [LoggerMessage(EventId = 1007, Level = LogLevel.Trace, Message = "Found translation pack for language '{LanguageKey}'")]
    public static partial void FoundTranslationPack(ILogger logger, string languageKey);

    [LoggerMessage(EventId = 1008, Level = LogLevel.Trace, Message = "Found translation for key '{FullTextKey}' in language '{LanguageKey}'")]
    public static partial void FoundFullTextTranslation(ILogger logger, string fullTextKey, string languageKey);

    [LoggerMessage(EventId = 1009, Level = LogLevel.Trace, Message = "Found translation for key '{Key}' in language '{LanguageKey}'")]
    public static partial void FoundKeyTranslation(ILogger logger, string key, string languageKey);

    [LoggerMessage(EventId = 1010, Level = LogLevel.Warning, Message = "No translation found for key '{Key}' in language '{LanguageKey}', falling back to previous routes")]
    public static partial void NoTranslationFound(ILogger logger, string key, string languageKey);

    [LoggerMessage(EventId = 1011, Level = LogLevel.Trace, Message = "Applying metadata to translated text: {TranslatedText}")]
    public static partial void ApplyingTranslationMetadata(ILogger logger, string translatedText);

    [LoggerMessage(EventId = 1012, Level = LogLevel.Trace, Message = "Applied metadata to translated text: {TranslatedText}")]
    public static partial void AppliedTranslationMetadata(ILogger logger, string translatedText);

    // --- HomeScreenManager ---
    [LoggerMessage(EventId = 1100, Level = LogLevel.Information, Message = "Updating user settings for user {UserId}")]
    public static partial void UpdatingUserSettings(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 1101, Level = LogLevel.Information, Message = "Json of user settings received from browser: {UserSettingsJson}")]
    public static partial void UserSettingsJsonReceived(ILogger logger, string userSettingsJson);

    [LoggerMessage(EventId = 1102, Level = LogLevel.Information, Message = "Plugin settings file: {PluginSettings}")]
    public static partial void PluginSettingsFile(ILogger logger, string pluginSettings);

    [LoggerMessage(EventId = 1103, Level = LogLevel.Information, Message = "Creating directory: '{DirectoryName}' if it doesn't exist.")]
    public static partial void CreatingSettingsDirectory(ILogger logger, string? directoryName);

    [LoggerMessage(EventId = 1104, Level = LogLevel.Information, Message = "Checking if user settings already exist for user {UserId} and reading it if so.")]
    public static partial void CheckingExistingUserSettings(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 1105, Level = LogLevel.Information, Message = "Parsed user settings: {SettingsJson}")]
    public static partial void ParsedUserSettings(ILogger logger, string settingsJson);

    [LoggerMessage(EventId = 1106, Level = LogLevel.Information, Message = "Removing all existing user settings for user {UserId} and adding the new one.")]
    public static partial void RemovingExistingUserSettings(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 1107, Level = LogLevel.Information, Message = "Adding user settings for user {UserId} to the settings array.")]
    public static partial void AddingUserSettings(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 1108, Level = LogLevel.Information, Message = "Writing user settings to file: {PluginSettings}")]
    public static partial void WritingUserSettings(ILogger logger, string pluginSettings);

    [LoggerMessage(EventId = 1109, Level = LogLevel.Information, Message = "Content of written settings json: {SettingsJson}")]
    public static partial void WrittenSettingsContent(ILogger logger, string settingsJson);

    [LoggerMessage(EventId = 1110, Level = LogLevel.Information, Message = "User settings file exists.")]
    public static partial void UserSettingsFileExists(ILogger logger);

    [LoggerMessage(EventId = 1111, Level = LogLevel.Information, Message = "User settings updated.")]
    public static partial void UserSettingsUpdated(ILogger logger);

    // --- StartupService ---
    [LoggerMessage(EventId = 1200, Level = LogLevel.Information, Message = "Found loadSections in `{FileName}` registering transformation for it with ID '{TransformationId}'")]
    public static partial void FoundLoadSections(ILogger logger, string fileName, Guid transformationId);

    // --- HomeScreenSectionService ---
    [LoggerMessage(EventId = 1300, Level = LogLevel.Error, Message = "An error occurred while creating section instances for user '{UserId}' and section '{Section}'")]
    public static partial void SectionInstanceError(ILogger logger, Exception exception, Guid userId, string? section);

    [LoggerMessage(EventId = 1301, Level = LogLevel.Debug, Message = "Failed to resolve section title link for AdditionalData '{AdditionalData}' and user '{UserId}'")]
    public static partial void SectionTitleLinkResolveFailed(ILogger logger, Exception exception, string additionalData, Guid userId);

    // --- RecentlyAddedShowsSection ---
    [LoggerMessage(EventId = 1400, Level = LogLevel.Information, Message = "Season '{SeasonName}' has been sorted based on an episode having a date created of: {DateCreated}.")]
    public static partial void SeasonSortedByEpisodeDate(ILogger logger, string seasonName, DateTime? dateCreated);

    [LoggerMessage(EventId = 1401, Level = LogLevel.Information, Message = "Item '{ItemName}' has been sorted based on the default behaviour with a value of: {DateCreated}.")]
    public static partial void ItemSortedByDefaultDate(ILogger logger, string itemName, DateTime? dateCreated);

    // --- UpcomingSectionBase ---
    [LoggerMessage(EventId = 1500, Level = LogLevel.Warning, Message = "{ServiceName} URL or API key not configured, skipping {SectionName}")]
    public static partial void ArrServiceNotConfigured(ILogger logger, string serviceName, string sectionName);

    [LoggerMessage(EventId = 1501, Level = LogLevel.Debug, Message = "Fetching {SectionName} from {StartDate} to {EndDate}")]
    public static partial void FetchingUpcomingSection(ILogger logger, string sectionName, DateTime startDate, DateTime endDate);

    [LoggerMessage(EventId = 1502, Level = LogLevel.Debug, Message = "No {SectionName} found from {ServiceName}")]
    public static partial void NoUpcomingItems(ILogger logger, string sectionName, string serviceName);

    [LoggerMessage(EventId = 1503, Level = LogLevel.Debug, Message = "Found {Count} upcoming items after filtering")]
    public static partial void FoundUpcomingItems(ILogger logger, int count);

    [LoggerMessage(EventId = 1504, Level = LogLevel.Error, Message = "Error fetching {SectionName} from {ServiceName}")]
    public static partial void UpcomingSectionError(ILogger logger, Exception exception, string sectionName, string serviceName);

    // --- ArrApiService ---
    [LoggerMessage(EventId = 1600, Level = LogLevel.Warning, Message = "{ServiceName} URL or API key not configured")]
    public static partial void ArrUrlOrKeyMissing(ILogger logger, string? serviceName);

    [LoggerMessage(EventId = 1601, Level = LogLevel.Debug, Message = "Fetching {ServiceName} calendar from {Url}")]
    public static partial void FetchingArrCalendar(ILogger logger, string? serviceName, string url);

    [LoggerMessage(EventId = 1602, Level = LogLevel.Error, Message = "Failed to fetch {ServiceName} calendar. Status: {StatusCode}, Reason: {ReasonPhrase}")]
    public static partial void ArrCalendarHttpFailed(ILogger logger, string? serviceName, System.Net.HttpStatusCode statusCode, string? reasonPhrase);

    [LoggerMessage(EventId = 1603, Level = LogLevel.Warning, Message = "Empty response from {ServiceName} calendar API")]
    public static partial void ArrCalendarEmpty(ILogger logger, string? serviceName);

    [LoggerMessage(EventId = 1604, Level = LogLevel.Debug, Message = "Successfully fetched {Count} calendar items from {ServiceName}")]
    public static partial void ArrCalendarFetched(ILogger logger, int count, string? serviceName);

    [LoggerMessage(EventId = 1605, Level = LogLevel.Error, Message = "HTTP error while fetching {ServiceName} calendar")]
    public static partial void ArrCalendarHttpError(ILogger logger, Exception exception, string? serviceName);

    [LoggerMessage(EventId = 1606, Level = LogLevel.Error, Message = "JSON parsing error while processing {ServiceName} calendar response")]
    public static partial void ArrCalendarJsonError(ILogger logger, Exception exception, string? serviceName);

    [LoggerMessage(EventId = 1607, Level = LogLevel.Error, Message = "Unexpected error while fetching {ServiceName} calendar")]
    public static partial void ArrCalendarUnexpectedError(ILogger logger, Exception exception, string? serviceName);

    // --- ImageCacheService ---
    [LoggerMessage(EventId = 1700, Level = LogLevel.Debug, Message = "Using cached image for {CacheKey}")]
    public static partial void UsingCachedImage(ILogger logger, string cacheKey);

    [LoggerMessage(EventId = 1701, Level = LogLevel.Debug, Message = "Evicted {Count} old cache entries")]
    public static partial void EvictedCacheEntries(ILogger logger, int count);

    [LoggerMessage(EventId = 1702, Level = LogLevel.Debug, Message = "Downloading image from {SourceUrl}")]
    public static partial void DownloadingImage(ILogger logger, string sourceUrl);

    [LoggerMessage(EventId = 1703, Level = LogLevel.Warning, Message = "Failed to download image from {SourceUrl}, status: {StatusCode}")]
    public static partial void ImageDownloadFailed(ILogger logger, string sourceUrl, System.Net.HttpStatusCode statusCode);

    [LoggerMessage(EventId = 1704, Level = LogLevel.Debug, Message = "Cached image {CacheKey} from {SourceUrl}")]
    public static partial void CachedImage(ILogger logger, string cacheKey, string sourceUrl);

    [LoggerMessage(EventId = 1705, Level = LogLevel.Error, Message = "Error downloading and caching image from {SourceUrl}")]
    public static partial void ImageCacheError(ILogger logger, Exception exception, string sourceUrl);

    [LoggerMessage(EventId = 1706, Level = LogLevel.Debug, Message = "Cache miss for key {CacheKey}")]
    public static partial void CacheMiss(ILogger logger, string cacheKey);

    [LoggerMessage(EventId = 1707, Level = LogLevel.Debug, Message = "Cache expired for key {CacheKey}")]
    public static partial void CacheExpired(ILogger logger, string cacheKey);

    [LoggerMessage(EventId = 1708, Level = LogLevel.Warning, Message = "Cache file missing for key {CacheKey}")]
    public static partial void CacheFileMissing(ILogger logger, string cacheKey);

    [LoggerMessage(EventId = 1709, Level = LogLevel.Error, Message = "Error reading cached image {CacheKey}")]
    public static partial void CacheReadError(ILogger logger, Exception exception, string cacheKey);

    [LoggerMessage(EventId = 1710, Level = LogLevel.Debug, Message = "Deleted expired cache file {FilePath}")]
    public static partial void DeletedExpiredCacheFile(ILogger logger, string filePath);

    [LoggerMessage(EventId = 1711, Level = LogLevel.Warning, Message = "Failed to delete expired cache file {FilePath}")]
    public static partial void FailedDeleteExpiredCacheFile(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(EventId = 1712, Level = LogLevel.Information, Message = "Cleared {Count} expired cache entries")]
    public static partial void ClearedExpiredCacheEntries(ILogger logger, int count);

    [LoggerMessage(EventId = 1713, Level = LogLevel.Warning, Message = "Failed to delete cache file {FilePath}")]
    public static partial void FailedDeleteCacheFile(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(EventId = 1714, Level = LogLevel.Warning, Message = "Failed to resize image from {OriginalWidth}x{OriginalHeight}")]
    public static partial void ImageResizeFailed(ILogger logger, int originalWidth, int originalHeight);

    [LoggerMessage(EventId = 1715, Level = LogLevel.Debug, Message = "Resized image from {OriginalWidth}x{OriginalHeight} to {NewWidth}x{NewHeight}")]
    public static partial void ImageResized(ILogger logger, int originalWidth, int originalHeight, int newWidth, int newHeight);

    [LoggerMessage(EventId = 1716, Level = LogLevel.Information, Message = "Loaded {Count} cached images from index")]
    public static partial void LoadedCacheIndex(ILogger logger, int count);

    [LoggerMessage(EventId = 1717, Level = LogLevel.Information, Message = "Cleared all cache entries")]
    public static partial void ClearedAllCacheEntries(ILogger logger);

    [LoggerMessage(EventId = 1718, Level = LogLevel.Warning, Message = "Failed to decode image for processing")]
    public static partial void ImageDecodeFailed(ILogger logger);

    [LoggerMessage(EventId = 1719, Level = LogLevel.Error, Message = "Error processing image")]
    public static partial void ImageProcessError(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1720, Level = LogLevel.Error, Message = "Error loading cache index")]
    public static partial void CacheIndexLoadError(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1721, Level = LogLevel.Error, Message = "Error saving cache index")]
    public static partial void CacheIndexSaveError(ILogger logger, Exception exception);

    // --- ImageCacheHelper ---
    [LoggerMessage(EventId = 1800, Level = LogLevel.Warning, Message = "Failed to cache image from {SourceUrl}, using original URL")]
    public static partial void ImageCacheFallback(ILogger logger, string sourceUrl);

    [LoggerMessage(EventId = 1801, Level = LogLevel.Error, Message = "Error caching image from {SourceUrl}")]
    public static partial void ImageCacheHelperError(ILogger logger, Exception exception, string sourceUrl);

    // --- ImageCacheCleanupTask ---
    [LoggerMessage(EventId = 1900, Level = LogLevel.Information, Message = "Starting image cache cleanup")]
    public static partial void ImageCacheCleanupStarted(ILogger logger);

    [LoggerMessage(EventId = 1901, Level = LogLevel.Information, Message = "Image cache cleanup completed")]
    public static partial void ImageCacheCleanupCompleted(ILogger logger);

    [LoggerMessage(EventId = 1902, Level = LogLevel.Error, Message = "Error during image cache cleanup")]
    public static partial void ImageCacheCleanupError(ILogger logger, Exception exception);

    // --- ModularHomeViewsController ---
    [LoggerMessage(EventId = 2000, Level = LogLevel.Error, Message = "Failed to get resource {Resource}")]
    public static partial void FailedGetResource(ILogger logger, string? resource);

    // --- TranslationManager ---
    [LoggerMessage(EventId = 1013, Level = LogLevel.Trace, Message = "Loading translation files")]
    public static partial void LoadingTranslationFiles(ILogger logger);

    // --- UpcomingSectionBase ---
    [LoggerMessage(EventId = 1505, Level = LogLevel.Warning, Message = "Plugin configuration not available")]
    public static partial void PluginConfigurationMissing(ILogger logger);
}
