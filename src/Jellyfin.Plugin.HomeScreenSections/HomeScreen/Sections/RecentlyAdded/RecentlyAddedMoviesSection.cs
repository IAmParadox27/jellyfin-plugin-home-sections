using Jellyfin.Plugin.HomeScreenSections.Configuration;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections.RecentlyAdded
{
    /// <summary>
    /// Latest Movies Section.
    /// </summary>
    public class RecentlyAddedMoviesSection : RecentlyAddedSectionBase
    {
        /// <inheritdoc/>
        public override string? Section => "RecentlyAddedMovies";

        /// <inheritdoc/>
        public override string? DisplayText { get; set; } = "Recently Added Movies";

        /// <inheritdoc/>
        public override string? Route => "movies";

        /// <inheritdoc/>
        public override string? AdditionalData { get; set; } = "movies";

        protected override BaseItemKind SectionItemKind => BaseItemKind.Movie;

        protected override CollectionType CollectionType => CollectionType.movies;

        protected override string? LibraryId => HomeScreenSectionsPlugin.Instance?.Configuration?.DefaultMoviesLibraryId;

        protected override SectionViewMode DefaultViewMode => SectionViewMode.Landscape;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="userViewManager">Instance of <see href="IUserViewManager" /> interface.</param>
        /// <param name="userManager">Instance of <see href="IUserManager" /> interface.</param>
        /// <param name="libraryManager"></param>
        /// <param name="dtoService">Instance of <see href="IDtoService" /> interface.</param>
        public RecentlyAddedMoviesSection(IUserViewManager userViewManager,
            IUserManager userManager,
            ILibraryManager libraryManager,
            IDtoService dtoService) : base(userViewManager, userManager, libraryManager, dtoService)
        {
        }
    }
}
