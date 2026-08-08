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
/// Movies from a weighted/random decade present in the library (multi-instance).
/// </summary>
public class DecadeSection : IHomeScreenSection
{
    public string? Section => "Decade";

    public string? DisplayText { get; set; } = "Decade";

    public int? Limit => 3;

    public string? Route => null;

    public string? AdditionalData { get; set; }

    public object? OriginalPayload { get; set; }

    private readonly IUserManager _userManager;
    private readonly ILibraryManager _libraryManager;
    private readonly IDtoService _dtoService;

    public DecadeSection(IUserManager userManager, ILibraryManager libraryManager, IDtoService dtoService)
    {
        _userManager = userManager;
        _libraryManager = libraryManager;
        _dtoService = dtoService;
    }

    public QueryResult<BaseItemDto> GetResults(HomeScreenSectionPayload payload, IQueryCollection queryCollection)
    {
        if (string.IsNullOrWhiteSpace(payload.AdditionalData)
            || !int.TryParse(payload.AdditionalData, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int decadeStart))
        {
            return new QueryResult<BaseItemDto>();
        }

        User? user = _userManager.GetUserById(payload.UserId);
        if (user == null)
        {
            return new QueryResult<BaseItemDto>();
        }

        int[] years = Enumerable.Range(decadeStart, 10).ToArray();
        DtoOptions dtoOptions = SectionDtoHelper.CreateDefaultDtoOptions();
        bool? isPlayed = GetHideWatchedIsPlayed();

        QueryResult<BaseItem> items = _libraryManager.GetItemsResult(new InternalItemsQuery(user)
        {
            IncludeItemTypes = [BaseItemKind.Movie],
            Recursive = true,
            Years = years,
            IsPlayed = isPlayed,
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
        User? user = userId is null || userId.Value == Guid.Empty
            ? null
            : _userManager.GetUserById(userId.Value);

        if (user == null)
        {
            yield break;
        }

        List<int> decades = FindDecadesWithMovies(user);
        if (decades.Count == 0)
        {
            yield break;
        }

        Random rnd = new Random();
        foreach (int decade in decades.OrderBy(_ => rnd.Next()).Take(instanceCount))
        {
            yield return new DecadeSection(_userManager, _libraryManager, _dtoService)
            {
                AdditionalData = decade.ToString(System.Globalization.CultureInfo.InvariantCulture),
                DisplayText = $"{decade}s Movies"
            };
        }
    }

public HomeScreenSectionInfo GetInfo() => SectionDtoHelper.CreateInfo(this, allowHideWatched: true);

    private List<int> FindDecadesWithMovies(User user)
    {
        QueryResult<BaseItem> sample = _libraryManager.GetItemsResult(new InternalItemsQuery(user)
        {
            IncludeItemTypes = [BaseItemKind.Movie],
            Recursive = true,
            Limit = 400,
            OrderBy = [(ItemSortBy.Random, SortOrder.Ascending)],
            EnableTotalRecordCount = false,
            IsVirtualItem = false
        });

        return sample.Items
            .Where(x => x.ProductionYear is >= 1900 and <= 2100)
            .Select(x => (x.ProductionYear!.Value / 10) * 10)
            .Distinct()
            .OrderByDescending(x => x)
            .ToList();
    }

    private static bool? GetHideWatchedIsPlayed()
    {
        SectionSettings? settings = HomeScreenSectionsPlugin.Instance?.Configuration?.SectionSettings
            .FirstOrDefault(x => string.Equals(x.SectionId, "Decade", StringComparison.Ordinal));
        return settings?.HideWatchedItems == true ? false : null;
    }
}
