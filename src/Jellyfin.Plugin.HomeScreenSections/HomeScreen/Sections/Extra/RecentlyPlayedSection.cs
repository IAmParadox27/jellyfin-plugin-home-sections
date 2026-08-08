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

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections.Extra;

/// <summary>
/// Recently finished / fully played items (not in-progress continue watching).
/// </summary>
public class RecentlyPlayedSection : IHomeScreenSection
{
    public string? Section => "RecentlyPlayed";

    public string? DisplayText { get; set; } = "Recently Played";

    public int? Limit => 1;

    public string? Route => null;

    public string? AdditionalData { get; set; }

    public object? OriginalPayload => null;

    private readonly IUserManager _userManager;
    private readonly ILibraryManager _libraryManager;
    private readonly IDtoService _dtoService;

    public RecentlyPlayedSection(IUserManager userManager, ILibraryManager libraryManager, IDtoService dtoService)
    {
        _userManager = userManager;
        _libraryManager = libraryManager;
        _dtoService = dtoService;
    }

    public QueryResult<BaseItemDto> GetResults(HomeScreenSectionPayload payload, IQueryCollection queryCollection)
    {
        User? user = _userManager.GetUserById(payload.UserId);
        if (user == null)
        {
            return new QueryResult<BaseItemDto>();
        }

        DtoOptions dtoOptions = SectionDtoHelper.CreateDefaultDtoOptions();
        QueryResult<BaseItem> items = _libraryManager.GetItemsResult(new InternalItemsQuery(user)
        {
            IncludeItemTypes = SectionDtoHelper.MovieAndSeriesKinds,
            Recursive = true,
            IsPlayed = true,
            IsResumable = false,
            Limit = 16,
            OrderBy = [(ItemSortBy.DatePlayed, SortOrder.Descending)],
            DtoOptions = dtoOptions,
            EnableTotalRecordCount = false,
            IsVirtualItem = false
        });

        return new QueryResult<BaseItemDto>(_dtoService.GetBaseItemDtos(items.Items, dtoOptions, user));
    }

    public IEnumerable<IHomeScreenSection> CreateInstances(Guid? userId, int instanceCount)
    {
        yield return this;
    }

public HomeScreenSectionInfo GetInfo() => SectionDtoHelper.CreateInfo(this);
}
