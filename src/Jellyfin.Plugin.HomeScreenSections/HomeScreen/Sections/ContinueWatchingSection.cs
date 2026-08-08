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
        /// <remarks>
        /// Named route for resumable content list; client only renders a link when the route resolves.
        /// </remarks>
        public string? Route => "list";

        /// <inheritdoc/>
        public string? AdditionalData { get; set; }

        public object? OriginalPayload => null;
        
        private readonly IUserViewManager _userViewManager;
        private readonly IUserManager _userManager;
        private readonly IDtoService _dtoService;
        private readonly ILibraryManager _libraryManager;
        private readonly ISessionManager _sessionManager;

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
            _userViewManager = userViewManager;
            _userManager = userManager;
            _dtoService = dtoService;
            _libraryManager = libraryManager;
            _sessionManager = sessionManager;
        }

        /// <inheritdoc/>
        public QueryResult<BaseItemDto> GetResults(HomeScreenSectionPayload payload, IQueryCollection queryCollection)
        {
            User? user = _userManager.GetUserById(payload.UserId);
            DtoOptions? dtoOptions = new DtoOptions
            {
                Fields = [ItemFields.PrimaryImageAspectRatio],
                ImageTypeLimit = 1,
                ImageTypes = [ImageType.Thumb,
                    ImageType.Backdrop,
                    ImageType.Primary,]
            };

            Guid[]? ancestorIds = [];

            Guid[]? excludeFolderIds = user!.GetPreferenceValues<Guid>(PreferenceKind.LatestItemExcludes);
            if (excludeFolderIds.Length > 0)
            {
                ancestorIds = _libraryManager.GetUserRootFolder().GetChildren(user, true)
                    .Where(i => i is Folder)
                    .Where(i => !excludeFolderIds.Contains(i.Id))
                    .Select(i => i.Id)
                    .ToArray();
            }

            QueryResult<BaseItem>? itemsResult = _libraryManager.GetItemsResult(new InternalItemsQuery(user)
            {
                OrderBy = [(ItemSortBy.DatePlayed, SortOrder.Descending)],
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

            IReadOnlyList<BaseItemDto>? returnItems = _dtoService.GetBaseItemDtos(itemsResult.Items, dtoOptions, user);

            return new QueryResult<BaseItemDto>(
                null,
                itemsResult.TotalRecordCount,
                returnItems);
        }

        /// <inheritdoc/>
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
                AllowViewModeChange = false
            };
        }
    }
}
