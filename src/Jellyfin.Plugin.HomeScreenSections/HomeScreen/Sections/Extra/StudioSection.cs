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
/// Movies from a studio / network derived from the user's history (multi-instance).
/// </summary>
public class StudioSection : IHomeScreenSection
{
    public string? Section => "Studio";

    public string? DisplayText { get; set; } = "Studio";

    public int? Limit => 3;

    public string? Route => null;

    public string? AdditionalData { get; set; }

    public object? OriginalPayload { get; set; }

    private readonly IUserManager _userManager;
    private readonly ILibraryManager _libraryManager;
    private readonly IDtoService _dtoService;
    private readonly IUserDataManager _userDataManager;

    public StudioSection(
        IUserManager userManager,
        ILibraryManager libraryManager,
        IDtoService dtoService,
        IUserDataManager userDataManager)
    {
        _userManager = userManager;
        _libraryManager = libraryManager;
        _dtoService = dtoService;
        _userDataManager = userDataManager;
    }

    public QueryResult<BaseItemDto> GetResults(HomeScreenSectionPayload payload, IQueryCollection queryCollection)
    {
        if (string.IsNullOrWhiteSpace(payload.AdditionalData))
        {
            return new QueryResult<BaseItemDto>();
        }

        User? user = _userManager.GetUserById(payload.UserId);
        if (user == null)
        {
            return new QueryResult<BaseItemDto>();
        }

        DtoOptions dtoOptions = SectionDtoHelper.CreateDefaultDtoOptions();
        bool? isPlayed = GetHideWatchedIsPlayed();
        string studio = payload.AdditionalData;

        // Pull a random sample and filter by studio name (query has StudioIds, not names).
        QueryResult<BaseItem> items = _libraryManager.GetItemsResult(new InternalItemsQuery(user)
        {
            IncludeItemTypes = SectionDtoHelper.MovieAndSeriesKinds,
            Recursive = true,
            IsPlayed = isPlayed,
            Limit = 120,
            OrderBy = [(ItemSortBy.Random, SortOrder.Ascending)],
            DtoOptions = dtoOptions,
            EnableTotalRecordCount = false,
            IsVirtualItem = false
        });

        BaseItem[] matched = items.Items
            .Where(x => x.Studios != null
                && x.Studios.Any(s => string.Equals(s, studio, StringComparison.OrdinalIgnoreCase)))
            .Take(16)
            .ToArray();

        return new QueryResult<BaseItemDto>(_dtoService.GetBaseItemDtos(matched, dtoOptions, user));
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

        List<string> studios = GetStudiosForUser(user);
        if (studios.Count == 0)
        {
            yield break;
        }

        Random rnd = new Random();
        foreach (string studio in studios.OrderBy(_ => rnd.Next()).Take(instanceCount))
        {
            yield return new StudioSection(_userManager, _libraryManager, _dtoService, _userDataManager)
            {
                AdditionalData = studio,
                DisplayText = studio
            };
        }
    }

public HomeScreenSectionInfo GetInfo() => SectionDtoHelper.CreateInfo(this, allowHideWatched: true);

    private List<string> GetStudiosForUser(User user)
    {
        QueryResult<BaseItem> played = _libraryManager.GetItemsResult(new InternalItemsQuery(user)
        {
            IncludeItemTypes = SectionDtoHelper.MovieAndSeriesKinds,
            Recursive = true,
            IsPlayed = true,
            Limit = 200,
            EnableTotalRecordCount = false,
            IsVirtualItem = false
        });

        Dictionary<string, int> scores = new(StringComparer.OrdinalIgnoreCase);
        foreach (BaseItem item in played.Items)
        {
            if (item.Studios == null || item.Studios.Length == 0)
            {
                continue;
            }

            int weight = 1;
            UserItemData? data = _userDataManager.GetUserData(user, item);
            if (data?.PlayCount is > 0)
            {
                weight = data.PlayCount;
            }

            foreach (string studio in item.Studios)
            {
                if (string.IsNullOrWhiteSpace(studio))
                {
                    continue;
                }

                scores[studio] = scores.GetValueOrDefault(studio) + weight;
            }
        }

        return scores
            .OrderByDescending(x => x.Value)
            .Select(x => x.Key)
            .Take(12)
            .ToList();
    }

    private static bool? GetHideWatchedIsPlayed()
    {
        SectionSettings? settings = HomeScreenSectionsPlugin.Instance?.Configuration?.SectionSettings
            .FirstOrDefault(x => string.Equals(x.SectionId, "Studio", StringComparison.Ordinal));
        return settings?.HideWatchedItems == true ? false : null;
    }
}
