using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using Jellyfin.Plugin.HomeScreenSections.Services;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections.Upcoming
{
    public class UpcomingMoviesSection : UpcomingSectionBase<RadarrCalendarDto>
    {
        public override string? Section => "UpcomingMovies";
        
        public override string? DisplayText { get; set; } = "Upcoming Movies";

        public UpcomingMoviesSection(IUserManager userManager, IDtoService dtoService, ArrApiService arrApiService, ImageCacheService imageCacheService, ILogger<UpcomingMoviesSection> logger)
            : base(userManager, dtoService, arrApiService, imageCacheService, logger)
        {
        }

        protected override (string? url, string? apiKey) GetServiceConfiguration(PluginConfiguration config)
        {
            return (config.Radarr.Url, config.Radarr.ApiKey);
        }

        protected override (int value, TimeframeUnit unit) GetTimeframeConfiguration(PluginConfiguration config)
        {
            return (config.Radarr.UpcomingTimeframeValue, config.Radarr.UpcomingTimeframeUnit);
        }

        protected override RadarrCalendarDto[] GetCalendarItems(DateTime startDate, DateTime endDate)
        {
            return ArrApiService.GetArrCalendarAsync<RadarrCalendarDto>(ArrServiceType.Radarr, startDate, endDate).GetAwaiter().GetResult() ?? [];
        }

        private DateTime GetEarliestReleaseDate(RadarrCalendarDto item, PluginConfiguration config)
        {
            var candidates = new[]
            {
                config.Radarr.ConsiderCinemaRelease ? item.InCinemas : null,
                config.Radarr.ConsiderPhysicalRelease ? item.PhysicalRelease : null,
                config.Radarr.ConsiderDigitalRelease ? item.DigitalRelease : null
            };
            return candidates.Where(date => date.HasValue).Select(date => date!.Value).Min();
        }

        protected override IOrderedEnumerable<RadarrCalendarDto> FilterAndSortItems(RadarrCalendarDto[] items)
        {
            var config = HomeScreenSectionsPlugin.Instance.Configuration;
            return items
                .Where(item => 
                {
                    if (!item.Monitored || item.HasFile)
                        return false;
                    
                    bool hasValidRelease = 
                        (config.Radarr.ConsiderCinemaRelease && item.InCinemas.HasValue) ||
                        (config.Radarr.ConsiderPhysicalRelease && item.PhysicalRelease.HasValue) ||
                        (config.Radarr.ConsiderDigitalRelease && item.DigitalRelease.HasValue);
                    
                    return hasValidRelease;
                })
                .OrderBy(item => GetEarliestReleaseDate(item, config));
        }

        protected override string GetFallbackCoverUrl(RadarrCalendarDto missingItem)
        {
            return $"https://placehold.co/250x400/{GetRandomBgColor()}/FFF?text={Uri.EscapeDataString($"{missingItem.Title}\nImage Not Found")}";
        }

        protected override BaseItemDto CreateDto(RadarrCalendarDto calendarItem, PluginConfiguration config)
        {
            DateTime releaseDate = GetEarliestReleaseDate(calendarItem, config);
            string countdownText = CalculateCountdown(releaseDate, config);

            string yearInfo = calendarItem.Year > 0 ? $" ({calendarItem.Year})" : "";

            ArrImageDto? posterImage = calendarItem.Images?.FirstOrDefault(img => 
                string.Equals(img.CoverType, "poster", StringComparison.OrdinalIgnoreCase));

            string sourceImageUrl = posterImage?.RemoteUrl ?? GetFallbackCoverUrl(calendarItem);
            string cachedImageUrl = GetCachedImageUrl(sourceImageUrl);

            // Create provider IDs to store external image URL and metadata
            Dictionary<string, string> providerIds = new Dictionary<string, string>
            {
                { "RadarrMovieId", calendarItem.Id.ToString() },
                { "YearInfo", yearInfo },
                { "FormattedDate", countdownText },
                { "RadarrPoster", cachedImageUrl }
            };

            return new BaseItemDto
            {
                Id = Guid.NewGuid(),
                Name = calendarItem.Title ?? "Unknown Movie",
                Type = BaseItemKind.Movie,
                PremiereDate = releaseDate,
                ProductionYear = calendarItem.Year > 0 ? calendarItem.Year : null,
                ProviderIds = providerIds,
                UserData = new UserItemDataDto
                {
                    Key = $"upcoming-movie-{calendarItem.Id}",
                    PlaybackPositionTicks = 0,
                    IsFavorite = false
                }
            };
        }

        protected override string GetServiceName() => "Radarr";

        protected override string GetSectionName() => "upcoming movies";

        public override IEnumerable<IHomeScreenSection> CreateInstances(Guid? userId, int instanceCount)
        {
            yield return new UpcomingMoviesSection(UserManager, DtoService, ArrApiService, ImageCacheService, (ILogger<UpcomingMoviesSection>)Logger)
            {
                DisplayText = DisplayText,
                AdditionalData = AdditionalData,
                OriginalPayload = OriginalPayload
            };
        }
        
        public override HomeScreenSectionInfo GetInfo()
        {
            return new HomeScreenSectionInfo
            {
                Section = Section,
                DisplayText = DisplayText,
                AdditionalData = AdditionalData,
                Route = Route,
                Limit = Limit ?? 1,
                OriginalPayload = OriginalPayload,
                ViewMode = SectionViewMode.Portrait,
                AllowViewModeChange = false,
                ContainerClass = "upcoming-movies-section"
            };
        }
    }
}