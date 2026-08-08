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
    public class DiscoverSection : IHomeScreenSection
    {
        private readonly IUserManager _userManager;
        private readonly ImageCacheService _imageCacheService;
        
        public virtual string? Section => "Discover";

        public virtual string? DisplayText { get; set; } = "Discover";
        public int? Limit => 1;
        public string? Route => null;
        public string? AdditionalData { get; set; }
        public object? OriginalPayload { get; }

        protected virtual string JellyseerEndpoint => "/api/v1/discover/trending";
        
        public DiscoverSection(IUserManager userManager, ImageCacheService imageCacheService)
        {
            _userManager = userManager;
            _imageCacheService = imageCacheService;
        }
        
        public QueryResult<BaseItemDto> GetResults(HomeScreenSectionPayload payload, IQueryCollection queryCollection)
        {
            string? jellyseerrUrl = HomeScreenSectionsPlugin.Instance.Configuration.JellyseerrUrl;
            string? jellyseerrExternalUrl = HomeScreenSectionsPlugin.Instance.Configuration.JellyseerrExternalUrl;
            string? jellyseerrDisplayUrl = !string.IsNullOrEmpty(jellyseerrExternalUrl) ? jellyseerrExternalUrl : jellyseerrUrl;

            if (string.IsNullOrEmpty(jellyseerrUrl))
            {
                return new QueryResult<BaseItemDto>();
            }
            
            User? user = _userManager.GetUserById(payload.UserId);
            if (user == null)
            {
                return new QueryResult<BaseItemDto>();
            }

            using HttpClient client = JellyseerrHelper.CreateClient(jellyseerrUrl);
            int? jellyseerrUserId = JellyseerrHelper.ResolveUserId(client, user.Username);
            if (jellyseerrUserId == null)
            {
                return new QueryResult<BaseItemDto>();
            }
            
            client.DefaultRequestHeaders.Add(
                "X-Api-User",
                jellyseerrUserId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));

            List<BaseItemDto> returnItems = FetchDiscoverItems(client, jellyseerrDisplayUrl ?? jellyseerrUrl);
            return new QueryResult<BaseItemDto>()
            {
                Items = returnItems,
                StartIndex = 0,
                TotalRecordCount = returnItems.Count
            };
        }


        private List<BaseItemDto> FetchDiscoverItems(HttpClient client, string jellyseerrDisplayUrl)
        {
            List<BaseItemDto> returnItems = [];
            int page = 1;
            do
            {
                HttpResponseMessage discoverResponse = client.GetAsync($"{JellyseerEndpoint}?page={page}").GetAwaiter().GetResult();
                if (discoverResponse.IsSuccessStatusCode)
                {
                    string jsonRaw = discoverResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    JObject? jsonResponse = JObject.Parse(jsonRaw);
                    if (jsonResponse != null)
                    {
                        foreach (JObject item in jsonResponse.Value<JArray>("results")!.OfType<JObject>().Where(x => !x.Value<bool>("adult")))
                        {
                            BaseItemDto? dto = TryMapDiscoverItem(item, jellyseerrDisplayUrl);
                            if (dto != null)
                            {
                                returnItems.Add(dto);
                            }
                        }
                    }
                }

                page++;
            } while (returnItems.Count < 20);

            return returnItems;
        }

        private BaseItemDto? TryMapDiscoverItem(JObject item, string jellyseerrDisplayUrl)
        {
            string? preferredLanguages = HomeScreenSectionsPlugin.Instance.Configuration.JellyseerrPreferredLanguages;
            if (!string.IsNullOrEmpty(preferredLanguages) &&
                !preferredLanguages.Split(',').Select(x => x.Trim())
                    .Contains(item.Value<string>("originalLanguage"), StringComparer.Ordinal))
            {
                return null;
            }

            if (item.Value<JObject>("mediaInfo") != null)
            {
                return null;
            }

            string dateTimeString = item.Value<string>("firstAirDate") ??
                                    item.Value<string>("releaseDate") ?? "1970-01-01";
            if (string.IsNullOrWhiteSpace(dateTimeString))
            {
                dateTimeString = "1970-01-01";
            }

            string posterPath = item.Value<string>("posterPath") ?? "404";
            string cachedImageUrl = GetCachedImageUrl($"https://image.tmdb.org/t/p/w600_and_h900_bestv2{posterPath}");
            float rating = item.Value<float?>("vote_average") ?? item.Value<float?>("voteAverage") ?? 0f;

            return new BaseItemDto()
            {
                Name = item.Value<string>("title") ?? item.Value<string>("name"),
                OriginalTitle = item.Value<string>("originalTitle") ?? item.Value<string>("originalName"),
                SourceType = item.Value<string>("mediaType"),
                CommunityRating = rating > 0 ? rating : null,
                ProviderIds = new(StringComparer.Ordinal)
                {
                    { "JellyseerrRoot", jellyseerrDisplayUrl },
                    { "Jellyseerr", item.Value<int>("id").ToString(System.Globalization.CultureInfo.InvariantCulture) },
                    { "JellyseerrPoster", cachedImageUrl }
                },
                PremiereDate = DateTime.Parse(dateTimeString, System.Globalization.CultureInfo.InvariantCulture)
            };
        }

        protected string GetCachedImageUrl(string sourceUrl)
        {
            return ImageCacheHelper.GetCachedImageUrl(_imageCacheService, sourceUrl);
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
                AllowViewModeChange = false
            };
        }
    }
}