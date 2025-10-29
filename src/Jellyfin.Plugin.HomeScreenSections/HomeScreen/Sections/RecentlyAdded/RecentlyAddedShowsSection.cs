using Jellyfin.Plugin.HomeScreenSections.Configuration;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections.RecentlyAdded
{
    public class RecentlyAddedShowsSection : RecentlyAddedSectionBase
    {
        public override string? Section => "RecentlyAddedShows";

        public override string? DisplayText { get; set; } = "Recently Added Shows";

        public override string? Route => "tvshows";

        public override string? AdditionalData { get; set; } = "tvshows";

        protected override BaseItemKind SectionItemKind => BaseItemKind.Series;

        protected override CollectionType CollectionType => CollectionType.tvshows;
        
        protected override CollectionTypeOptions CollectionTypeOptions => CollectionTypeOptions.tvshows;

        protected override string? LibraryId => HomeScreenSectionsPlugin.Instance?.Configuration?.DefaultTVShowsLibraryId;

        protected override SectionViewMode DefaultViewMode => SectionViewMode.Landscape;

        public RecentlyAddedShowsSection(IUserViewManager userViewManager,
            IUserManager userManager,
            ILibraryManager libraryManager,
            IDtoService dtoService) : base(userViewManager, userManager, libraryManager, dtoService)
        {
        }

        protected override IEnumerable<BaseItem> GetItems(User? user, DtoOptions dtoOptions, VirtualFolderInfo[] folders, bool? isPlayed)
        {
            // Default behaviour is to get the 16 most recently added items from each library that matches, then order that by date created and take 16.
            // The reason we do this is to ensure that we always get 16 items, even if there is only 1 library that matches our type.
            return folders.SelectMany(x =>
            {
                return m_libraryManager.GetItemList(new InternalItemsQuery(user)
                {
                    IncludeItemTypes = new[]
                    {
                        SectionItemKind
                    },
                    DtoOptions = dtoOptions,
                    IsPlayed = isPlayed,
                    OrderBy = new [] { (ItemSortBy.DateCreated, SortOrder.Descending) },
                    Limit = 16
                });
            }).DistinctBy(x => x.Id).OrderByDescending(x => GetSortDateForItem(x, user, dtoOptions)).Take(16);
        }

        protected override DateTime GetSortDateForItem(BaseItem item, User? user, DtoOptions dtoOptions)
        {
            DateTime? dateCreated = null;
            
            if (item is Series series)
            {
                string? seriesKey = series.GetPresentationUniqueKey();

                InternalItemsQuery query = new InternalItemsQuery(user)
                {
                    AncestorWithPresentationUniqueKey = null,
                    SeriesPresentationUniqueKey = seriesKey,
                    IncludeItemTypes = new[] { BaseItemKind.Episode },
                    OrderBy = new[] { (ItemSortBy.DateCreated, SortOrder.Descending) },
                    DtoOptions = dtoOptions,
                    IsMissing = false,
                    Limit = 1
                };

                dateCreated = m_libraryManager.GetItemList(query).FirstOrDefault()?.DateCreated;
            }
            else if (item is Season season)
            {
                dateCreated = season.GetEpisodes(user, dtoOptions, false).Max(x => x.DateCreated);
            }

            dateCreated ??= base.GetSortDateForItem(item, user, dtoOptions);
            
            return dateCreated.Value;
        }
    }
}
