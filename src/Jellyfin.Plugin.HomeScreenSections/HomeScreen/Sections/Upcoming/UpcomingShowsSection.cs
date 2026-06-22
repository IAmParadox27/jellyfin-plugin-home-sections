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
    public class UpcomingShowsSection : UpcomingSectionBase<SonarrCalendarDto>
    {
        public override string? Section => "UpcomingShows";
        
        public override string? DisplayText { get; set; } = "Upcoming Shows";

        public UpcomingShowsSection(IUserManager userManager, IDtoService dtoService, ArrApiService arrApiService, ImageCacheService imageCacheService, ILogger<UpcomingShowsSection> logger)
            : base(userManager, dtoService, arrApiService, imageCacheService, logger)
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

        protected override IOrderedEnumerable<SonarrCalendarDto> FilterAndSortItems(SonarrCalendarDto[] items, string language)
        {
            var config = HomeScreenSectionsPlugin.Instance?.Configuration;
            var filtered = items.Where(item => item.Monitored && !item.HasFile && item.AirDateUtc.HasValue);

            if (config?.Sonarr?.GroupUpcoming == true)
            {
                return filtered
                    .GroupBy(item => new { item.SeriesId, item.SeasonNumber })
                    .Select(g => 
                    {
                        var orderedGroup = g.OrderBy(e => e.AirDateUtc).ToList();
                        var firstEpisode = orderedGroup.First();
                        firstEpisode.TotalEpisodesInGroup = orderedGroup.Count;
                        if (orderedGroup.Count > 1)
                        {
                            firstEpisode.LastEpisodeNumberInGroup = orderedGroup.Last().EpisodeNumber;
                            firstEpisode.GroupFrequencyText = DetermineGroupFrequency(orderedGroup, language);
                        }
                        return firstEpisode;
                    })
                    .OrderBy(item => item.AirDateUtc);
            }
            else if (config?.Sonarr?.GroupUpcomingNextOnly == true)
            {
                var validTitlesFiltered = filtered.Where(item => 
                    !string.IsNullOrWhiteSpace(item.Title) &&
                    !string.Equals(item.Title, "TBA", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(item.Title, "TDA", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(item.Title, "TBD", StringComparison.OrdinalIgnoreCase));

                return validTitlesFiltered
                    .GroupBy(item => new { item.SeriesId, item.SeasonNumber })
                    .Select(g => g.OrderBy(e => e.AirDateUtc).First())
                    .OrderBy(item => item.AirDateUtc);
            }

            return filtered.OrderBy(item => item.AirDateUtc);
        }

        private string DetermineGroupFrequency(List<SonarrCalendarDto> group, string language)
        {
            var translationManager = (ITranslationManager?)HomeScreenSectionsPlugin.Instance?.ServiceProvider?.GetService(typeof(ITranslationManager));
            
            string GetTranslation(string key, string fallbackText)
            {
                if (translationManager != null)
                {
                    return translationManager.Translate(key, language, fallbackText);
                }
                return fallbackText;
            }

            var intervals = new List<double>();
            for (int i = 0; i < group.Count - 1; i++)
            {
                if (group[i].AirDateUtc.HasValue && group[i+1].AirDateUtc.HasValue)
                {
                    intervals.Add((group[i+1].AirDateUtc.Value - group[i].AirDateUtc.Value).TotalDays);
                }
            }

            if (intervals.Count == 0) return string.Empty;

            double averageDays = intervals.Average();
            
            if (averageDays >= 6.0 && averageDays <= 8.0)
            {
                return GetTranslation("FrequencyWeekly", "Weekly");
            }
            if (averageDays < 2.0)
            {
                return GetTranslation("FrequencyDaily", "Daily");
            }
            
            return string.Empty;
        }

        protected override string GetFallbackCoverUrl(SonarrCalendarDto missingItem)
        {
            return $"https://placehold.co/250x400/{GetRandomBgColor()}/FFF?text={Uri.EscapeDataString($"{missingItem.Series?.Title}\n{missingItem.Title}\nImage Not Found")}";
        }

        protected override BaseItemDto CreateDto(SonarrCalendarDto calendarItem, PluginConfiguration config, string language)
        {
            DateTime airDate = calendarItem.AirDateUtc ?? DateTime.Now;
            string countdownText = CalculateCountdown(airDate, config, language);

            string episodeInfo;
            if (config?.Sonarr?.GroupUpcoming == true && calendarItem.TotalEpisodesInGroup > 1 && calendarItem.LastEpisodeNumberInGroup.HasValue)
            {
                string frequencySuffix = !string.IsNullOrEmpty(calendarItem.GroupFrequencyText) ? $" ({calendarItem.GroupFrequencyText})" : "";
                episodeInfo = $"S{calendarItem.SeasonNumber:D2}E{calendarItem.EpisodeNumber:D2}-{calendarItem.LastEpisodeNumberInGroup:D2}{frequencySuffix}";
            }
            else
            {
                episodeInfo = $"S{calendarItem.SeasonNumber:D2}E{calendarItem.EpisodeNumber:D2} - {calendarItem.Title}";
            }

            ArrImageDto? posterImage = calendarItem.Series?.Images?.FirstOrDefault(img => 
                string.Equals(img.CoverType, "poster", StringComparison.OrdinalIgnoreCase));

            string sourceImageUrl = posterImage?.RemoteUrl ?? GetFallbackCoverUrl(calendarItem);
            string cachedImageUrl = GetCachedImageUrl(sourceImageUrl);

            // Create provider IDs to store external image URL and metadata
            Dictionary<string, string> providerIds = new Dictionary<string, string>
            {
                { "SonarrSeriesId", calendarItem.SeriesId.ToString() },
                { "SonarrEpisodeId", calendarItem.Id.ToString() },
                { "EpisodeInfo", episodeInfo },
                { "FormattedDate", countdownText },
                { "SonarrPoster", cachedImageUrl }
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

        public override IEnumerable<IHomeScreenSection> CreateInstances(Guid? userId, int instanceCount)
        {
            yield return new UpcomingShowsSection(UserManager, DtoService, ArrApiService, ImageCacheService, (ILogger<UpcomingShowsSection>)Logger)
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
