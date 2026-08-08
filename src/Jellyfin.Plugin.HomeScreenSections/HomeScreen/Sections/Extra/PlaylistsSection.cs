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

    private readonly IUserManager _userManager;
    private readonly IDtoService _dtoService;
    private readonly IPlaylistManager _playlistManager;
    private readonly ILibraryManager _libraryManager;

    public PlaylistsSection(
        IUserManager userManager,
        IDtoService dtoService,
        IPlaylistManager playlistManager,
        ILibraryManager libraryManager)
    {
        _userManager = userManager;
        _dtoService = dtoService;
        _playlistManager = playlistManager;
        _libraryManager = libraryManager;
    }

    public QueryResult<BaseItemDto> GetResults(HomeScreenSectionPayload payload, IQueryCollection queryCollection)
    {
        User? user = _userManager.GetUserById(payload.UserId);
        if (user == null || string.IsNullOrWhiteSpace(payload.AdditionalData)
            || !Guid.TryParse(payload.AdditionalData, out Guid playlistId))
        {
            return new QueryResult<BaseItemDto>();
        }

        BaseItem? item = _libraryManager.GetItemById(playlistId);
        if (item is not Playlist playlist)
        {
            return new QueryResult<BaseItemDto>();
        }

        DtoOptions dtoOptions = SectionDtoHelper.CreateDefaultDtoOptions();
        List<BaseItem> children = playlist.GetChildren(user, true, new InternalItemsQuery(user))
            .Take(24)
            .ToList();
        return new QueryResult<BaseItemDto>(_dtoService.GetBaseItemDtos(children, dtoOptions, user));
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

        IEnumerable<Playlist> playlists = _playlistManager.GetPlaylists(user.Id)
            .Where(p => !string.Equals(p.Name, "My List", StringComparison.OrdinalIgnoreCase))
            .Where(p => p.GetChildren(user, true, new InternalItemsQuery(user)).Count > 0)
            .Take(instanceCount);

        foreach (Playlist playlist in playlists)
        {
            yield return new PlaylistsSection(_userManager, _dtoService, _playlistManager, _libraryManager)
            {
                AdditionalData = playlist.Id.ToString("N"),
                DisplayText = playlist.Name,
                OriginalPayload = _dtoService.GetBaseItemDto(playlist, linkDto, user)
            };
        }
    }

public HomeScreenSectionInfo GetInfo() => SectionDtoHelper.CreateInfo(this);
}
