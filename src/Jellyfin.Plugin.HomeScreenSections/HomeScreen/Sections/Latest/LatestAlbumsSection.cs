using Jellyfin.Plugin.HomeScreenSections.Configuration;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections.Latest
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
        protected override CollectionTypeOptions CollectionTypeOptions => CollectionTypeOptions.music;

        public LatestAlbumsSection(IUserViewManager userViewManager,
            IUserManager userManager,
            ILibraryManager libraryManager,
            IDtoService dtoService,
            IServiceProvider serviceProvider) : base(userViewManager, userManager, libraryManager, dtoService, serviceProvider)
        {
        }
        
        protected override LatestSectionBase CreateInstance()
        {
            return new LatestAlbumsSection(m_userViewManager, m_userManager, m_libraryManager, m_dtoService, m_serviceProvider);
        }
    }
}
