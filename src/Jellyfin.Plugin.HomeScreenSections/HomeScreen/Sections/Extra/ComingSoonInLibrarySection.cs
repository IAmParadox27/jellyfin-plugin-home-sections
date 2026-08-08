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
/// Items already in the library with a future premiere / air date.
/// </summary>
public class ComingSoonInLibrarySection : IHomeScreenSection
{
    public string? Section => "ComingSoonInLibrary";

    public string? DisplayText { get; set; } = "Coming Soon";

    public int? Limit => 1;

    public string? Route => null;

    public string? AdditionalData { get; set; }

    public object? OriginalPayload => null;

    private readonly IUserManager _userManager;
    private readonly ILibraryManager _libraryManager;
    private readonly IDtoService _dtoService;

    public ComingSoonInLibrarySection(IUserManager userManager, ILibraryManager libraryManager, IDtoService dtoService)
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
        DateTime today = DateTime.UtcNow.Date;

        QueryResult<BaseItem> items = _libraryManager.GetItemsResult(new InternalItemsQuery(user)
        {
            IncludeItemTypes = SectionDtoHelper.MovieSeriesEpisodeKinds,
            Recursive = true,
            MinPremiereDate = today,
            MaxPremiereDate = today.AddDays(90),
            Limit = 24,
            OrderBy = [(ItemSortBy.PremiereDate, SortOrder.Ascending)],
            DtoOptions = dtoOptions,
            EnableTotalRecordCount = false
        });

        return new QueryResult<BaseItemDto>(_dtoService.GetBaseItemDtos(items.Items, dtoOptions, user));
    }

    public IEnumerable<IHomeScreenSection> CreateInstances(Guid? userId, int instanceCount)
    {
        yield return this;
    }

public HomeScreenSectionInfo GetInfo() => SectionDtoHelper.CreateInfo(this);
}
