using Jellyfin.Plugin.HomeScreenSections.Configuration;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections.RecentlyAdded
{
    public class RecentlyAddedMusicVideosSection : RecentlyAddedSectionBase
    {

        public override string? Section => "RecentlyAddedMusicVideos";
        
        public override string? DisplayText { get; set; } = "Recently Added Music Videos";
        
        public override string? Route => "musicvideos";
        
        public override string? AdditionalData { get; set; } = "musicvideos";
        
        protected override BaseItemKind SectionItemKind => BaseItemKind.MusicVideo;
        
        protected override CollectionType CollectionType => CollectionType.musicvideos;
        
        protected override CollectionTypeOptions CollectionTypeOptions => CollectionTypeOptions.musicvideos;
        
        protected override string? LibraryId => HomeScreenSectionsPlugin.Instance?.Configuration?.DefaultMusicVideosLibraryId;
        
        protected override SectionViewMode DefaultViewMode => SectionViewMode.Landscape;
        
        public RecentlyAddedMusicVideosSection(IUserViewManager userViewManager, IUserManager userManager, ILibraryManager libraryManager, IDtoService dtoService) : base(userViewManager, userManager, libraryManager, dtoService)
        {
        }
    }
}