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

    private readonly IUserManager m_userManager;
    private readonly IDtoService m_dtoService;
    private readonly CollectionManagerProxy m_collectionManagerProxy;
    private readonly ILibraryManager m_libraryManager;
    private readonly IUserDataManager m_userDataManager;

    public UnwatchedCollectionsSection(
        IUserManager userManager,
        IDtoService dtoService,
        CollectionManagerProxy collectionManagerProxy,
        ILibraryManager libraryManager,
        IUserDataManager userDataManager)
    {
        m_userManager = userManager;
        m_dtoService = dtoService;
        m_collectionManagerProxy = collectionManagerProxy;
        m_libraryManager = libraryManager;
        m_userDataManager = userDataManager;
    }

    public QueryResult<BaseItemDto> GetResults(HomeScreenSectionPayload payload, IQueryCollection queryCollection)
    {
        User? user = m_userManager.GetUserById(payload.UserId);
        if (user == null || string.IsNullOrWhiteSpace(payload.AdditionalData)
            || !Guid.TryParse(payload.AdditionalData, out Guid collectionId))
        {
            return new QueryResult<BaseItemDto>();
        }

        BaseItem? item = m_libraryManager.GetItemById(collectionId);
        if (item is not BoxSet boxSet)
        {
            return new QueryResult<BaseItemDto>();
        }

        DtoOptions dtoOptions = SectionDtoHelper.CreateDefaultDtoOptions();
        List<BaseItem> unwatched = boxSet.GetChildren(user, true, new InternalItemsQuery(user))
            .Where(child =>
            {
                UserItemData? data = m_userDataManager.GetUserData(user, child);
                return data == null || !data.Played;
            })
            .Take(16)
            .ToList();

        return new QueryResult<BaseItemDto>(m_dtoService.GetBaseItemDtos(unwatched, dtoOptions, user));
    }

    public IEnumerable<IHomeScreenSection> CreateInstances(Guid? userId, int instanceCount)
    {
        if (userId is null || userId.Value == Guid.Empty)
        {
            yield break;
        }

        User? user = m_userManager.GetUserById(userId.Value);
        if (user == null)
        {
            yield break;
        }

        DtoOptions linkDto = new DtoOptions
        {
            Fields = new List<ItemFields>
            {
                ItemFields.PrimaryImageAspectRatio,
                ItemFields.DisplayPreferencesId
            }
        };

        foreach (BoxSet boxSet in FindPartialCollections(user).Take(instanceCount))
        {
            yield return new UnwatchedCollectionsSection(
                m_userManager,
                m_dtoService,
                m_collectionManagerProxy,
                m_libraryManager,
                m_userDataManager)
            {
                AdditionalData = boxSet.Id.ToString("N"),
                DisplayText = $"Continue: {boxSet.Name}",
                OriginalPayload = m_dtoService.GetBaseItemDto(boxSet, linkDto, user)
            };
        }
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

    private List<BoxSet> FindPartialCollections(User user)
    {
        List<BoxSet> partial = new List<BoxSet>();
        foreach (BoxSet boxSet in m_collectionManagerProxy.GetCollections(user))
        {
            List<BaseItem> children = boxSet.GetChildren(user, true, new InternalItemsQuery(user)).ToList();
            if (children.Count < 2)
            {
                continue;
            }

            int played = children.Count(c => m_userDataManager.GetUserData(user, c)?.Played == true);
            if (played > 0 && played < children.Count)
            {
                partial.Add(boxSet);
            }
        }

        return partial;
    }
}
