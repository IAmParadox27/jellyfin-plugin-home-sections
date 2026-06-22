using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Helpers;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using Jellyfin.Plugin.HomeScreenSections.Services;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections
{
    public abstract class UpcomingSectionBase<T> : IHomeScreenSection where T : class
    {
        public abstract string? Section { get; }
        public abstract string? DisplayText { get; set; }
        public virtual int? Limit => 1;
        public virtual string? Route => null;
        public string? AdditionalData { get; set; }
        public object? OriginalPayload { get; set; } = null;
        
        protected IUserManager UserManager { get; }
        protected IDtoService DtoService { get; }
        protected ArrApiService ArrApiService { get; }
        protected ImageCacheService ImageCacheService { get; }
        protected ILogger Logger { get; }

        protected UpcomingSectionBase(IUserManager userManager, IDtoService dtoService, ArrApiService arrApiService, ImageCacheService imageCacheService, ILogger logger)
        {
            UserManager = userManager;
            DtoService = dtoService;
            ArrApiService = arrApiService;
            ImageCacheService = imageCacheService;
            Logger = logger;
        }

        public QueryResult<BaseItemDto> GetResults(HomeScreenSectionPayload payload, IQueryCollection queryCollection)
        {
            try
            {
                PluginConfiguration? config = HomeScreenSectionsPlugin.Instance?.Configuration;
                if (config == null)
                {
                    Logger.LogWarning("Plugin configuration not available");
                    return new QueryResult<BaseItemDto>();
                }

                // Check if service is configured
                (string? url, string? apiKey) = GetServiceConfiguration(config);
                if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(apiKey))
                {
                    Logger.LogWarning("{ServiceName} URL or API key not configured, skipping {SectionName}", GetServiceName(), GetSectionName());
                    return new QueryResult<BaseItemDto>();
                }

                DateTime startDate = DateTime.UtcNow;
                (int timeframeValue, TimeframeUnit timeframeUnit) = GetTimeframeConfiguration(config);
                DateTime endDate = ArrApiService.CalculateEndDate(startDate, timeframeValue, timeframeUnit);
                
                Logger.LogDebug("Fetching {SectionName} from {StartDate} to {EndDate}", GetSectionName(), startDate, endDate);

                T[] calendarItems = GetCalendarItems(startDate, endDate);
                
                if (calendarItems == null || calendarItems.Length == 0)
                {
                    Logger.LogDebug("No {SectionName} found from {ServiceName}", GetSectionName(), GetServiceName());
                    return new QueryResult<BaseItemDto>();
                }

                string language = queryCollection["language"].FirstOrDefault() ?? queryCollection["Language"].FirstOrDefault() ?? "en";
                Logger.LogInformation("GetResults for {SectionName} using language: {Language} (Query values: language={QueryLang}, Language={QueryLangUpper})", GetSectionName(), language, queryCollection["language"].FirstOrDefault(), queryCollection["Language"].FirstOrDefault());
                T[] upcomingItems = [.. FilterAndSortItems(calendarItems, language).Take(16)];

                Logger.LogDebug("Found {Count} upcoming items after filtering", upcomingItems.Length);

                BaseItemDto[] dtoItems = [.. upcomingItems.Select(item => CreateDto(item, config, language))];

                return new QueryResult<BaseItemDto>(dtoItems);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error fetching {SectionName} from {ServiceName}", GetSectionName(), GetServiceName());
                return new QueryResult<BaseItemDto>();
            }
        }

        protected string CalculateCountdown(DateTime releaseDate, PluginConfiguration config, string language = "en")
        {
            DateTime releaseDateLocal = releaseDate.ToLocalTime();
            // Calculate the difference in calendar days
            int totalDays = (releaseDateLocal.Date - DateTime.Now.Date).Days;

            var translationManager = (ITranslationManager?)HomeScreenSectionsPlugin.Instance?.ServiceProvider?.GetService(typeof(ITranslationManager));

            string GetTranslation(string key, string fallbackText)
            {
                if (translationManager != null)
                {
                    return translationManager.Translate(key, language, fallbackText);
                }
                return fallbackText;
            }
            
            string countdownText;
            if (totalDays <= 0)
            {
                countdownText = GetTranslation("CountdownToday", "Today!");
            }
            else if (totalDays < 7)
            {
                string dayText = totalDays == 1 ? GetTranslation("CountdownDay", "Day") : GetTranslation("CountdownDays", "Days");
                countdownText = $"{totalDays} {dayText}";
            }
            else if (totalDays < 30)
            {
                countdownText = FormatTimeUnit(totalDays / 7, totalDays % 7, "Week", "Day", language, translationManager);
            }
            else if (totalDays < 365)
            {
                countdownText = FormatTimeUnit(totalDays / 30, (totalDays % 30) / 7, "Month", "Week", language, translationManager);
            }
            else
            {
                countdownText = FormatTimeUnit(totalDays / 365, (totalDays % 365) / 30, "Year", "Month", language, translationManager);
            }

            return $"{countdownText} - {ArrApiService.FormatDate(releaseDateLocal, config.DateFormat, config.DateDelimiter)}";
        }

        private static string FormatTimeUnit(int primaryValue, int secondaryValue, string primaryUnit, string secondaryUnit, string language, ITranslationManager? translationManager)
        {
            string GetTranslation(string key, string fallbackText)
            {
                if (translationManager != null)
                {
                    return translationManager.Translate(key, language, fallbackText);
                }
                return fallbackText;
            }

            string primaryUnitPlural = primaryUnit + "s";
            string primaryTranslatedUnit = primaryValue == 1 ? GetTranslation($"Countdown{primaryUnit}", primaryUnit) : GetTranslation($"Countdown{primaryUnitPlural}", primaryUnitPlural);
            string primaryText = $"{primaryValue} {primaryTranslatedUnit}";
            
            if (secondaryValue > 0)
            {
                string secondaryUnitPlural = secondaryUnit + "s";
                string secondaryTranslatedUnit = secondaryValue == 1 ? GetTranslation($"Countdown{secondaryUnit}", secondaryUnit) : GetTranslation($"Countdown{secondaryUnitPlural}", secondaryUnitPlural);
                string secondaryText = $"{secondaryValue} {secondaryTranslatedUnit}";
                return $"{primaryText}, {secondaryText}";
            }
            
            return primaryText;
        }

        protected static string GetRandomBgColor()
        {
            return $"{Random.Shared.Next(0, 128):X2}{Random.Shared.Next(0, 128):X2}{Random.Shared.Next(0, 128):X2}";
        }

        protected virtual string GetFallbackCoverUrl(T missingItem)
        {
            return $"https://placehold.co/250x400/{GetRandomBgColor()}/FFF?text={Uri.EscapeDataString("Unknown Item\nImage Not Found")}";
        }
        
        protected string GetCachedImageUrl(string? sourceUrl)
        {
            return ImageCacheHelper.GetCachedImageUrl(ImageCacheService, sourceUrl, Logger);
        }

        // Abstract methods that subclasses must implement
        protected abstract (string? url, string? apiKey) GetServiceConfiguration(PluginConfiguration config);
        protected abstract (int value, TimeframeUnit unit) GetTimeframeConfiguration(PluginConfiguration config);
        protected abstract T[] GetCalendarItems(DateTime startDate, DateTime endDate);
        protected abstract IOrderedEnumerable<T> FilterAndSortItems(T[] items, string language);
        protected abstract BaseItemDto CreateDto(T item, PluginConfiguration config, string language);
        protected abstract string GetServiceName();
        protected abstract string GetSectionName();

        public abstract IEnumerable<IHomeScreenSection> CreateInstances(Guid? userId, int instanceCount);
        public abstract HomeScreenSectionInfo GetInfo();
    }
}