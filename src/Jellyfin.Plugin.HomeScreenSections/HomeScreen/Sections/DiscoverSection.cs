using System.Globalization;
using System.Net;
using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Helpers;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using Jellyfin.Plugin.HomeScreenSections.Services;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections
{
    public class DiscoverSection : IHomeScreenSection, IAsyncHomeScreenSection
    {
        private readonly IUserManager m_userManager;
        private readonly ImageCacheService m_imageCacheService;
        private readonly SeerrApiService m_seerrApiService;
        private const int c_resultLimit = 20;
        private const int c_maxPages = 40;
        
        public virtual string? Section => "Discover";

        public virtual string? DisplayText { get; set; } = "Discover";
        public int? Limit => 1;
        public string? Route => null;
        public string? AdditionalData { get; set; }
        public object? OriginalPayload { get; } = null;

        protected virtual string JellyseerEndpoint => "/api/v1/discover/trending";
        protected virtual string? DefaultMediaType => null;
        
        public DiscoverSection(IUserManager userManager, ImageCacheService imageCacheService, SeerrApiService seerrApiService)
        {
            m_userManager = userManager;
            m_imageCacheService = imageCacheService;
            m_seerrApiService = seerrApiService;
        }
        
        public QueryResult<BaseItemDto> GetResults(HomeScreenSectionPayload payload, IQueryCollection queryCollection)
        {
            return GetResultsAsync(payload, queryCollection, CancellationToken.None).GetAwaiter().GetResult();
        }

        public async Task<QueryResult<BaseItemDto>> GetResultsAsync(HomeScreenSectionPayload payload, IQueryCollection queryCollection, CancellationToken cancellationToken)
        {
            string? jellyseerrUrl = HomeScreenSectionsPlugin.Instance.Configuration.JellyseerrUrl;
            if (string.IsNullOrEmpty(jellyseerrUrl))
            {
                return new QueryResult<BaseItemDto>();
            }

            User? user = m_userManager.GetUserById(payload.UserId);
            if (user == null)
            {
                throw new SeerrRequestException(HttpStatusCode.BadRequest, "The Jellyfin user does not exist.");
            }

            using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(SeerrApiService.RequestTimeout);
            try
            {
                return await GetDiscoverResultsAsync(user.Username, deadline.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new SeerrRequestException(HttpStatusCode.GatewayTimeout, "The Seerr discovery request timed out.");
            }
        }

        private async Task<QueryResult<BaseItemDto>> GetDiscoverResultsAsync(string username, CancellationToken cancellationToken)
        {
            int? jellyseerrUserId = await m_seerrApiService.GetUserIdAsync(username, cancellationToken);
            if (jellyseerrUserId == null)
            {
                return new QueryResult<BaseItemDto>();
            }

            string? jellyseerrExternalUrl = HomeScreenSectionsPlugin.Instance.Configuration.JellyseerrExternalUrl;
            string jellyseerrDisplayUrl = !string.IsNullOrEmpty(jellyseerrExternalUrl)
                ? jellyseerrExternalUrl : HomeScreenSectionsPlugin.Instance.Configuration.JellyseerrUrl!;
            string[] preferredLanguages = (HomeScreenSectionsPlugin.Instance.Configuration.JellyseerrPreferredLanguages ?? string.Empty)
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            List<BaseItemDto> returnItems = new List<BaseItemDto>();
            HashSet<string> seenItems = new HashSet<string>();

            for (int page = 1; page <= c_maxPages && returnItems.Count < c_resultLimit; page++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                JObject response = await m_seerrApiService.GetAsync($"{JellyseerEndpoint}?page={page}", jellyseerrUserId, cancellationToken);
                JArray results = SeerrApiService.GetResults(response);
                if (results.Count == 0)
                {
                    break;
                }

                bool madeProgress = false;
                foreach (JObject item in results.OfType<JObject>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (item["id"]?.Type != JTokenType.Integer || !int.TryParse(item["id"]?.ToString(), out int itemId) || itemId <= 0)
                    {
                        continue;
                    }

                    string? mediaType = GetString(item, "mediaType") ?? DefaultMediaType;
                    if (mediaType != "movie" && mediaType != "tv")
                    {
                        continue;
                    }

                    if (!seenItems.Add($"{mediaType}:{itemId}"))
                    {
                        continue;
                    }

                    // Progress means new remote IDs, not eligible cards: a filtered page can precede an eligible one.
                    madeProgress = true;
                    if ((item["adult"] != null && item["adult"]!.Type != JTokenType.Null &&
                        (item["adult"]!.Type != JTokenType.Boolean || item.Value<bool>("adult"))) ||
                        (item["mediaInfo"] != null && item["mediaInfo"]!.Type != JTokenType.Null) ||
                        (preferredLanguages.Length > 0 && !preferredLanguages.Contains(GetString(item, "originalLanguage"))))
                    {
                        continue;
                    }

                    string? dateText = GetString(item, "firstAirDate") ?? GetString(item, "releaseDate");
                    DateTime premiereDate = DateTime.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime parsedDate)
                        ? parsedDate : new DateTime(1970, 1, 1);
                    string posterPath = GetString(item, "posterPath") ?? "404";
                    string cachedImageUrl = await ImageCacheHelper.GetCachedImageUrlAsync(m_imageCacheService, $"https://image.tmdb.org/t/p/w600_and_h900_bestv2{posterPath}", cancellationToken);
                    float rating = float.TryParse((item["vote_average"] ?? item["voteAverage"])?.ToString(), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out float parsedRating) && float.IsFinite(parsedRating) ? parsedRating : 0f;
                    returnItems.Add(new BaseItemDto()
                    {
                        Name = GetString(item, "title") ?? GetString(item, "name"),
                        OriginalTitle = GetString(item, "originalTitle") ?? GetString(item, "originalName"),
                        SourceType = mediaType,
                        CommunityRating = rating > 0 ? rating : null,
                        ProviderIds = new Dictionary<string, string>()
                        {
                            { "JellyseerrRoot", jellyseerrDisplayUrl },
                            { "Jellyseerr", itemId.ToString(CultureInfo.InvariantCulture) },
                            { "JellyseerrPoster", cachedImageUrl }
                        },
                        PremiereDate = premiereDate
                    });
                    if (returnItems.Count == c_resultLimit)
                    {
                        break;
                    }
                }

                if (!madeProgress || (int.TryParse(response["totalPages"]?.ToString(), out int totalPages) && page >= totalPages))
                {
                    break;
                }
            }

            return new QueryResult<BaseItemDto>()
            {
                Items = returnItems,
                StartIndex = 0,
                TotalRecordCount = returnItems.Count
            };
        }

        private static string? GetString(JObject item, string property)
        {
            return item[property]?.Type == JTokenType.String ? item.Value<string>(property) : null;
        }

        protected string GetCachedImageUrl(string sourceUrl)
        {
            return ImageCacheHelper.GetCachedImageUrl(m_imageCacheService, sourceUrl);
        }

        public IEnumerable<IHomeScreenSection> CreateInstances(Guid? userId, int instanceCount)
        {
            yield return this;
        }

        public HomeScreenSectionInfo GetInfo()
        {
            return new HomeScreenSectionInfo()
            {
                Section = Section,
                DisplayText = DisplayText,
                AdditionalData = AdditionalData,
                Route = Route,
                Limit = Limit ?? 1,
                OriginalPayload = OriginalPayload,
                ViewMode = SectionViewMode.Portrait,
                AllowViewModeChange = false,
                PluginConfigurationOptions = (this as IHomeScreenSection).GetPluginConfigurationOptions().ToArray()
            };
        }
    }
}