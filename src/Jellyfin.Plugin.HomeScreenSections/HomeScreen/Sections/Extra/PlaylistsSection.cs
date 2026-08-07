using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections.Extra;

/// <summary>
/// One home row per user playlist (multi-instance).
/// </summary>
public class PlaylistsSection : IHomeScreenSection
{
    public string? Section => "Playlists";

    public string? DisplayText { get; set; } = "Playlists";

    public int? Limit => 5;

    public string? Route => null;

    public string? AdditionalData { get; set; }

    public object? OriginalPayload { get; set; }

    private readonly IUserManager m_userManager;
    private readonly IDtoService m_dtoService;
    private readonly IPlaylistManager m_playlistManager;
    private readonly ILibraryManager m_libraryManager;

    public PlaylistsSection(
        IUserManager userManager,
        IDtoService dtoService,
        IPlaylistManager playlistManager,
        ILibraryManager libraryManager)
    {
        m_userManager = userManager;
        m_dtoService = dtoService;
        m_playlistManager = playlistManager;
        m_libraryManager = libraryManager;
    }

    public QueryResult<BaseItemDto> GetResults(HomeScreenSectionPayload payload, IQueryCollection queryCollection)
    {
        User? user = m_userManager.GetUserById(payload.UserId);
        if (user == null || string.IsNullOrWhiteSpace(payload.AdditionalData)
            || !Guid.TryParse(payload.AdditionalData, out Guid playlistId))
        {
            return new QueryResult<BaseItemDto>();
        }

        BaseItem? item = m_libraryManager.GetItemById(playlistId);
        if (item is not Playlist playlist)
        {
            return new QueryResult<BaseItemDto>();
        }

        DtoOptions dtoOptions = SectionDtoHelper.CreateDefaultDtoOptions();
        List<BaseItem> children = playlist.GetChildren(user, true, new InternalItemsQuery(user))
            .Take(24)
            .ToList();
        return new QueryResult<BaseItemDto>(m_dtoService.GetBaseItemDtos(children, dtoOptions, user));
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

        IEnumerable<Playlist> playlists = m_playlistManager.GetPlaylists(user.Id)
            .Where(p => !string.Equals(p.Name, "My List", StringComparison.OrdinalIgnoreCase))
            .Where(p => p.GetChildren(user, true, new InternalItemsQuery(user)).Count > 0)
            .Take(instanceCount);

        foreach (Playlist playlist in playlists)
        {
            yield return new PlaylistsSection(m_userManager, m_dtoService, m_playlistManager, m_libraryManager)
            {
                AdditionalData = playlist.Id.ToString("N"),
                DisplayText = playlist.Name,
                OriginalPayload = m_dtoService.GetBaseItemDto(playlist, linkDto, user)
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
}
