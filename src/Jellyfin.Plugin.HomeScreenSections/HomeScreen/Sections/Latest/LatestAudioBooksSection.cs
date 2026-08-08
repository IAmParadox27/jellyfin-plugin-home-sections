using Jellyfin.Plugin.HomeScreenSections.Configuration;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections.Latest
{
    public class LatestAudioBooksSection : LatestSectionBase
    {
        public override string? Section => "LatestAudioBooks";
        
        public override string? DisplayText { get; set; } = "Latest Audiobooks";
        
        public override string? Route => "books";
        
        public override string? AdditionalData { get; set; } = "audiobooks";
        
        public override SectionViewMode DefaultViewMode => SectionViewMode.Portrait;
        
        protected override BaseItemKind SectionItemKind => BaseItemKind.AudioBook;
        
        protected override CollectionType CollectionType => CollectionType.books;
        
        protected override string? LibraryId => HomeScreenSectionsPlugin.Instance?.Configuration?.DefaultBooksLibraryId;
        protected override CollectionTypeOptions CollectionTypeOptions => CollectionTypeOptions.books;

        public LatestAudioBooksSection(IUserViewManager userViewManager,
            IUserManager userManager,
            ILibraryManager libraryManager,
            IDtoService dtoService,
            IServiceProvider serviceProvider) : base(userViewManager, userManager, libraryManager, dtoService, serviceProvider)
        {
        }
        
        protected override LatestSectionBase CreateInstance()
        {
            return new LatestAudioBooksSection(_userViewManager, _userManager, _libraryManager, _dtoService, _serviceProvider);
        }
    }
}
