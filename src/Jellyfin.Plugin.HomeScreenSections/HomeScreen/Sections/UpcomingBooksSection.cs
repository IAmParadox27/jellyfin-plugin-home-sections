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
    public class UpcomingBooksSection : UpcomingSectionBase<ReadarrCalendarDto>
    {
        public override string? Section => "UpcomingBooks";
        
        public override string? DisplayText { get; set; } = "Upcoming Books";

        public UpcomingBooksSection(IUserManager userManager, IDtoService dtoService, ArrApiService arrApiService, ILogger<UpcomingBooksSection> logger)
            : base(userManager, dtoService, arrApiService, logger)
        {
        }

        protected override (string? url, string? apiKey) GetServiceConfiguration(PluginConfiguration config)
        {
            return (config.Readarr.Url, config.Readarr.ApiKey);
        }

        protected override (int value, TimeframeUnit unit) GetTimeframeConfiguration(PluginConfiguration config)
        {
            return (config.Readarr.UpcomingTimeframeValue, config.Readarr.UpcomingTimeframeUnit);
        }

        protected override ReadarrCalendarDto[] GetCalendarItems(DateTime startDate, DateTime endDate)
        {
            return ArrApiService.GetArrCalendarAsync<ReadarrCalendarDto>(ArrServiceType.Readarr, startDate, endDate).GetAwaiter().GetResult() ?? [];
        }

        protected override IOrderedEnumerable<ReadarrCalendarDto> FilterAndSortItems(ReadarrCalendarDto[] items)
        {
            return items
                .Where(item => item.Monitored && !item.HasFile && item.ReleaseDate.HasValue)
                .OrderBy(item => item.ReleaseDate);
        }

        protected override string GetFallbackCoverUrl(ReadarrCalendarDto missingItem)
        {
            return $"https://placehold.co/250x400/{GetRandomBgColor()}/FFF?text={Uri.EscapeDataString($"{missingItem.Title}\n{missingItem.Author?.AuthorName}\nImage Not Found")}";
        }

        protected override BaseItemDto CreateDto(ReadarrCalendarDto calendarItem, PluginConfiguration config)
        {
            DateTime releaseDate = calendarItem.ReleaseDate ?? DateTime.Now;
            string countdownText = CalculateCountdown(releaseDate, config);

            ArrImageDto? posterImage = calendarItem.Images?.FirstOrDefault(img => 
                string.Equals(img.CoverType, "cover", StringComparison.OrdinalIgnoreCase));

            Dictionary<string, string> providerIds = new Dictionary<string, string>
            {
                { "ReadarrBookId", calendarItem.Id.ToString() },
                { "FormattedDate", countdownText },
                { "ReadarrPoster", posterImage?.RemoteUrl ?? GetFallbackCoverUrl(calendarItem) }
            };

            return new BaseItemDto
            {
                Id = Guid.NewGuid(),
                Name = calendarItem.Title ?? "Unknown Book",
                Overview = (calendarItem.Author?.AuthorName ?? "Unknown Author") + (string.IsNullOrEmpty(calendarItem.SeriesTitle) ? "" : " - " + calendarItem.SeriesTitle),
                PremiereDate = calendarItem.ReleaseDate,
                Type = BaseItemKind.Book,
                ProviderIds = providerIds,
                UserData = new UserItemDataDto
                {
                    Key = $"upcoming-book-{calendarItem.Id}",
                    PlaybackPositionTicks = 0,
                    IsFavorite = false,
                }
            };
        }

        protected override string GetServiceName() => "Readarr";
        protected override string GetSectionName() => "upcoming books";

        public override IHomeScreenSection CreateInstance(Guid? userId, IEnumerable<IHomeScreenSection>? otherInstances = null)
        {
            return new UpcomingBooksSection(UserManager, DtoService, ArrApiService, (ILogger<UpcomingBooksSection>)Logger)
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
                ContainerClass = "upcoming-books-section"
            };
        }
    }
}