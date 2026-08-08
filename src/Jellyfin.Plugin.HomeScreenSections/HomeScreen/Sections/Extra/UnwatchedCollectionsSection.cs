using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.JellyfinVersionSpecific;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections.Extra;

/// <summary>
/// Unwatched items from collections the user has started but not finished (multi-instance).
/// </summary>
public class UnwatchedCollectionsSection : IHomeScreenSection
{
    public string? Section => "UnwatchedCollections";

    public string? DisplayText { get; set; } = "Finish These Collections";

    public int? Limit => 3;

    public string? Route => null;

    public string? AdditionalData { get; set; }

    public object? OriginalPayload { get; set; }

    private readonly IUserManager _userManager;
    private readonly IDtoService _dtoService;
    private readonly CollectionManagerProxy _collectionManagerProxy;
    private readonly ILibraryManager _libraryManager;
    private readonly IUserDataManager _userDataManager;

    public UnwatchedCollectionsSection(
        IUserManager userManager,
        IDtoService dtoService,
        CollectionManagerProxy collectionManagerProxy,
        ILibraryManager libraryManager,
        IUserDataManager userDataManager)
    {
        _userManager = userManager;
        _dtoService = dtoService;
        _collectionManagerProxy = collectionManagerProxy;
        _libraryManager = libraryManager;
        _userDataManager = userDataManager;
    }

    public QueryResult<BaseItemDto> GetResults(HomeScreenSectionPayload payload, IQueryCollection queryCollection)
    {
        User? user = _userManager.GetUserById(payload.UserId);
        if (user == null || string.IsNullOrWhiteSpace(payload.AdditionalData)
            || !Guid.TryParse(payload.AdditionalData, out Guid collectionId))
        {
            return new QueryResult<BaseItemDto>();
        }

        BaseItem? item = _libraryManager.GetItemById(collectionId);
        if (item is not BoxSet boxSet)
        {
            return new QueryResult<BaseItemDto>();
        }

        DtoOptions dtoOptions = SectionDtoHelper.CreateDefaultDtoOptions();
        List<BaseItem> unwatched = boxSet.GetChildren(user, true, new InternalItemsQuery(user))
            .Where(child =>
            {
                UserItemData? data = _userDataManager.GetUserData(user, child);
                return data == null || !data.Played;
            })
            .Take(16)
            .ToList();

        return new QueryResult<BaseItemDto>(_dtoService.GetBaseItemDtos(unwatched, dtoOptions, user));
    }

    public IEnumerable<IHomeScreenSection> CreateInstances(Guid? userId, int instanceCount)
    {
        if (userId is null || userId.Value == Guid.Empty)
        {
            yield break;
        }

        User? user = _userManager.GetUserById(userId.Value);
        if (user == null)
        {
            yield break;
        }

        DtoOptions linkDto = new DtoOptions
        {
            Fields = [ItemFields.PrimaryImageAspectRatio,
                ItemFields.DisplayPreferencesId]
        };

        foreach (BoxSet boxSet in FindPartialCollections(user).Take(instanceCount))
        {
            yield return new UnwatchedCollectionsSection(
                _userManager,
                _dtoService,
                _collectionManagerProxy,
                _libraryManager,
                _userDataManager)
            {
                AdditionalData = boxSet.Id.ToString("N"),
                DisplayText = $"Continue: {boxSet.Name}",
                OriginalPayload = _dtoService.GetBaseItemDto(boxSet, linkDto, user)
            };
        }
    }

public HomeScreenSectionInfo GetInfo() => SectionDtoHelper.CreateInfo(this);

    private List<BoxSet> FindPartialCollections(User user)
    {
        List<BoxSet> partial = [];
        foreach (BoxSet boxSet in _collectionManagerProxy.GetCollections(user))
        {
            List<BaseItem> children = boxSet.GetChildren(user, true, new InternalItemsQuery(user)).ToList();
            if (children.Count < 2)
            {
                continue;
            }

            int played = children.Count(c => _userDataManager.GetUserData(user, c)?.Played == true);
            if (played > 0 && played < children.Count)
            {
                partial.Add(boxSet);
            }
        }

        return partial;
    }
}
