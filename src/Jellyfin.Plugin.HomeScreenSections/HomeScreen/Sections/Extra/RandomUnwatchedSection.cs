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
/// Random unwatched movies and series ("Surprise me").
/// </summary>
public class RandomUnwatchedSection : IHomeScreenSection
{
    public string? Section => "RandomUnwatched";

    public string? DisplayText { get; set; } = "Surprise Me";

    public int? Limit => 1;

    public string? Route => null;

    public string? AdditionalData { get; set; }

    public object? OriginalPayload => null;

    private readonly IUserManager _userManager;
    private readonly ILibraryManager _libraryManager;
    private readonly IDtoService _dtoService;

    public RandomUnwatchedSection(IUserManager userManager, ILibraryManager libraryManager, IDtoService dtoService)
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
            IsPlayed = false,
            Limit = 16,
            OrderBy = [(ItemSortBy.Random, SortOrder.Ascending)],
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

public HomeScreenSectionInfo GetInfo() => SectionDtoHelper.CreateInfo(this, allowHideWatched: false);
}
