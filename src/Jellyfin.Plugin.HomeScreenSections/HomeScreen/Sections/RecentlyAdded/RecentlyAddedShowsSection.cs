using Jellyfin.Plugin.HomeScreenSections.Configuration;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections.RecentlyAdded
{
    public class RecentlyAddedShowsSection : RecentlyAddedSectionBase
    {
        public override string? Section => "RecentlyAddedShows";

        public override string? DisplayText { get; set; } = "Recently Added Shows";

        public override string? Route => "tvshows";

        public override string? AdditionalData { get; set; } = "tvshows";

        protected override BaseItemKind SectionItemKind => BaseItemKind.Series;

        protected override CollectionType CollectionType => CollectionType.tvshows;

        protected override string? LibraryId => HomeScreenSectionsPlugin.Instance?.Configuration?.DefaultTvShowsLibraryId;

        protected override SectionViewMode DefaultViewMode => SectionViewMode.Landscape;

        public RecentlyAddedShowsSection(IUserViewManager userViewManager,
            IUserManager userManager,
            ILibraryManager libraryManager,
            IDtoService dtoService) : base(userViewManager, userManager, libraryManager, dtoService)
        {
        }
    }
}
