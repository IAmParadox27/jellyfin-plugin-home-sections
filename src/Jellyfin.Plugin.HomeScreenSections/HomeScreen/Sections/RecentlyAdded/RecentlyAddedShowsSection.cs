using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Helpers;
using Jellyfin.Plugin.HomeScreenSections.JellyfinVersionSpecific;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
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

        protected override IEnumerable<BaseItem> GetItems(User? user, DtoOptions dtoOptions, VirtualFolderInfo[] folders, bool? isPlayed, HomeScreenSectionPayload payload, Folder? folderOverride = null)
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

            BaseItem[] shows = candidateShows.ToArray();
            if (shows.Length == 0)
            {
                return shows;
            }

            Dictionary<Guid, DateTime> sortDates = new Dictionary<Guid, DateTime>();
            Dictionary<string, DateTime?> episodeDates = new Dictionary<string, DateTime?>();

            if (shows.Length <= 16)
            {
                ResolveSortDates(shows, user, dtoOptions, sortDates, episodeDates);
            }
            else
            {
                // Hints only identify candidates. The original user-scoped lookup supplies every score.
                HashSet<string> recentSeriesKeys = GetEpisodeSeriesKeys(null, 200);
                HashSet<string> candidateSeriesKeys = shows.OfType<Series>()
                    .Select(x => x.GetPresentationUniqueKey())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToHashSet();
                if (recentSeriesKeys.Count(candidateSeriesKeys.Contains) < 16)
                {
                    // A backfill may fill the raw hint page with one series. Grouped hints only seed more exact lookups.
                    recentSeriesKeys.UnionWith(GetEpisodeSeriesKeys(null, 200, true));
                }
                HashSet<Guid> newestShows = shows.OrderByDescending(x => x.DateCreated)
                    .Take(16)
                    .Select(x => x.Id)
                    .ToHashSet();
                ResolveSortDates(shows.Where(x => newestShows.Contains(x.Id)
                    || x is not Series
                    || string.IsNullOrWhiteSpace(x.GetPresentationUniqueKey())
                    || recentSeriesKeys.Contains(x.GetPresentationUniqueKey())),
                    user, dtoOptions, sortDates, episodeDates);

                DateTime cutoff = sortDates.Values.OrderByDescending(x => x).ElementAt(15);
                // Grouping after the date filter finds every key that might beat or tie the cutoff.
                // A representative episode's date is never treated as the series maximum.
                HashSet<string> possibleSeriesKeys = GetEpisodeSeriesKeys(cutoff, null);
                ResolveSortDates(shows.Where(x => !sortDates.ContainsKey(x.Id)
                    && (x.DateCreated >= cutoff || possibleSeriesKeys.Contains(x.GetPresentationUniqueKey()))),
                    user, dtoOptions, sortDates, episodeDates);
            }

            return shows.Where(x => sortDates.ContainsKey(x.Id))
                .OrderByDescending(x => sortDates[x.Id])
                .Take(16);
        }

        private HashSet<string> GetEpisodeSeriesKeys(DateTime? minDateCreated, int? limit, bool groupBySeries = false)
        {
            // The per-series score query can see presentation versions outside the candidate folder.
            // Keep these hints a superset, without user/library filters; never return their items or dates.
            IReadOnlyList<BaseItem> episodes = m_libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Episode },
                IsMissing = false,
                IsVirtualItem = false,
                GroupByPresentationUniqueKey = false,
                GroupBySeriesPresentationUniqueKey = minDateCreated.HasValue || groupBySeries,
                MinDateCreated = minDateCreated,
                OrderBy = limit.HasValue ? new[] { (ItemSortBy.DateCreated, SortOrder.Descending) } : Array.Empty<(ItemSortBy, SortOrder)>(),
                Limit = limit,
                EnableTotalRecordCount = false,
                DtoOptions = new DtoOptions { EnableImages = false, Fields = Array.Empty<ItemFields>() }
            });

            return episodes.OfType<Episode>()
                .Select(x => x.SeriesPresentationUniqueKey)
                .Where(x => !string.IsNullOrEmpty(x))
                .ToHashSet();
        }

        private void ResolveSortDates(IEnumerable<BaseItem> shows, User? user, DtoOptions dtoOptions,
            Dictionary<Guid, DateTime> sortDates, Dictionary<string, DateTime?> episodeDates)
        {
            foreach (BaseItem item in shows)
            {
                if (item is Series series && !string.IsNullOrWhiteSpace(series.GetPresentationUniqueKey()))
                {
                    string seriesKey = series.GetPresentationUniqueKey();
                    if (!episodeDates.TryGetValue(seriesKey, out DateTime? episodeDate))
                    {
                        episodeDate = GetLatestEpisodeDate(seriesKey, user, dtoOptions);
                        episodeDates[seriesKey] = episodeDate;
                    }

                    sortDates[item.Id] = episodeDate ?? item.DateCreated;
                }
                else
                {
                    sortDates[item.Id] = GetSortDateForItem(item, user, dtoOptions);
                }
            }
        }

        private DateTime? GetLatestEpisodeDate(string? seriesKey, User? user, DtoOptions dtoOptions)
        {
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

            return m_libraryManager.GetItemList(query).FirstOrDefault()?.DateCreated;
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
                dateCreated = GetLatestEpisodeDate(series.GetPresentationUniqueKey(), user, dtoOptions);
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
