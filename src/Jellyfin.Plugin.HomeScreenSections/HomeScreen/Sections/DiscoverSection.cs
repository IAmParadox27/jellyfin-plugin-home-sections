using System.Net.Http.Json;
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
    public class DiscoverSection : IHomeScreenSection
    {
        private readonly IUserManager m_userManager;
        
        public virtual string? Section => "Discover";

        public virtual string? DisplayText { get; set; } = "Discover";
        public int? Limit => 1;
        public string? Route => null;
        public string? AdditionalData { get; set; }
        public object? OriginalPayload { get; } = null;

        protected virtual string JellyseerEndpoint => "/api/v1/discover/trending";
        
        public DiscoverSection(IUserManager userManager)
        {
            m_userManager = userManager;
        }
        
        public QueryResult<BaseItemDto> GetResults(HomeScreenSectionPayload payload, IQueryCollection queryCollection)
        {
            List<BaseItemDto> returnItems = new List<BaseItemDto>();
            
            // TODO: Get Jellyseerr Url
            string? jellyseerrUrl = HomeScreenSectionsPlugin.Instance.Configuration.JellyseerrUrl;

            if (string.IsNullOrEmpty(jellyseerrUrl))
            {
                return new QueryResult<BaseItemDto>();
            }
            
            User? user = m_userManager.GetUserById(payload.UserId);
            
            HttpClient client = new HttpClient();
            client.BaseAddress = new Uri(jellyseerrUrl);
            client.DefaultRequestHeaders.Add("X-Api-Key", HomeScreenSectionsPlugin.Instance.Configuration.JellyseerrApiKey);
            
            HttpResponseMessage usersResponse = client.GetAsync("/api/v1/user").GetAwaiter().GetResult();
            string userResponseRaw = usersResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            int jellyseerrUserId = JObject.Parse(userResponseRaw).Value<JArray>("results").OfType<JObject>().FirstOrDefault(x => x.Value<string>("jellyfinUsername") == user.Username).Value<int>("id");
            
            client.DefaultRequestHeaders.Add("X-Api-User", jellyseerrUserId.ToString());

            // Make the API call to discover and get the 20 results
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
                            if (!string.IsNullOrEmpty(HomeScreenSectionsPlugin.Instance.Configuration.JellyseerrPreferredLanguages) && 
                                !HomeScreenSectionsPlugin.Instance.Configuration.JellyseerrPreferredLanguages.Split(',')
                                    .Select(x => x.Trim()).Contains(item.Value<string>("originalLanguage")))
                            {
                                continue;
                            }
                            
                            if (item.Value<JObject>("mediaInfo") == null)
                            {
                                returnItems.Add(new BaseItemDto()
                                {
                                    Name = item.Value<string>("title") ?? item.Value<string>("name"),
                                    OriginalTitle = item.Value<string>("originalTitle") ?? item.Value<string>("originalName"),
                                    SourceType = item.Value<string>("mediaType"),
                                    ProviderIds = new Dictionary<string, string>()
                                    {
                                        { "JellyseerrRoot", jellyseerrUrl },
                                        { "Jellyseerr", item.Value<int>("id").ToString() },
                                        { "JellyseerrPoster", item.Value<string>("posterPath") ?? "404" }
                                    },
                                    PremiereDate = DateTime.Parse(item.Value<string>("firstAirDate") ?? item.Value<string>("releaseDate") ?? "1970-01-01")
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

        public IHomeScreenSection CreateInstance(Guid? userId, IEnumerable<IHomeScreenSection>? otherInstances = null)
        {
            return this;
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