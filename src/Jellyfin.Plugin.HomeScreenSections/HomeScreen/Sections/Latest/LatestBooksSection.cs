using Jellyfin.Plugin.HomeScreenSections.Configuration;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections.Latest
{
    public class LatestBooksSection : LatestSectionBase
    {
        public override string? Section => "LatestBooks";
        
        public override  string? DisplayText { get; set; } = "Latest Books";
        
        public override string? Route => "books";
        
        public override string? AdditionalData { get; set; } = "books";
        
        public override SectionViewMode DefaultViewMode => SectionViewMode.Portrait;
        
        protected override BaseItemKind SectionItemKind => BaseItemKind.Book;
        
        protected override CollectionType CollectionType => CollectionType.books;
        
        protected override string? LibraryId => HomeScreenSectionsPlugin.Instance?.Configuration?.DefaultBooksLibraryId;
        protected override CollectionTypeOptions CollectionTypeOptions => CollectionTypeOptions.books;

        public LatestBooksSection(IUserViewManager userViewManager,
            IUserManager userManager,
            ILibraryManager libraryManager,
            IDtoService dtoService,
            IServiceProvider serviceProvider) : base(userViewManager, userManager, libraryManager, dtoService, serviceProvider)
        {
        }
        
        protected override LatestSectionBase CreateInstance()
        {
            return new LatestBooksSection(m_userViewManager, m_userManager, m_libraryManager, m_dtoService, m_serviceProvider);
        }
    }
}
