using System.Net.Http.Json;
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
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections
{
    public class DiscoverSection : IHomeScreenSection
    {
        private const int c_resultLimit = 20;
        private const int c_maxPages = 10;
        private const int c_timeoutSeconds = 12;
        private readonly IHttpClientFactory m_httpClientFactory;
        private readonly IHttpContextAccessor m_httpContextAccessor;
        private readonly ILogger<DiscoverSection> m_logger;
        private readonly IUserManager m_userManager;
        private readonly ImageCacheService m_imageCacheService;
        
        public virtual string? Section => "Discover";

        public virtual string? DisplayText { get; set; } = "Discover";
        public int? Limit => 1;
        public string? Route => null;
        public string? AdditionalData { get; set; }
        public object? OriginalPayload { get; } = null;

        protected virtual string JellyseerEndpoint => "/api/v1/discover/trending";
        
        public DiscoverSection(IUserManager userManager, ImageCacheService imageCacheService, IHttpClientFactory httpClientFactory, IHttpContextAccessor httpContextAccessor, ILogger<DiscoverSection> logger)
        {
            m_userManager = userManager;
            m_imageCacheService = imageCacheService;
            m_httpClientFactory = httpClientFactory;
            m_httpContextAccessor = httpContextAccessor;
            m_logger = logger;
        }
        
        public QueryResult<BaseItemDto> GetResults(HomeScreenSectionPayload payload, IQueryCollection queryCollection)
        {
            List<BaseItemDto> returnItems = new List<BaseItemDto>();
            
            // TODO: Get Jellyseerr Url
            string? jellyseerrUrl = HomeScreenSectionsPlugin.Instance.Configuration.JellyseerrUrl;
            string? jellyseerrExternalUrl = HomeScreenSectionsPlugin.Instance.Configuration.JellyseerrExternalUrl;
            
            // Use external URL for frontend links if configured, otherwise fall back to internal URL
            string jellyseerrDisplayUrl = !string.IsNullOrEmpty(jellyseerrExternalUrl) ? jellyseerrExternalUrl : jellyseerrUrl ?? string.Empty;

            if (string.IsNullOrEmpty(jellyseerrUrl))
            {
                return new QueryResult<BaseItemDto>();
            }
            
            if (!Uri.TryCreate(jellyseerrUrl, UriKind.Absolute, out Uri? baseUri) ||
                (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
            {
                m_logger.LogWarning("Seerr discovery URL must be an absolute HTTP or HTTPS URL.");
                return new QueryResult<BaseItemDto>();
            }

            User? user = m_userManager.GetUserById(payload.UserId);
            
            if (user == null)
            {
                return new QueryResult<BaseItemDto>();
            }

            using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(
                m_httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None);
            deadline.CancelAfter(TimeSpan.FromSeconds(c_timeoutSeconds));
            CancellationToken cancellationToken = deadline.Token;

            try
            {
                using HttpClient client = m_httpClientFactory.CreateClient();
                client.BaseAddress = baseUri;
                client.DefaultRequestHeaders.Add("X-Api-Key", HomeScreenSectionsPlugin.Instance.Configuration.JellyseerrApiKey);

                JObject? users = GetResponse(client, $"/api/v1/user?q={Uri.EscapeDataString(user.Username)}", cancellationToken);
                if (users?["results"] is not JArray usersResults)
                {
                    return new QueryResult<BaseItemDto>();
                }

                JObject? seerrUser = usersResults.OfType<JObject>()
                    .FirstOrDefault(x => x["jellyfinUsername"]?.Type == JTokenType.String && x.Value<string>("jellyfinUsername") == user.Username);
                if (!int.TryParse(seerrUser?["id"]?.ToString(), out int jellyseerrUserId) || jellyseerrUserId <= 0)
                {
                    return new QueryResult<BaseItemDto>();
                }

                client.DefaultRequestHeaders.Add("X-Api-User", jellyseerrUserId.ToString());

                for (int page = 1; page <= c_maxPages && returnItems.Count < c_resultLimit; page++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    JObject? jsonResponse = GetResponse(client, $"{JellyseerEndpoint}?page={page}", cancellationToken);
                    if (jsonResponse?["results"] is JArray results && results.Count > 0)
                    {
                        foreach (JObject item in results.OfType<JObject>().Where(x => !x.Value<bool>("adult")))
                        {
                            cancellationToken.ThrowIfCancellationRequested();
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
                                string cachedImageUrl = GetCachedImageUrl($"https://image.tmdb.org/t/p/w600_and_h900_bestv2{posterPath}", cancellationToken);
                                float rating = item.Value<float?>("vote_average") ?? item.Value<float?>("voteAverage") ?? 0f;

                                returnItems.Add(new BaseItemDto()
                                {
                                    Name = item.Value<string>("title") ?? item.Value<string>("name"),
                                    OriginalTitle = item.Value<string>("originalTitle") ?? item.Value<string>("originalName"),
                                    SourceType = item.Value<string>("mediaType"),
                                    CommunityRating = rating > 0 ? rating : null,
                                    ProviderIds = new Dictionary<string, string>()
                                    {
                                        { "JellyseerrRoot", jellyseerrDisplayUrl },
                                        { "Jellyseerr", item.Value<int>("id").ToString() },
                                        { "JellyseerrPoster", cachedImageUrl }
                                    },
                                    PremiereDate = DateTime.Parse(dateTimeString)
                                });
                            }
                            if (returnItems.Count == c_resultLimit)
                            {
                                break;
                            }
                        }
                    }
                    else
                    {
                        break;
                    }

                    if (int.TryParse(jsonResponse["totalPages"]?.ToString(), out int totalPages) && page >= totalPages)
                    {
                        break;
                    }
                }
            }
            catch (HttpRequestException)
            {
                m_logger.LogWarning("Seerr discovery request failed; returning the items collected so far.");
            }
            catch (OperationCanceledException)
            {
                m_logger.LogWarning("Seerr discovery was cancelled or timed out; returning the items collected so far.");
            }
            catch (JsonReaderException)
            {
                m_logger.LogWarning("Seerr returned invalid discovery JSON; returning the items collected so far.");
            }

            return new QueryResult<BaseItemDto>()
            {
                Items = returnItems,
                StartIndex = 0,
                TotalRecordCount = returnItems.Count
            };
        }

        private JObject? GetResponse(HttpClient client, string endpoint, CancellationToken cancellationToken)
        {
            using HttpResponseMessage response = client.GetAsync(endpoint, cancellationToken).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                m_logger.LogWarning("Seerr discovery returned HTTP {StatusCode}.", (int)response.StatusCode);
                return null;
            }

            string content = response.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            if (JToken.Parse(content) is not JObject result || result["results"] is not JArray)
            {
                m_logger.LogWarning("Seerr discovery returned an invalid results object.");
                return null;
            }

            return result;
        }

        protected string GetCachedImageUrl(string sourceUrl)
        {
            return ImageCacheHelper.GetCachedImageUrl(m_imageCacheService, sourceUrl);
        }

        protected string GetCachedImageUrl(string sourceUrl, CancellationToken cancellationToken)
        {
            return ImageCacheHelper.GetCachedImageUrl(m_imageCacheService, sourceUrl, cancellationToken: cancellationToken);
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