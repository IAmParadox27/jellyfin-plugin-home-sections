using Jellyfin.Plugin.HomeScreenSections.Configuration;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections.RecentlyAdded
{
    public class RecentlyAddedArtistsSection : RecentlyAddedSectionBase
    {
        public override string? Section => "RecentlyAddedArtists";

        public override string? DisplayText { get; set; } = "Recently Added Artists";

        public override string? Route => "music";

        public override string? AdditionalData { get; set; } = "artists";
        protected override BaseItemKind SectionItemKind => BaseItemKind.MusicArtist;

        protected override CollectionType CollectionType => CollectionType.music;
        
        protected override CollectionTypeOptions CollectionTypeOptions => CollectionTypeOptions.music;

        protected override string? LibraryId => HomeScreenSectionsPlugin.Instance?.Configuration?.DefaultMusicLibraryId;

        protected override SectionViewMode DefaultViewMode => SectionViewMode.Portrait;

        public RecentlyAddedArtistsSection(IUserViewManager userViewManager,
            IUserManager userManager,
            ILibraryManager libraryManager,
            IDtoService dtoService) : base(userViewManager, userManager, libraryManager, dtoService)
        {
        }
    }
}
