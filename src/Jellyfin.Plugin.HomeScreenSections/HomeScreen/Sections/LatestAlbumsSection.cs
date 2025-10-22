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
    public class LatestAlbumsSection : LatestSectionBase
    {
        public override string? Section => "LatestAlbums";
        
        public override string? DisplayText { get; set; } = "Latest Albums";
        
        public override string? Route => "music";
        
        public override string? AdditionalData { get; set; } = "albums";
        
        public override SectionViewMode DefaultViewMode => SectionViewMode.Square;
        
        protected override BaseItemKind SectionItemKind => BaseItemKind.MusicAlbum;
        
        protected override CollectionType CollectionType => CollectionType.music;
        
        protected override string? LibraryId => HomeScreenSectionsPlugin.Instance?.Configuration?.DefaultMusicLibraryId;

        public LatestAlbumsSection(IUserViewManager userViewManager,
            IUserManager userManager,
            ILibraryManager libraryManager,
            IDtoService dtoService,
            IServiceProvider serviceProvider) : base(userViewManager, userManager, libraryManager, dtoService, serviceProvider)
        {
        }
    }
}
