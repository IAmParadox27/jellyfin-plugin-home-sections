using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Playlists;

namespace Jellyfin.Plugin.HomeScreenSections.Tests.Support;

/// <summary>
/// Playlist whose children are scripted. BaseItem.Id/Name cannot be stubbed through Moq
/// (Moq rejects the member), so a real subclass with settable Id/Name is the seam.
/// </summary>
public sealed class TestPlaylist : Playlist
{
    private readonly IReadOnlyList<BaseItem> _children;

    public TestPlaylist(IReadOnlyList<BaseItem> children)
    {
        _children = children;
    }

    public override IReadOnlyList<BaseItem> GetChildren(User user, bool includeLinkedChildren, InternalItemsQuery? query)
    {
        return _children;
    }
}

/// <summary>
/// BoxSet whose children are scripted; see TestPlaylist for why a subclass is used.
/// </summary>
public sealed class TestBoxSet : BoxSet
{
    private readonly IReadOnlyList<BaseItem> _children;

    public TestBoxSet(IReadOnlyList<BaseItem> children)
    {
        _children = children;
    }

    public override IReadOnlyList<BaseItem> GetChildren(User user, bool includeLinkedChildren, InternalItemsQuery? query)
    {
        return _children;
    }
}
