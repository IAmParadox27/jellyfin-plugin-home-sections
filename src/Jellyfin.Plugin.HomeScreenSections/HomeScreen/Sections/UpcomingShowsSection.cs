using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using Jellyfin.Plugin.HomeScreenSections.Services;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections
{
    public class UpcomingShowsSection : UpcomingSectionBase<SonarrCalendarDto>
    {
        public override string? Section => "UpcomingShows";
        
        public override string? DisplayText { get; set; } = "Upcoming Shows";

        public UpcomingShowsSection(IUserManager userManager, IDtoService dtoService, ArrApiService arrApiService, ILogger<UpcomingShowsSection> logger)
            : base(userManager, dtoService, arrApiService, logger)
        {
        }

        protected override (string? url, string? apiKey) GetServiceConfiguration(PluginConfiguration config)
        {
            return (config.Sonarr.Url, config.Sonarr.ApiKey);
        }

        protected override (int value, TimeframeUnit unit) GetTimeframeConfiguration(PluginConfiguration config)
        {
            return (config.Sonarr.UpcomingTimeframeValue, config.Sonarr.UpcomingTimeframeUnit);
        }

        protected override SonarrCalendarDto[] GetCalendarItems(DateTime startDate, DateTime endDate)
        {
            return ArrApiService.GetArrCalendarAsync<SonarrCalendarDto>(ArrServiceType.Sonarr, startDate, endDate).GetAwaiter().GetResult() ?? [];
        }

        protected override IOrderedEnumerable<SonarrCalendarDto> FilterAndSortItems(SonarrCalendarDto[] items)
        {
            return items
                .Where(item => item.Monitored && !item.HasFile && item.AirDateUtc.HasValue)
                .OrderBy(item => item.AirDateUtc);
        }

        protected override string GetFallbackCoverUrl(SonarrCalendarDto missingItem)
        {
            return $"https://placehold.co/250x400/{GetRandomBgColor()}/FFF?text={Uri.EscapeDataString($"{missingItem.Series?.Title}\n{missingItem.Title}\nImage Not Found")}";
        }

        protected override BaseItemDto CreateDto(SonarrCalendarDto calendarItem, PluginConfiguration config)
        {
            DateTime airDate = calendarItem.AirDateUtc ?? DateTime.Now;
            string countdownText = CalculateCountdown(airDate, config);

            string episodeInfo = $"S{calendarItem.SeasonNumber:D2}E{calendarItem.EpisodeNumber:D2} - {calendarItem.Title}";

            ArrImageDto? posterImage = calendarItem.Series?.Images?.FirstOrDefault(img => 
                string.Equals(img.CoverType, "poster", StringComparison.OrdinalIgnoreCase));

            // Create provider IDs to store external image URL and metadata
            Dictionary<string, string> providerIds = new Dictionary<string, string>
            {
                { "SonarrSeriesId", calendarItem.SeriesId.ToString() },
                { "SonarrEpisodeId", calendarItem.Id.ToString() },
                { "EpisodeInfo", episodeInfo },
                { "FormattedDate", countdownText },
                { "SonarrPoster", posterImage?.RemoteUrl ?? GetFallbackCoverUrl(calendarItem) }
            };

            return new BaseItemDto
            {
                Id = Guid.NewGuid(),
                Name = calendarItem.Series?.Title ?? "Unknown Series",
                Type = BaseItemKind.Episode,
                PremiereDate = calendarItem.AirDateUtc,
                SeriesName = calendarItem.Series?.Title,
                IndexNumber = calendarItem.EpisodeNumber,
                ParentIndexNumber = calendarItem.SeasonNumber,
                ProviderIds = providerIds,
                UserData = new UserItemDataDto
                {
                    Key = $"upcoming-{calendarItem.Id}",
                    PlaybackPositionTicks = 0,
                    IsFavorite = false
                }
            };
        }

        protected override string GetServiceName() => "Sonarr";

        protected override string GetSectionName() => "upcoming shows";

        public override IHomeScreenSection CreateInstance(Guid? userId, IEnumerable<IHomeScreenSection>? otherInstances = null)
        {
            return new UpcomingShowsSection(UserManager, DtoService, ArrApiService, (ILogger<UpcomingShowsSection>)Logger)
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
                ContainerClass = "upcoming-shows-section"
            };
        }
    }
}
