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
    }
}