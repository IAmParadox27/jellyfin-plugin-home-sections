using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Helpers;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.TV;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections.Latest
{
    public class LatestShowsSection : LatestSectionBase
    {
        public override string? Section => "LatestShows";
        
        public override string? Route => "tvshows";

        public override string? DisplayText { get; set; } = "Latest Shows";

        private readonly ITVSeriesManager m_tvSeriesManager;
        
        public LatestShowsSection(IUserViewManager userViewManager,
            IUserManager userManager,
            ILibraryManager libraryManager,
            ITVSeriesManager tvSeriesManager,
            IDtoService dtoService,
            IServiceProvider serviceProvider) : base(userViewManager, userManager, libraryManager, dtoService, serviceProvider)
        {
            m_tvSeriesManager = tvSeriesManager;
        }

        public override SectionViewMode DefaultViewMode => SectionViewMode.Landscape;
        protected override BaseItemKind SectionItemKind => BaseItemKind.Episode;
        protected override CollectionType CollectionType => CollectionType.tvshows;
        protected override string? LibraryId => HomeScreenSectionsPlugin.Instance?.Configuration?.DefaultTVShowsLibraryId;
        protected override CollectionTypeOptions CollectionTypeOptions => CollectionTypeOptions.tvshows;

        public override QueryResult<BaseItemDto> GetResults(HomeScreenSectionPayload payload, IQueryCollection queryCollection)
        {
            DtoOptions? dtoOptions = new DtoOptions
            {
                Fields = new List<ItemFields>
                {
                    ItemFields.PrimaryImageAspectRatio,
                    ItemFields.Path
                }
            };

            dtoOptions.ImageTypeLimit = 1;
            dtoOptions.ImageTypes = new List<ImageType>
            {
                ImageType.Thumb,
                ImageType.Backdrop,
                ImageType.Primary,
            };

            User? user = m_userManager.GetUserById(payload.UserId);

            var config = HomeScreenSectionsPlugin.Instance?.Configuration;
            var sectionSettings = config?.SectionSettings.FirstOrDefault(x => x.SectionId == Section);
            // If HideWatchedItems is enabled for this section, set isPlayed to false to hide watched items; otherwise, include all.
            bool? isPlayed = sectionSettings?.HideWatchedItems == true ? false : null;

            VirtualFolderInfo[] folders = m_libraryManager.GetVirtualFolders()
                .Where(x => x.CollectionType == CollectionTypeOptions)
                .FilterToUserPermitted(m_libraryManager, user);

            const int episodePageSize = 200;
            DateTime currentDate = DateTime.Now;
            Dictionary<Guid, DateTime?> latestPremiereDates = new Dictionary<Guid, DateTime?>();
            Dictionary<Guid, Series> visibleSeries = new Dictionary<Guid, Series>();
            HashSet<Guid> resolvedSeriesIds = new HashSet<Guid>();

            foreach (VirtualFolderInfo virtualFolder in folders)
            {
                BaseItem item = m_libraryManager.GetParentItem(Guid.Parse(virtualFolder.ItemId), user?.Id);

                if (item is not Folder folder)
                {
                    folder = m_libraryManager.GetUserRootFolder();
                }

                HashSet<string?> folderSeriesKeys = new HashSet<string?>();
                int startIndex = 0;

                while (folderSeriesKeys.Count < 16)
                {
                    QueryResult<BaseItem> page = folder.GetItems(new InternalItemsQuery(user)
                    {
                        IncludeItemTypes = new[] { SectionItemKind },
                        OrderBy = new[] { (ItemSortBy.PremiereDate, SortOrder.Descending) },
                        StartIndex = startIndex,
                        Limit = episodePageSize,
                        IsVirtualItem = false,
                        IsPlayed = isPlayed,
                        Recursive = true,
                        ParentId = folder.Id,
                        MaxPremiereDate = currentDate,
                        MinPremiereDate = new DateTime(1925, 1, 1),
                        EnableTotalRecordCount = false
                    });

                    // Advance by the raw page, including unaired episodes and unresolved series.
                    startIndex += page.Items.Count;
                    List<(Guid SeriesId, DateTime? PremiereDate)> episodes = page.Items.OfType<Episode>()
                        .Where(x => !x.IsUnaired)
                        .Select(x => (SeriesId: x.SeriesId == Guid.Empty ? x.Series?.Id ?? Guid.Empty : x.SeriesId,
                            PremiereDate: x.PremiereDate))
                        .Where(x => x.SeriesId != Guid.Empty)
                        .ToList();
                    Guid[] seriesIds = episodes.Select(x => x.SeriesId)
                        .Distinct()
                        .Where(x => resolvedSeriesIds.Add(x))
                        .ToArray();

                    // Empty ItemIds means an unfiltered query, so never hydrate an empty page.
                    if (seriesIds.Length > 0)
                    {
                        IReadOnlyList<BaseItem> seriesItems = m_libraryManager.GetItemList(new InternalItemsQuery(user)
                        {
                            ItemIds = seriesIds,
                            GroupByPresentationUniqueKey = false,
                            DtoOptions = dtoOptions,
                            EnableTotalRecordCount = false
                        });

                        foreach (Series series in seriesItems.OfType<Series>())
                        {
                            visibleSeries[series.Id] = series;
                        }
                    }

                    foreach ((Guid seriesId, DateTime? premiereDate) in episodes)
                    {
                        if (!visibleSeries.ContainsKey(seriesId))
                        {
                            continue;
                        }

                        folderSeriesKeys.Add(user == null ? seriesId.ToString() : visibleSeries[seriesId].PresentationUniqueKey);
                        if (!latestPremiereDates.TryGetValue(seriesId, out DateTime? previousDate) || premiereDate > previousDate)
                        {
                            latestPremiereDates[seriesId] = premiereDate;
                        }
                    }

                    if (page.Items.Count < episodePageSize)
                    {
                        break;
                    }
                }
            }

            if (latestPremiereDates.Count == 0)
            {
                return new QueryResult<BaseItemDto>(Array.Empty<BaseItemDto>());
            }

            // Retain the final user-scoped presentation grouping across all folders and pages.
            ILookup<string?, Series> seriesByKey = m_libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                ItemIds = latestPremiereDates.Keys.ToArray(),
                DtoOptions = dtoOptions,
                EnableTotalRecordCount = false
            }).OfType<Series>().ToLookup(x => user == null ? x.Id.ToString() : x.PresentationUniqueKey);

            // Count and rank presentation groups, so physical versions do not consume row slots.
            // Equal dates retain encounter order, including the repository's null/empty key groups.
            BaseItem[] orderedSeries = latestPremiereDates
                .GroupBy(x => user == null ? x.Key.ToString() : visibleSeries[x.Key].PresentationUniqueKey)
                .OrderByDescending(x => x.Max(y => y.Value))
                .Select(x => seriesByKey[x.Key].FirstOrDefault())
                .Where(x => x != null)
                .Take(16)
                .Cast<BaseItem>()
                .ToArray();

            return new QueryResult<BaseItemDto>(Array.ConvertAll(orderedSeries,
                i => m_dtoService.GetBaseItemDto(i, dtoOptions, user)));
        }

        protected override LatestSectionBase CreateInstance()
        {
            return new LatestShowsSection(m_userViewManager, m_userManager, m_libraryManager, m_tvSeriesManager, m_dtoService, m_serviceProvider);
        }
    }
}
