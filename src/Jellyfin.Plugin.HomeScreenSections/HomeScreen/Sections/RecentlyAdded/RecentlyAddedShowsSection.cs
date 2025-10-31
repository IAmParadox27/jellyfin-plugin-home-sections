using Jellyfin.Plugin.HomeScreenSections.Configuration;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections.RecentlyAdded
{
    public class RecentlyAddedShowsSection : RecentlyAddedSectionBase
    {
        private readonly ILogger<RecentlyAddedShowsSection> m_logger;
        
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
            IDtoService dtoService, ILogger<RecentlyAddedShowsSection> logger) : base(userViewManager, userManager, libraryManager, dtoService)
        {
            m_logger = logger;
        }

        protected override IEnumerable<BaseItem> GetItems(User? user, DtoOptions dtoOptions, VirtualFolderInfo[] folders, bool? isPlayed)
        {
            return folders.SelectMany(x =>
            {
                return m_libraryManager.GetItemList(new InternalItemsQuery(user)
                {
                    IncludeItemTypes = new[]
                    {
                        SectionItemKind
                    },
                    DtoOptions = dtoOptions,
                    IsPlayed = isPlayed
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

                BaseItem? latestItemAddedForShow = m_libraryManager.GetItemList(query).FirstOrDefault();

                dateCreated = latestItemAddedForShow?.DateCreated;
                
                m_logger.LogInformation($"Show '{series.Name}' has been sorted based on episode '{latestItemAddedForShow?.Name}' which has date created of: {dateCreated}.");
                
                // This is debug code to help with testing the issue.
                // It is slow, so it should only be used when debugging.
                InternalItemsQuery debugQuery = new InternalItemsQuery(user)
                {
                    AncestorWithPresentationUniqueKey = null,
                    SeriesPresentationUniqueKey = seriesKey,
                    IncludeItemTypes = new[] { BaseItemKind.Episode },
                    OrderBy = new[] { (ItemSortBy.DateCreated, SortOrder.Descending) },
                    DtoOptions = dtoOptions,
                    IsMissing = false
                };
                IReadOnlyList<BaseItem> allEpisodesInShow = m_libraryManager.GetItemList(debugQuery);

                if (allEpisodesInShow.Max(x => x.DateCreated) != latestItemAddedForShow?.DateCreated)
                {
                    m_logger.LogWarning($"After getting all episodes for the show and getting the largest DateCreated value, the value is not the same as the one found using the quicker query. This is a bug.");
                }
            }
            else if (item is Season season)
            {
                List<BaseItem>? seasonEpisodes = season.GetEpisodes(user, dtoOptions, false);
                dateCreated = (seasonEpisodes?.Any() ?? false) ? seasonEpisodes.Max(x => x.DateCreated) : null;
                
                m_logger.LogInformation($"Season '{season.Name}' has been sorted based on an episode having a date created of: {dateCreated}.");
            }

            if (dateCreated == null)
            {
                dateCreated = base.GetSortDateForItem(item, user, dtoOptions);
                m_logger.LogInformation($"Item '{item.Name}' has been sorted based on the default behaviour with a value of: {dateCreated}.");
            }
            
            return dateCreated.Value;
        }
    }
}
