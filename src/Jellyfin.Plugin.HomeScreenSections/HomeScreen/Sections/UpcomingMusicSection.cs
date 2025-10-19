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
    public class UpcomingMusicSection : UpcomingSectionBase<LidarrCalendarDto>
    {
        public override string? Section => "UpcomingMusic";
        
        public override string? DisplayText { get; set; } = "Upcoming Music";

        public UpcomingMusicSection(IUserManager userManager, IDtoService dtoService, ArrApiService arrApiService, ILogger<UpcomingMusicSection> logger)
            : base(userManager, dtoService, arrApiService, logger)
        {
        }

        protected override (string? url, string? apiKey) GetServiceConfiguration(PluginConfiguration config)
        {
            return (config.Lidarr.Url, config.Lidarr.ApiKey);
        }

        protected override (int value, TimeframeUnit unit) GetTimeframeConfiguration(PluginConfiguration config)
        {
            return (config.Lidarr.UpcomingTimeframeValue, config.Lidarr.UpcomingTimeframeUnit);
        }

        protected override LidarrCalendarDto[] GetCalendarItems(DateTime startDate, DateTime endDate)
        {
            return ArrApiService.GetArrCalendarAsync<LidarrCalendarDto>(ArrServiceType.Lidarr, startDate, endDate).GetAwaiter().GetResult() ?? [];
        }

        protected override IOrderedEnumerable<LidarrCalendarDto> FilterAndSortItems(LidarrCalendarDto[] items)
        {
            return items
                .Where(item => item.Monitored && !item.HasFile && item.ReleaseDate.HasValue)
                .OrderBy(item => item.ReleaseDate);
        }

        protected override string GetFallbackCoverUrl(LidarrCalendarDto missingItem)
        {
            return $"https://placehold.co/300x300/{GetRandomBgColor()}/FFF?text={Uri.EscapeDataString($"{missingItem.Title}\n{missingItem.Artist?.ArtistName}\nImage Not Found")}";
        }

        protected override BaseItemDto CreateDto(LidarrCalendarDto calendarItem, PluginConfiguration config)
        {

            DateTime releaseDate = calendarItem.ReleaseDate ?? DateTime.Now;
            string countdownText = CalculateCountdown(releaseDate, config);

            ArrImageDto? albumImage = calendarItem.Images?.FirstOrDefault(img => 
                string.Equals(img.CoverType, "cover", StringComparison.OrdinalIgnoreCase));

            Dictionary<string, string> providerIds = new Dictionary<string, string>
            {
                { "LidarrAlbumId", calendarItem.Id.ToString() },
                { "FormattedDate", countdownText },
                { "LidarrPoster", albumImage?.RemoteUrl ?? GetFallbackCoverUrl(calendarItem) }
            };

            return new BaseItemDto
            {
                Id = Guid.NewGuid(),
                Name = calendarItem.Title ?? "Unknown Album",
                Overview = $"{calendarItem.Artist?.ArtistName ?? "Unknown Artist"} - {calendarItem.AlbumType}",
                PremiereDate = calendarItem.ReleaseDate,
                Type = BaseItemKind.MusicAlbum,
                ProviderIds = providerIds,
                UserData = new UserItemDataDto
                {
                    Key = $"upcoming-album-{calendarItem.Id}",
                    PlaybackPositionTicks = 0,
                    IsFavorite = false,
                }
            };
        }

        protected override string GetServiceName() => "Lidarr";
        protected override string GetSectionName() => "upcoming music";

        public override IHomeScreenSection CreateInstance(Guid? userId, IEnumerable<IHomeScreenSection>? otherInstances = null)
        {
            return new UpcomingMusicSection(UserManager, DtoService, ArrApiService, (ILogger<UpcomingMusicSection>)Logger)
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
                ViewMode = SectionViewMode.Square,
                AllowViewModeChange = false,
                ContainerClass = "upcoming-music-section"
            };
        }
    }
}