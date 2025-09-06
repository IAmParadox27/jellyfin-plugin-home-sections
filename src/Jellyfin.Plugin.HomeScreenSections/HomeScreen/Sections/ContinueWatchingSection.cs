using Jellyfin.Data;
using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections
{
    /// <summary>
    /// Continue Watching Section.
    /// </summary>
    public class ContinueWatchingSection : IHomeScreenSection
    {
        /// <inheritdoc/>
        public string? Section => "ContinueWatching";

        /// <inheritdoc/>
        public string? DisplayText { get; set; } = "Continue Watching";

        /// <inheritdoc/>
        public int? Limit => 1;

        /// <inheritdoc/>
        public string? Route => null;

        /// <inheritdoc/>
        public string? AdditionalData { get; set; } = null;

        public object? OriginalPayload => null;
        
        private readonly IUserViewManager m_userViewManager;
        private readonly IUserManager m_userManager;
        private readonly IDtoService m_dtoService;
        private readonly ILibraryManager m_libraryManager;
        private readonly ISessionManager m_sessionManager;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="userViewManager">Instance of <see href="IUserViewManager" /> interface.</param>
        /// <param name="userManager">Instance of <see href="IUserManager" /> interface.</param>
        /// <param name="dtoService">Instance of <see href="IDtoService" /> interface.</param>
        /// <param name="libraryManager">Instance of <see href="ILibraryManager" /> interface.</param>
        /// <param name="sessionManager">Instance of <see href="ISessionManager" /> interface.</param>
        public ContinueWatchingSection(IUserViewManager userViewManager,
            IUserManager userManager,
            IDtoService dtoService,
            ILibraryManager libraryManager,
            ISessionManager sessionManager)
        {
            m_userViewManager = userViewManager;
            m_userManager = userManager;
            m_dtoService = dtoService;
            m_libraryManager = libraryManager;
            m_sessionManager = sessionManager;
        }

        /// <inheritdoc/>
        public QueryResult<BaseItemDto> GetResults(HomeScreenSectionPayload payload, IQueryCollection queryCollection)
        {
            User? user = m_userManager.GetUserById(payload.UserId);
            DtoOptions? dtoOptions = new DtoOptions
            {
                Fields = new List<ItemFields>
                {
                    ItemFields.PrimaryImageAspectRatio
                },
                ImageTypeLimit = 1,
                ImageTypes = new List<ImageType>
                {
                    ImageType.Thumb,
                    ImageType.Backdrop,
                    ImageType.Primary,
                }
            };

            Guid[]? ancestorIds = Array.Empty<Guid>();

            Guid[]? excludeFolderIds = user!.GetPreferenceValues<Guid>(PreferenceKind.LatestItemExcludes);
            if (excludeFolderIds.Length > 0)
            {
                ancestorIds = m_libraryManager.GetUserRootFolder().GetChildren(user, true)
                    .Where(i => i is Folder)
                    .Where(i => !excludeFolderIds.Contains(i.Id))
                    .Select(i => i.Id)
                    .ToArray();
            }

            QueryResult<BaseItem>? itemsResult = m_libraryManager.GetItemsResult(new InternalItemsQuery(user)
            {
                OrderBy = new[] { (ItemSortBy.DatePlayed, SortOrder.Descending) },
                IsResumable = true,
                Limit = 12,
                Recursive = true,
                DtoOptions = dtoOptions,
                MediaTypes = new MediaType[]
                {
                    MediaType.Video
                },
                IsVirtualItem = false,
                CollapseBoxSetItems = false,
                EnableTotalRecordCount = false,
                AncestorIds = ancestorIds
            });

            IReadOnlyList<BaseItemDto>? returnItems = m_dtoService.GetBaseItemDtos(itemsResult.Items, dtoOptions, user);

            return new QueryResult<BaseItemDto>(
                null,
                itemsResult.TotalRecordCount,
                returnItems);
        }

        /// <inheritdoc/>
        public IHomeScreenSection CreateInstance(Guid? userId, IEnumerable<IHomeScreenSection>? otherInstances = null)
        {
            return this;
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
                AllowViewModeChange = false
            };
        }
    }
}
