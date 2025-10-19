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

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections
{
    public class RecentlyAddedArtistsSection : RecentlyAddedSectionBase
    {
        public override string? Section => "RecentlyAddedArtists";

        public override string? DisplayText { get; set; } = "Recently Added Artists";

        public override string? Route => "music";

        public override string? AdditionalData { get; set; } = "artists";
        protected override BaseItemKind SectionItemKind => BaseItemKind.MusicArtist;

        protected override CollectionType CollectionType => CollectionType.music;

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
