using Jellyfin.Extensions;
using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Helpers;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections.RecentlyAdded
{
    public class RecentlyAddedInLibrarySection : RecentlyAddedSectionBase
    {
        private sealed record RecentItem(
            BaseItem Item,
            DateTime SortDate);
        
        /// <inheritdoc/>
        public override string? Section => "RecentlyAddedInLibrary";

        /// <inheritdoc/>
        public override string? DisplayText { get; set; } = "Recently Added In Library";

        /// <inheritdoc/>
        public override string? Route => null;

        /// <inheritdoc/>
        public override string? AdditionalData { get; set; } = null;

        protected override BaseItemKind SectionItemKind => BaseItemKind.Folder; // Not used in this section

        protected override CollectionType CollectionType => CollectionType.folders; // Not used in this section
        
        protected override CollectionTypeOptions CollectionTypeOptions => CollectionTypeOptions.mixed; // Not used in this section

        protected override string? LibraryId => null;

        protected override SectionViewMode DefaultViewMode => SectionViewMode.Landscape;

        public override int? Limit => 999;

        public override TranslationMetadata? TranslationMetadata { get; protected set; } = null;

        public RecentlyAddedInLibrarySection(IUserViewManager userViewManager,
            IUserManager userManager,
            ILibraryManager libraryManager,
            IDtoService dtoService,
            IServiceProvider serviceProvider) : base(userViewManager, userManager, libraryManager, dtoService, serviceProvider)
        {
        }

        protected override IEnumerable<PluginConfigurationOption> GetPluginConfigurationOptionsInternal()
        {
            yield return PluginConfigurationHelper.CreateCheckbox("groupEpisodes", "Group Episodes to Seasons/Series",
                "Do you want to group newly added episodes into seasons/series? (Note: this will make the section slower to load)", 
                "AdminRecentlyAddedInLibraryGroupEpisodes", false, true);
        }
        
        public override QueryResult<BaseItemDto> GetResults(HomeScreenSectionPayload payload, IQueryCollection queryCollection)
        {
            User? user = m_userManager.GetUserById(payload.UserId);

            DtoOptions dtoOptions = new DtoOptions
            {
                Fields = new List<ItemFields>
                {
                    ItemFields.PrimaryImageAspectRatio,
                    ItemFields.Path,
                    ItemFields.DateCreated
                },
                ImageTypeLimit = 1,
                ImageTypes = new List<ImageType>
                {
                    ImageType.Primary,
                    ImageType.Thumb,
                    ImageType.Backdrop,
                }
            };
            
            Guid folderId = payload.AdditionalData != null ? Guid.Parse(payload.AdditionalData) : Guid.Empty;
            
            IEnumerable<BaseItem> recentlyAddedItems = Enumerable.Empty<BaseItem>();
            if (folderId != Guid.Empty)
            {
                Folder? folder = m_libraryManager.GetItemById<Folder>(folderId, user); // Ensure the folder exists and is accessible by the user
                
                if (folder != null)
                {
                    VirtualFolderInfo[] folders = m_libraryManager.GetVirtualFolders()
                        .FilterToUserPermitted(m_libraryManager, user);

                    PluginConfiguration? config = HomeScreenSectionsPlugin.Instance?.Configuration;
                    SectionSettings? sectionSettings = config?.SectionSettings.FirstOrDefault(x => x.SectionId == Section);
                    // If HideWatchedItems is enabled for this section, set isPlayed to false to hide watched items; otherwise, include all.
                    bool? isPlayed = sectionSettings?.HideWatchedItems == true ? false : null;

                    recentlyAddedItems = GetItems(user, dtoOptions, folders, isPlayed, payload, folder);
                }
            }
            
            return new QueryResult<BaseItemDto>(Array.ConvertAll(recentlyAddedItems.ToArray(),
                i => m_dtoService.GetBaseItemDto(i, dtoOptions, user)));
        }

        protected override IEnumerable<IHomeScreenSection> CreateInstancesInternal(Guid? userId)
        {
            User? user = m_userManager.GetUserById(userId ?? Guid.Empty);

            BaseItemDto? originalPayload = null;
            
            Folder[] itemFolders = m_libraryManager.GetUserRootFolder()
                .GetChildren(user, true)
                .OfType<Folder>()
                .ToArray();

            foreach (Folder folder in itemFolders)
            {
                DtoOptions dtoOptions = new DtoOptions();
                dtoOptions.Fields =
                    [..dtoOptions.Fields, ItemFields.PrimaryImageAspectRatio, ItemFields.DisplayPreferencesId];

                originalPayload = Array.ConvertAll(new[] { folder }, i => m_dtoService.GetBaseItemDto(i, dtoOptions, user)).First();
                
                RecentlyAddedInLibrarySection instance = (ActivatorUtilities.CreateInstance(m_serviceProvider, GetType(), m_userViewManager, m_userManager, m_libraryManager, m_dtoService) as RecentlyAddedInLibrarySection)!;
                
                instance.AdditionalData = folder.Id.ToString();
                instance.DisplayText = DisplayText;
                instance.OriginalPayload = originalPayload;
                instance.TranslationMetadata = new TranslationMetadata()
                {
                    Type = TranslationType.Pattern,
                    AdditionalContent = folder.Name
                };
                
                yield return instance;
            }
        }
        
        protected override IEnumerable<BaseItem> GetItems(User? user, DtoOptions dtoOptions, VirtualFolderInfo[] folders, 
            bool? isPlayed, HomeScreenSectionPayload payload, Folder? folderOverride = null)
        {
            const int c_resultLimit = 16;
            const int c_batchSize = 16;
            
            // Default behaviour is to get the 16 most recently added items from each library that matches, then order that by date created and take 16.
            // The reason we do this is to ensure that we always get 16 items, even if there is only 1 library that matches our type.
            return folders.SelectMany(x =>
            {
                BaseItem item = folderOverride ?? m_libraryManager.GetParentItem(Guid.Parse(x.ItemId), user?.Id);

                if (item is not Folder folder)
                {
                    folder = m_libraryManager.GetUserRootFolder();
                }

                bool groupEpisodes = HomeScreenSectionPayload.GetEffectiveBoolConfig(Section ?? string.Empty, "groupEpisodes");
                if (groupEpisodes)
                {
                    List<BaseItem> rawItems = new List<BaseItem>();
                    bool finishedLibrary = false;

                    while (!finishedLibrary)
                    {
                        IReadOnlyList<BaseItem> items = folder.GetItems(new InternalItemsQuery(user)
                        {
                            ExcludeItemTypes = new[] { BaseItemKind.Folder },
                            MediaTypes = new[] { MediaType.Video, MediaType.Audio },
                            DtoOptions = dtoOptions,
                            IsPlayed = isPlayed,
                            OrderBy = new[] { (ItemSortBy.DateCreated, SortOrder.Descending) },
                            Limit = c_batchSize,
                            ExcludeItemIds = rawItems.Select(i => i.Id).ToArray(),
                            IsMissing = false,
                            Recursive = true,
                            ParentId = folder.Id
                        }).Items;

                        rawItems.AddRange(items);

                        List<RecentItem> collapsed = CollapseEpisodes(rawItems, user, dtoOptions);

                        if (collapsed.Count >= c_resultLimit)
                        {
                            return collapsed
                                .OrderByDescending(y => y.SortDate)
                                .Take(c_resultLimit)
                                .Select(y => y.Item)
                                .ToArray();
                        }

                        if (items.Count < c_batchSize)
                        {
                            finishedLibrary = true;
                        }
                    }

                    return CollapseEpisodes(rawItems, user, dtoOptions)
                        .OrderByDescending(y => y.SortDate)
                        .Take(c_resultLimit)
                        .Select(y => y.Item)
                        .ToArray();
                }
                else
                {
                    return folder.GetItems(new InternalItemsQuery(user)
                    {
                        ExcludeItemTypes = new[] { BaseItemKind.Folder },
                        MediaTypes = new[] { MediaType.Video, MediaType.Audio },
                        DtoOptions = dtoOptions,
                        IsPlayed = isPlayed,
                        OrderBy = new[] { (ItemSortBy.DateCreated, SortOrder.Descending) },
                        Limit = c_resultLimit,
                        IsMissing = false,
                        Recursive = true,
                        ParentId = folder.Id
                    }).Items;
                }
            }).DistinctBy(x => x.Id)
            .OrderByDescending(x => GetSortDateForItem(x, user, dtoOptions))
            .Take(16);
        }
        
        private List<RecentItem> CollapseEpisodes(
            IEnumerable<BaseItem> items,
            User? user,
            DtoOptions dtoOptions)
        {
            List<RecentItem> normalItems = new List<RecentItem>();

            IEnumerable<IGrouping<Guid, Episode>> episodes = items
                .OfType<Episode>()
                .Where(x => x.SeriesId != Guid.Empty && x.SeasonId != Guid.Empty)
                .GroupBy(x => x.SeriesId);

            foreach (BaseItem item in items.Where(x => x is not Episode))
            {
                normalItems.Add(new RecentItem(item, GetSortDateForItem(item, user, dtoOptions)));
            }

            foreach (IGrouping<Guid, Episode> seriesGroup in episodes)
            {
                Episode[] episodeArray = seriesGroup.ToArray();
                DateTime newestDate = episodeArray.Max(x => GetSortDateForItem(x, user, dtoOptions));

                BaseItem? displayItem = null;
                if (episodeArray.Length == 1)
                {
                    displayItem = episodeArray[0];
                }
                else
                {
                    Guid[] seasonIds = episodeArray
                        .Select(x => x.SeasonId)
                        .Distinct()
                        .ToArray();

                    displayItem = m_libraryManager.GetItemById(seasonIds.Length == 1 ? seasonIds[0] : seriesGroup.Key);
                }

                if (displayItem is not null)
                {
                    normalItems.Add(new RecentItem(displayItem, newestDate));
                }
            }

            return normalItems;
        }
    }
}
