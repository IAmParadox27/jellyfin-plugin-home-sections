using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Library;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections
{
    /// <summary>
    /// My Media Section.
    /// </summary>
    public class MyMediaSection : IHomeScreenSection
    {
        /// <inheritdoc/>
        public string Section => "MyMedia";

        /// <inheritdoc/>
        public string? DisplayText { get; set; } = "My Media";

        /// <inheritdoc/>
        public int? Limit => 1;

        /// <inheritdoc/>
        public string? Route => null;

        /// <inheritdoc/>
        public string? AdditionalData { get; set; }

        public object? OriginalPayload => null;
        
        private readonly IUserViewManager _userViewManager;
        private readonly IUserManager _userManager;
        private readonly IDtoService _dtoService;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="userViewManager">Instance of <see href="IUserViewManager" /> interface.</param>
        /// <param name="userManager">Instance of <see href="IUserManager" /> interface.</param>
        /// <param name="dtoService">Instance of <see href="IDtoService" /> interface.</param>
        public MyMediaSection(IUserViewManager userViewManager,
            IUserManager userManager,
            IDtoService dtoService)
        {
            _userViewManager = userViewManager;
            _userManager = userManager;
            _dtoService = dtoService;
        }

        /// <inheritdoc/>
        public QueryResult<BaseItemDto> GetResults(HomeScreenSectionPayload payload, IQueryCollection queryCollection)
        {
            User? user = _userManager.GetUserById(payload.UserId);

            if (user == null)
            {
                return new QueryResult<BaseItemDto>();
            }
            
            UserViewQuery query = new UserViewQuery
            {
                User = user,
                IncludeHidden = false
            };

            Folder[]? folders = _userViewManager.GetUserViews(query);

            DtoOptions dtoOptions = new DtoOptions();
            List<ItemFields> f = [ItemFields.PrimaryImageAspectRatio,
                ItemFields.DisplayPreferencesId];

            dtoOptions.Fields = f.ToArray();

            BaseItemDto[] dtos = folders.Select(i => _dtoService.GetBaseItemDto(i, dtoOptions, user))
                .ToArray();

            return new QueryResult<BaseItemDto>(dtos);
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
                AllowViewModeChange = true // NOTE: Change this to allowed view modes
            };
        }
    }
}
