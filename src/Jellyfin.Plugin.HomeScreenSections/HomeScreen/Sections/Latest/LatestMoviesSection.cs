using Jellyfin.Plugin.HomeScreenSections.Configuration;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections.Latest
{
    public class LatestMoviesSection : LatestSectionBase
    {
        public override string? Section => "LatestMovies";
        
        public override string? DisplayText { get; set; } = "Latest Movies";
        
        public override string? Route => "movies";

        public override SectionViewMode DefaultViewMode => SectionViewMode.Landscape;
        
        protected override BaseItemKind SectionItemKind => BaseItemKind.Movie;
        
        protected override CollectionType CollectionType => CollectionType.movies;
        
        protected override string? LibraryId => HomeScreenSectionsPlugin.Instance?.Configuration?.DefaultMoviesLibraryId;

        public LatestMoviesSection(IUserViewManager userViewManager,
            IUserManager userManager,
            ILibraryManager libraryManager,
            IDtoService dtoService,
            IServiceProvider serviceProvider) : base(userViewManager, userManager, libraryManager, dtoService, serviceProvider)
        {
        }
        
        protected override LatestSectionBase CreateInstance()
        {
            return new LatestMoviesSection(m_userViewManager, m_userManager, m_libraryManager, m_dtoService, m_serviceProvider);
        }
    }
}