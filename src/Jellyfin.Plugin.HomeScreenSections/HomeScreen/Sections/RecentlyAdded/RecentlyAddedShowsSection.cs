using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Helpers;
using Jellyfin.Plugin.HomeScreenSections.JellyfinVersionSpecific;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
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
            IDtoService dtoService,
            IServiceProvider serviceProvider,
            ILogger<RecentlyAddedShowsSection> logger) : base(userViewManager, userManager, libraryManager, dtoService, serviceProvider)
        {
            m_logger = logger;
        }

        protected override IEnumerable<PluginConfigurationOption> GetPluginConfigurationOptionsInternal()
        {
            yield return PluginConfigurationHelper.CreateDropdown("itemType", "Item Type",
                "What type of item do you want to display?", new Dictionary<string, string>()
                {
                    { "shows", "Shows" },
                    { "episodes", "Episodes" }
                }, "AdminRecentlyAddedShowsItemTypeDropdown", "shows", true);
        }

        protected override IEnumerable<BaseItem> GetItems(User? user, DtoOptions dtoOptions, VirtualFolderInfo[] folders, bool? isPlayed, HomeScreenSectionPayload payload)
        {
            string itemType = HomeScreenSectionPayload.GetEffectiveStringConfig(Section ?? string.Empty, "itemType", "shows");

            if (itemType == "shows")
            {
                return GetShowItems(user, dtoOptions, folders, isPlayed);
            }

            if (itemType == "episodes")
            {
                return GetEpisodeItems(user, dtoOptions, folders, isPlayed);
            }
            
            return Enumerable.Empty<BaseItem>();
        }

        private IEnumerable<BaseItem> GetShowItems(User? user, DtoOptions dtoOptions, VirtualFolderInfo[] folders,
            bool? isPlayed)
        {
            IEnumerable<BaseItem> candidateShows = folders.SelectMany(x =>
            {
                var item = m_libraryManager.GetParentItem(Guid.Parse(x.ItemId), user?.Id);

                if (item is not Folder folder)
                {
                    folder = m_libraryManager.GetUserRootFolder();
                }

                return folder.GetItems(new InternalItemsQuery(user)
                {
                    IncludeItemTypes = new[]
                    {
                        SectionItemKind
                    },
                    DtoOptions = dtoOptions,
                    EnableTotalRecordCount = false
                }).Items;
            })
            .DistinctBy(x => x.Id);

            // Filter watch status in memory to avoid expensive database query
            if (isPlayed.HasValue && user != null)
            {
                candidateShows = candidateShows.Where(x => x.IsPlayedVersionSpecific(user) == isPlayed.Value);
            }

            // Materialize to prevent re-execution, then sort by latest episode date
            return candidateShows
                .ToArray()
                .OrderByDescending(x => GetSortDateForItem(x, user, dtoOptions))
                .Take(16);
        }

        private IEnumerable<BaseItem> GetEpisodeItems(User? user, DtoOptions dtoOptions, VirtualFolderInfo[] folders,
            bool? isPlayed)
        {
            return folders.SelectMany(x =>
            {
                var item = m_libraryManager.GetParentItem(Guid.Parse(x.ItemId), user?.Id);

                if (item is not Folder folder)
                {
                    folder = m_libraryManager.GetUserRootFolder();
                }

                return folder.GetItems(new InternalItemsQuery(user)
                {
                    IncludeItemTypes = new[]
                    {
                        BaseItemKind.Episode
                    },
                    DtoOptions = dtoOptions,
                    IsPlayed = isPlayed,
                    OrderBy = new[] { (ItemSortBy.DateCreated, SortOrder.Descending) },
                    Limit = 16,
                    IsMissing = false,
                    Recursive = true,
                    ParentId = folder.Id
                }).Items;
            }).DistinctBy(x => x.Id)
            .OrderByDescending(x => GetSortDateForItem(x, user, dtoOptions))
            .Take(16);
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
                    IsVirtualItem = false,
                    EnableTotalRecordCount = false,
                    Limit = 1
                };

                BaseItem? latestItemAddedForShow = m_libraryManager.GetItemList(query).FirstOrDefault();

                dateCreated = latestItemAddedForShow?.DateCreated;
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
