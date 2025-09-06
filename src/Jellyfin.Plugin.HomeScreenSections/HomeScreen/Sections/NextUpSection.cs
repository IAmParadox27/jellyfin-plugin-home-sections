using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Controller.TV;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections
{
    /// <summary>
    /// Next Up Section.
    /// </summary>
    public class NextUpSection : IHomeScreenSection
    {
        /// <inheritdoc/>
        public string Section => "NextUp";

        /// <inheritdoc/>
        public string? DisplayText { get; set; } = "Next Up";

        /// <inheritdoc/>
        public int? Limit => 1;

        /// <inheritdoc/>
        public string? Route => "nextup";

        /// <inheritdoc/>
        public string? AdditionalData { get; set; } = null;

        public object? OriginalPayload => null;
        
        private readonly IUserViewManager m_userViewManager;
        private readonly IUserManager m_userManager;
        private readonly IDtoService m_dtoService;
        private readonly ILibraryManager m_libraryManager;
        private readonly ISessionManager m_sessionManager;
        private readonly ITVSeriesManager m_tvSeriesManager;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="userViewManager">Instance of <see href="IUserViewManager" /> interface.</param>
        /// <param name="userManager">Instance of <see href="IUserManager" /> interface.</param>
        /// <param name="dtoService">Instance of <see href="IDtoService" /> interface.</param>
        /// <param name="libraryManager">Instance of <see href="ILibraryManager" /> interface.</param>
        /// <param name="sessionManager">Instance of <see href="ISessionManager" /> interface.</param>
        /// <param name="tvSeriesManager">Instance of <see href="ITVSeriesManager" /> interface.</param>
        public NextUpSection(IUserViewManager userViewManager,
            IUserManager userManager,
            IDtoService dtoService,
            ILibraryManager libraryManager,
            ISessionManager sessionManager,
            ITVSeriesManager tvSeriesManager)
        {
            m_userViewManager = userViewManager;
            m_userManager = userManager;
            m_dtoService = dtoService;
            m_libraryManager = libraryManager;
            m_sessionManager = sessionManager;
            m_tvSeriesManager = tvSeriesManager;
        }

        /// <inheritdoc/>
        public QueryResult<BaseItemDto> GetResults(HomeScreenSectionPayload payload, IQueryCollection queryCollection)
        {
            User? user = m_userManager.GetUserById(payload.UserId);
            
            List<ItemFields> fields = new List<ItemFields>
            {
                ItemFields.PrimaryImageAspectRatio,
                ItemFields.DateCreated,
                ItemFields.Path,
                ItemFields.MediaSourceCount
            };

            DtoOptions options = new DtoOptions { Fields = fields };
            options.ImageTypeLimit = 1;
            options.ImageTypes = new List<ImageType>
            {
                ImageType.Thumb,
                ImageType.Backdrop,
                ImageType.Primary,
            };

            bool enableRewatching = true; // Enabled by default
            if (queryCollection.TryGetValue("EnableRewatching", out StringValues enableRewatchingValue))
            {
                enableRewatching = enableRewatchingValue.FirstOrDefault() == "true";
            }
            
            DateTime nextUpDateCutoff = DateTime.MinValue;
            if (queryCollection.TryGetValue("NextUpDateCutoff", out StringValues nextUpDateCutoffValue))
            {
                if (DateTime.TryParse(nextUpDateCutoffValue.FirstOrDefault(), out DateTime nextUpDateCutoffParsed))
                {
                    nextUpDateCutoff = nextUpDateCutoffParsed;
                }
            }

            QueryResult<BaseItem> result = m_tvSeriesManager.GetNextUp(
                new NextUpQuery
                {
                    Limit = 24,
                    SeriesId = null,
                    StartIndex = null,
                    User = user!,
                    EnableTotalRecordCount = false,
                    //DisableFirstEpisode = true,
                    NextUpDateCutoff = nextUpDateCutoff,
                    EnableRewatching = enableRewatching
                },
                options);

            IReadOnlyList<BaseItemDto> returnItems = m_dtoService.GetBaseItemDtos(result.Items, options, user);

            return new QueryResult<BaseItemDto>(
                null,
                result.TotalRecordCount,
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
                ViewMode = SectionViewMode.Landscape
            };
        }
    }
}
