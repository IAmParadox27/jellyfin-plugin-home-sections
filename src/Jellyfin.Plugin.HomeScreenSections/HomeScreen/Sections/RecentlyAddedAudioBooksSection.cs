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
    public class RecentlyAddedAudioBooksSection : RecentlyAddedSectionBase
    {
        public override string? Section => "RecentlyAddedAudioBooks";

        public override string? DisplayText { get; set; } = "Recently Added Audiobooks";

        public override string? Route => "books";

        public override string? AdditionalData { get; set; } = "audiobooks";

        protected override BaseItemKind SectionItemKind => BaseItemKind.AudioBook;
        protected override CollectionType CollectionType => CollectionType.books;
        protected override string? LibraryId => HomeScreenSectionsPlugin.Instance?.Configuration?.DefaultBooksLibraryId;
        protected override SectionViewMode DefaultViewMode => SectionViewMode.Portrait;

        public RecentlyAddedAudioBooksSection(IUserViewManager userViewManager,
            IUserManager userManager,
            ILibraryManager libraryManager,
            IDtoService dtoService) : base(userViewManager, userManager, libraryManager, dtoService)
        {
        }
    }
}
