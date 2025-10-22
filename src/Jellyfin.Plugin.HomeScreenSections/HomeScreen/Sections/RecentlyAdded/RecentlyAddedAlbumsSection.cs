using Jellyfin.Plugin.HomeScreenSections.Configuration;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections.RecentlyAdded
{
    public class RecentlyAddedAlbumsSection : RecentlyAddedSectionBase
    {
        public override string? Section => "RecentlyAddedAlbums";

        public override string? DisplayText { get; set; } = "Recently Added Albums";

        public override string? Route => "music";

        public override string? AdditionalData { get; set; } = "albums";

        protected override BaseItemKind SectionItemKind => BaseItemKind.MusicAlbum;

        protected override CollectionType CollectionType => CollectionType.music;

        protected override string? LibraryId => HomeScreenSectionsPlugin.Instance?.Configuration?.DefaultMusicLibraryId;

        protected override SectionViewMode DefaultViewMode => SectionViewMode.Square;

        public RecentlyAddedAlbumsSection(IUserViewManager userViewManager,
            IUserManager userManager,
            ILibraryManager libraryManager,
            IDtoService dtoService) : base(userViewManager, userManager, libraryManager, dtoService)
        {
        }
    }
}
