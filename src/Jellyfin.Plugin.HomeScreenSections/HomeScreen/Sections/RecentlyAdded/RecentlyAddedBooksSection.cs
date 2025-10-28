using Jellyfin.Plugin.HomeScreenSections.Configuration;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections.RecentlyAdded
{
    public class RecentlyAddedBooksSection : RecentlyAddedSectionBase
    {
        public override string? Section => "RecentlyAddedBooks";

        public override string? DisplayText { get; set; } = "Recently Added Books";

        public override string? Route => "books";

        public override string? AdditionalData { get; set; } = "books";

        protected override BaseItemKind SectionItemKind => BaseItemKind.Book;

        protected override CollectionType CollectionType => CollectionType.books;
        
        protected override CollectionTypeOptions CollectionTypeOptions => CollectionTypeOptions.books;

        protected override string? LibraryId => HomeScreenSectionsPlugin.Instance?.Configuration?.DefaultBooksLibraryId;

        protected override SectionViewMode DefaultViewMode => SectionViewMode.Portrait;

        public RecentlyAddedBooksSection(IUserViewManager userViewManager,
            IUserManager userManager,
            ILibraryManager libraryManager,
            IDtoService dtoService) : base(userViewManager, userManager, libraryManager, dtoService)
        {
        }
    }
}
