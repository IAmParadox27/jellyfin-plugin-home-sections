using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Helpers;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using Jellyfin.Plugin.HomeScreenSections.JellyfinVersionSpecific;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections
{
    public class MyRequestsSection : IHomeScreenSection
    {
        private readonly IUserManager m_userManager;
        private readonly ILibraryManager m_libraryManager;
        private readonly IDtoService m_dtoService;

        public string? Section => "MyJellyseerrRequests";
        
        public string? DisplayText { get; set; } = "My Requests";
        
        public int? Limit => 1;
        
        public string? Route => null;
        
        public string? AdditionalData { get; set; }
        
        public object? OriginalPayload { get; }

        public MyRequestsSection(IUserManager userManager, ILibraryManager libraryManager, IDtoService dtoService)
        {
            m_userManager = userManager;
            m_libraryManager = libraryManager;
            m_dtoService = dtoService;
        }
        
        public QueryResult<BaseItemDto> GetResults(HomeScreenSectionPayload payload, IQueryCollection queryCollection)
        {
            DtoOptions dtoOptions = CreateDtoOptions();

            string? jellyseerrUrl = HomeScreenSectionsPlugin.Instance.Configuration.JellyseerrUrl;
            if (string.IsNullOrEmpty(jellyseerrUrl))
            {
                return new QueryResult<BaseItemDto>();
            }
            
            User? user = m_userManager.GetUserById(payload.UserId);
            if (user == null)
            {
                return new QueryResult<BaseItemDto>();
            }
            
            using HttpClient client = CreateJellyseerrClient(jellyseerrUrl);
            int? jellyseerrUserId = ResolveJellyseerrUserId(client, user.Username);
            if (jellyseerrUserId == null)
            {
                return new QueryResult<BaseItemDto>();
            }

            return FetchRequestedItems(client, jellyseerrUserId.Value, user, dtoOptions);
        }

        public IEnumerable<IHomeScreenSection> CreateInstances(Guid? userId, int instanceCount)
        {
            yield return this;
        }

        public HomeScreenSectionInfo GetInfo()
        {
            return new HomeScreenSectionInfo
            {
                Section = Section,
                DisplayText = DisplayText,
                AdditionalData = AdditionalData,
                Route = Route,
                Limit = Limit ?? 1,
                OriginalPayload = OriginalPayload,
                ViewMode = SectionViewMode.Landscape,
                AllowViewModeChange = true, // NOTE: Change this to allowed view modes
                AllowHideWatched = true
            };
        }

        private static DtoOptions CreateDtoOptions()
        {
            return new DtoOptions
            {
                Fields = new[]
                {
                    ItemFields.PrimaryImageAspectRatio,
                    ItemFields.MediaSourceCount
                }
            };
        }

        private static HttpClient CreateJellyseerrClient(string jellyseerrUrl)
        {
            HttpClient client = new HttpClient();
            client.BaseAddress = new Uri(jellyseerrUrl);
            client.DefaultRequestHeaders.Add("X-Api-Key", HomeScreenSectionsPlugin.Instance.Configuration.JellyseerrApiKey);
            return client;
        }

        private static int? ResolveJellyseerrUserId(HttpClient client, string username)
        {
            HttpResponseMessage usersResponse = client.GetAsync($"/api/v1/user?q={Uri.EscapeDataString(username)}").GetAwaiter().GetResult();
            string userResponseRaw = usersResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return JObject.Parse(userResponseRaw).Value<JArray>("results")?.OfType<JObject>()
                .FirstOrDefault(x => string.Equals(x.Value<string>("jellyfinUsername"), username, StringComparison.Ordinal))
                ?.Value<int>("id");
        }

        private QueryResult<BaseItemDto> FetchRequestedItems(HttpClient client, int jellyseerrUserId, User user, DtoOptions dtoOptions)
        {
            HttpResponseMessage requestsResponse = client.GetAsync($"/api/v1/user/{jellyseerrUserId}/requests?take=100").GetAwaiter().GetResult();

            if (!requestsResponse.IsSuccessStatusCode)
            {
                return new QueryResult<BaseItemDto>();
            }

            string jsonRaw = requestsResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            JObject? jsonResponse = JObject.Parse(jsonRaw);
            IEnumerable<JObject>? presentRequestedMedia = jsonResponse.Value<JArray>("results")?.OfType<JObject>()
                .Where(x => x.Value<JObject>("media")?.Value<string>("jellyfinMediaId") != null)
                .Select(x => x.Value<JObject>("media")!);

            Guid[] requestedItemIds = ResolveRequestedItemIds(presentRequestedMedia);
            if (requestedItemIds.Length == 0)
            {
                return new QueryResult<BaseItemDto>();
            }

            IEnumerable<BaseItem> items = LoadRequestedLibraryItems(user, requestedItemIds);
            return new QueryResult<BaseItemDto>(m_dtoService.GetBaseItemDtos(items.Take(16).ToArray(), dtoOptions, user));
        }

        private static Guid[] ResolveRequestedItemIds(IEnumerable<JObject>? presentRequestedMedia)
        {
            IEnumerable<string?>? jellyfinItemIds = presentRequestedMedia?.Select(x => x.Value<string>("jellyfinMediaId"));

            // Only show items this user actually requested. Without this guard, a user with no
            // requests produces an empty ItemIds array, which Jellyfin's InternalItemsQuery treats
            // as "no filter" and returns the entire (recently-added) library for that ParentId.
            return jellyfinItemIds?
                .Where(y => !string.IsNullOrEmpty(y))
                .Select(y => Guid.Parse(y!))
                .ToArray() ?? Array.Empty<Guid>();
        }

        private IEnumerable<BaseItem> LoadRequestedLibraryItems(User user, Guid[] requestedItemIds)
        {
            VirtualFolderInfo[] folders = m_libraryManager.GetVirtualFolders()
                .FilterToUserPermitted(m_libraryManager, user);

            var config = HomeScreenSectionsPlugin.Instance?.Configuration;
            var sectionSettings = config?.SectionSettings.FirstOrDefault(x => string.Equals(x.SectionId, Section, StringComparison.Ordinal));
            bool hideWatchedItems = sectionSettings?.HideWatchedItems == true;

            IEnumerable<BaseItem> items = folders.SelectMany(x =>
            {
                return m_libraryManager.GetItemList(new InternalItemsQuery(user)
                {
                    ItemIds = requestedItemIds,
                    Recursive = true,
                    EnableTotalRecordCount = false,
                    ParentId = Guid.Parse(x.ItemId ?? Guid.Empty.ToString())
                });
            }).OrderByDescending(item => item.DateCreated);

            // Filter watched items after query since IsPlayed parameter doesn't work with specific ItemIds for TV shows
            if (hideWatchedItems)
            {
                items = items.Where(item => !item.IsPlayedVersionSpecific(user));
            }

            return items;
        }
    }
}
