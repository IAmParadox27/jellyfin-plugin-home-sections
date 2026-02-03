using Jellyfin.Plugin.HomeScreenSections.Configuration;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections.Latest
{
    public class LatestMusicVideoSection : LatestSectionBase
    {
        public override string? Section => "LatestMusicVideo";
        public override string? DisplayText { get; set; } = "Latest Music Videos";
        public override SectionViewMode DefaultViewMode => SectionViewMode.Landscape;
        protected override BaseItemKind SectionItemKind => BaseItemKind.MusicVideo;
        protected override CollectionType CollectionType => CollectionType.musicvideos;
        protected override string? LibraryId => HomeScreenSectionsPlugin.Instance?.Configuration?.DefaultMusicVideosLibraryId;
        protected override CollectionTypeOptions CollectionTypeOptions => CollectionTypeOptions.musicvideos;

        public LatestMusicVideoSection(IUserViewManager userViewManager, 
            IUserManager userManager, 
            ILibraryManager libraryManager, 
            IDtoService dtoService, 
            IServiceProvider serviceProvider) : base(userViewManager, userManager, libraryManager, dtoService, serviceProvider)
        {
        }
        
        protected override LatestSectionBase CreateInstance()
        {
            return new LatestMusicVideoSection(m_userViewManager, m_userManager, m_libraryManager, m_dtoService, m_serviceProvider);
        }
    }
}