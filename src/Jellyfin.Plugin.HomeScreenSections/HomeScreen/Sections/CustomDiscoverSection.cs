using System.Net.Http.Json;
using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Helpers;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using Jellyfin.Plugin.HomeScreenSections.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections
{
    /// <summary>
    /// A discover section that allows custom query parameters for the Jellyseerr API.
    /// Example: /api/v1/discover/movies?page=1&genre=27&language=en
    /// </summary>
    public class CustomDiscoverSection : IHomeScreenSection
    {
        private readonly IUserManager _userManager;
        private readonly ImageCacheService _imageCacheService;

        public virtual string? Section { get; set; } = "CustomDiscover";

        public virtual string? DisplayText { get; set; } = "Custom List";

        public int? Limit => 1;
        public string? Route => null;
        public string? AdditionalData { get; set; }
        public object? OriginalPayload { get; } = null;

        /// <summary>
        /// The endpoint to use for discovery. Can be /api/v1/discover/movies or /api/v1/discover/tv
        /// </summary>
        public string CustomEndpoint { get; set; } = "/api/v1/discover/movies";

        /// <summary>
        /// Custom query parameters to append to the Jellyseerr API request.
        /// Example: "genre=27&language=en" for horror movies in English
        /// </summary>
        public string CustomQueryParameters { get; set; } = "";

        public CustomDiscoverSection(
            IUserManager userManager, 
            ImageCacheService imageCacheService,
            string? displayText = null,
            string? endpoint = null,
            string? queryParameters = null)
        {
            _userManager = userManager;
            _imageCacheService = imageCacheService;
            
            if (!string.IsNullOrEmpty(displayText))
            {
                DisplayText = displayText;
            }
            
            if (!string.IsNullOrEmpty(endpoint))
            {
                CustomEndpoint = endpoint;
            }
            
            if (!string.IsNullOrEmpty(queryParameters))
            {
                CustomQueryParameters = queryParameters;
            }
        }

        public QueryResult<BaseItemDto> GetResults(HomeScreenSectionPayload payload, IQueryCollection queryCollection)
        {
            List<BaseItemDto> returnItems = new List<BaseItemDto>();
            
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
            
            HttpClient client = new HttpClient();
            client.BaseAddress = new Uri(jellyseerrUrl);
            client.DefaultRequestHeaders.Add("X-Api-Key", HomeScreenSectionsPlugin.Instance.Configuration.JellyseerrApiKey);
            
            HttpResponseMessage usersResponse = client.GetAsync($"/api/v1/user?q={user.Username}").GetAwaiter().GetResult();
            string userResponseRaw = usersResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            int? jellyseerrUserId = JObject.Parse(userResponseRaw).Value<JArray>("results")!.OfType<JObject>().FirstOrDefault(x => x.Value<string>("jellyfinUsername") == user.Username)?.Value<int>("id");

            if (jellyseerrUserId == null)
            {
                return new QueryResult<BaseItemDto>();
            }
            
            client.DefaultRequestHeaders.Add("X-Api-User", jellyseerrUserId.ToString());

            // Build the custom query string
            string customParams = string.IsNullOrEmpty(CustomQueryParameters) ? "" : $"&{CustomQueryParameters}";

            // Make the API call to discover with custom parameters
            int page = 1;
            do 
            {
                HttpResponseMessage discoverResponse = client.GetAsync($"{CustomEndpoint}?page={page}{customParams}").GetAwaiter().GetResult();

                if (discoverResponse.IsSuccessStatusCode)
                {
                    string jsonRaw = discoverResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    JObject? jsonResponse = JObject.Parse(jsonRaw);

                    if (jsonResponse != null)
                    {
                        foreach (JObject item in jsonResponse.Value<JArray>("results")!.OfType<JObject>().Where(x => !x.Value<bool>("adult")))
                        {
                            if (!string.IsNullOrEmpty(HomeScreenSectionsPlugin.Instance.Configuration.JellyseerrPreferredLanguages) && 
                                !HomeScreenSectionsPlugin.Instance.Configuration.JellyseerrPreferredLanguages.Split(',')
                                    .Select(x => x.Trim()).Contains(item.Value<string>("originalLanguage")))
                            {
                                continue;
                            }
                            
                            if (item.Value<JObject>("mediaInfo") == null)
                            {
                                string dateTimeString = item.Value<string>("firstAirDate") ??
                                                        item.Value<string>("releaseDate") ?? "1970-01-01";
                                
                                if (string.IsNullOrWhiteSpace(dateTimeString))
                                {
                                    dateTimeString = "1970-01-01";
                                }
                                
                                string posterPath = item.Value<string>("posterPath") ?? "404";
                                string cachedImageUrl = GetCachedImageUrl($"https://image.tmdb.org/t/p/w600_and_h900_bestv2{posterPath}");
                                
                                returnItems.Add(new BaseItemDto()
                                {
                                    Name = item.Value<string>("title") ?? item.Value<string>("name"),
                                    OriginalTitle = item.Value<string>("originalTitle") ?? item.Value<string>("originalName"),
                                    SourceType = item.Value<string>("mediaType"),
                                    ProviderIds = new Dictionary<string, string>()
                                    {
                                        { "JellyseerrRoot", jellyseerrDisplayUrl ?? jellyseerrUrl },
                                        { "Jellyseerr", item.Value<int>("id").ToString() },
                                        { "JellyseerrPoster", cachedImageUrl }
                                    },
                                    PremiereDate = DateTime.Parse(dateTimeString)
                                });
                            }
                        }
                    }
                }

                page++;
            } while (returnItems.Count < 20);
            
            return new QueryResult<BaseItemDto>()
            {
                Items = returnItems,
                StartIndex = 0,
                TotalRecordCount = returnItems.Count
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
