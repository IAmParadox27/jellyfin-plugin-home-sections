using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
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
        
        public string? AdditionalData { get; set; } = null;
        
        public object? OriginalPayload { get; } = null;

        public MyRequestsSection(IUserManager userManager, ILibraryManager libraryManager, IDtoService dtoService)
        {
            m_userManager = userManager;
            m_libraryManager = libraryManager;
            m_dtoService = dtoService;
        }
        
        public QueryResult<BaseItemDto> GetResults(HomeScreenSectionPayload payload, IQueryCollection queryCollection)
        {
            DtoOptions? dtoOptions = new DtoOptions 
            { 
                Fields = new[] 
                { 
                    ItemFields.PrimaryImageAspectRatio, 
                    ItemFields.MediaSourceCount
                }
            };

            string? jellyseerrUrl = HomeScreenSectionsPlugin.Instance.Configuration.JellyseerrUrl;

            if (string.IsNullOrEmpty(jellyseerrUrl))
            {
                return new QueryResult<BaseItemDto>();
            }
            
            User? user = m_userManager.GetUserById(payload.UserId);
            
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
            
            HttpResponseMessage requestsResponse = client.GetAsync($"/api/v1/user/{jellyseerrUserId}/requests?take=100").GetAwaiter().GetResult();

            if (requestsResponse.IsSuccessStatusCode)
            {
                string jsonRaw = requestsResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                JObject? jsonResponse = JObject.Parse(jsonRaw);
                IEnumerable<JObject>? presentRequestedMedia = jsonResponse.Value<JArray>("results")?.OfType<JObject>()
                    .Where(x => x.Value<JObject>("media")?.Value<string>("jellyfinMediaId") != null)
                    .Select(x => x.Value<JObject>("media")!);
                
                VirtualFolderInfo[] folders = m_libraryManager.GetVirtualFolders()
                    .ToArray();

                IEnumerable<string?>? jellyfinItemIds = presentRequestedMedia?.Select(x => x.Value<string>("jellyfinMediaId"));
                
                IEnumerable<BaseItem> items = folders.SelectMany(x =>
                {
                    return m_libraryManager.GetItemList(new InternalItemsQuery(user)
                    {
                        ItemIds = jellyfinItemIds?.Select(y => Guid.Parse(y ?? Guid.Empty.ToString()))?.ToArray() ?? Array.Empty<Guid>(),
                        Recursive = true,
                        EnableTotalRecordCount = false,
                        ParentId = Guid.Parse(x.ItemId ?? Guid.Empty.ToString()),
                        OrderBy = new[] { (ItemSortBy.DateCreated, SortOrder.Descending) }
                    });
                });
                
                return new QueryResult<BaseItemDto>(m_dtoService.GetBaseItemDtos(items.Take(16).ToArray(), dtoOptions, user));
            }
            
            return new QueryResult<BaseItemDto>();
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
                AllowViewModeChange = true // TODO: Change this to allowed view modes
            };
        }
    }
}