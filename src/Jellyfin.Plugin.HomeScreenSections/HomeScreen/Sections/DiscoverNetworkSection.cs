using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections
{
    public class DiscoverNetworkSection : IHomeScreenSection
    {
        private readonly IUserManager m_userManager;

        public string? Section => "DiscoverNetwork";
        public string? DisplayText { get; set; } = "Discover Network";
        public int? Limit
        {
            get
            {
                var config = HomeScreenSectionsPlugin.Instance?.Configuration;
                return config?.JellyseerrNetworks?.Count(n => n.Enabled) ?? 0;
            }
        }
        public string? Route => null;
        public string? AdditionalData { get; set; }
        public object? OriginalPayload => null;

        public DiscoverNetworkSection(IUserManager userManager)
        {
            m_userManager = userManager;
        }

        public QueryResult<BaseItemDto> GetResults(HomeScreenSectionPayload payload, IQueryCollection queryCollection)
        {
            List<BaseItemDto> returnItems = new List<BaseItemDto>();

            if (payload.AdditionalData == null || !int.TryParse(payload.AdditionalData, out int networkId))
            {
                return new QueryResult<BaseItemDto>();
            }

            string? jellyseerrUrl = HomeScreenSectionsPlugin.Instance.Configuration.JellyseerrUrl;

            if (string.IsNullOrEmpty(jellyseerrUrl))
            {
                return new QueryResult<BaseItemDto>();
            }

            User? user = m_userManager.GetUserById(payload.UserId);

            HttpClient client = new HttpClient();
            client.BaseAddress = new Uri(jellyseerrUrl);
            client.DefaultRequestHeaders.Add("X-Api-Key", HomeScreenSectionsPlugin.Instance.Configuration.JellyseerrApiKey);

            // Get Jellyseerr user ID
            HttpResponseMessage usersResponse = client.GetAsync($"/api/v1/user?q={user.Username}").GetAwaiter().GetResult();
            string userResponseRaw = usersResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            int? jellyseerrUserId = JObject.Parse(userResponseRaw).Value<JArray>("results")!.OfType<JObject>()
                .FirstOrDefault(x => x.Value<string>("jellyfinUsername") == user.Username)?.Value<int>("id");

            if (jellyseerrUserId == null)
            {
                return new QueryResult<BaseItemDto>();
            }

            client.DefaultRequestHeaders.Add("X-Api-User", jellyseerrUserId.ToString());

            // Make the API call to discover network TV shows
            int page = 1;
            do
            {
                HttpResponseMessage discoverResponse = client.GetAsync($"/api/v1/discover/tv/network/{networkId}?page={page}").GetAwaiter().GetResult();

                if (discoverResponse.IsSuccessStatusCode)
                {
                    string jsonRaw = discoverResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    JObject? jsonResponse = JObject.Parse(jsonRaw);

                    if (jsonResponse != null)
                    {
                        var results = jsonResponse.Value<JArray>("results");

                        // If no results on this page, stop fetching more pages
                        if (results == null || results.Count == 0)
                        {
                            break;
                        }

                        foreach (JObject item in results.OfType<JObject>())
                        {
                            string firstAirDateStr = item.Value<string>("firstAirDate");
                            DateTime? premiereDate = null;
                            if (!string.IsNullOrWhiteSpace(firstAirDateStr))
                            {
                                DateTime.TryParse(firstAirDateStr, out DateTime parsedDate);
                                premiereDate = parsedDate == default ? null : parsedDate;
                            }

                            returnItems.Add(new BaseItemDto()
                            {
                                Name = item.Value<string>("name"),
                                OriginalTitle = item.Value<string>("originalName"),
                                SourceType = item.Value<string>("mediaType"),
                                ProviderIds = new Dictionary<string, string>()
                                {
                                    { "JellyseerrRoot", jellyseerrUrl },
                                    { "Jellyseerr", item.Value<int>("id").ToString() },
                                    { "JellyseerrPoster", item.Value<string>("posterPath") ?? "404" }
                                },
                                PremiereDate = premiereDate
                            });
                        }
                    }
                }

                page++;
            } while (returnItems.Count < 20 && page <= 3);

            return new QueryResult<BaseItemDto>()
            {
                Items = returnItems,
                StartIndex = 0,
                TotalRecordCount = returnItems.Count
            };
        }

        public IHomeScreenSection CreateInstance(Guid? userId, IEnumerable<IHomeScreenSection>? otherInstances = null)
        {
            var config = HomeScreenSectionsPlugin.Instance?.Configuration;
            if (config?.JellyseerrNetworks == null)
            {
                return this;
            }

            // Get networks that haven't been used yet
            var usedNetworkIds = otherInstances?
                .Where(x => x is DiscoverNetworkSection)
                .Select(x => x.AdditionalData)
                .ToHashSet() ?? new HashSet<string>();

            var availableNetwork = config.JellyseerrNetworks
                .Where(n => n.Enabled && !usedNetworkIds.Contains(n.Id.ToString()))
                .FirstOrDefault();

            if (availableNetwork == null)
            {
                return this;
            }

            return new DiscoverNetworkSection(m_userManager)
            {
                AdditionalData = availableNetwork.Id.ToString(),
                DisplayText = $"Discover {availableNetwork.Name}"
            };
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
