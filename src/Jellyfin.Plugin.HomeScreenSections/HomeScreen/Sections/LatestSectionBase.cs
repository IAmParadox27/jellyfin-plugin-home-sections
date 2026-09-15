using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Helpers;
using Jellyfin.Plugin.HomeScreenSections.Services;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.TV;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections
{
    public abstract class LatestSectionBase : IHomeScreenSection
    {
        public abstract string? Section { get; }
        public abstract string? DisplayText { get; set; }
        public virtual int? Limit => 1;
        public virtual string? Route { get; } = null;
        public virtual string? AdditionalData { get; set; }
        public virtual object? OriginalPayload { get; set; } = null;
        public abstract SectionViewMode DefaultViewMode { get; }
        
        protected abstract BaseItemKind SectionItemKind { get; }
        
        protected abstract CollectionType CollectionType { get; }
        
        protected abstract string? LibraryId { get; }
        
        protected abstract CollectionTypeOptions CollectionTypeOptions { get; }
        
        private static readonly System.Reflection.PropertyInfo? m_presentationUniqueKeys = typeof(InternalItemsQuery).GetProperty("PresentationUniqueKeys");

        private readonly Func<string[], int, IReadOnlyList<(Guid Id, string? Key, DateTime? PremiereDate)>>? m_moviePresentationVersions;

        protected readonly IUserViewManager m_userViewManager;
        protected readonly IUserManager m_userManager;
        protected readonly ILibraryManager m_libraryManager;
        protected readonly IDtoService m_dtoService;
        protected readonly IServiceProvider m_serviceProvider;
        
        public LatestSectionBase(IUserViewManager userViewManager,
            IUserManager userManager,
            ILibraryManager libraryManager,
            IDtoService dtoService,
            IServiceProvider serviceProvider)
        {
            m_userViewManager = userViewManager;
            m_userManager = userManager;
            m_libraryManager = libraryManager;
            m_dtoService = dtoService;
            
            m_serviceProvider = serviceProvider;

            System.Reflection.MethodInfo? movieVersionsMethod = libraryManager.GetType().GetMethod(
                "GetMoviePresentationVersions", new[] { typeof(string[]), typeof(int) });
            if (movieVersionsMethod?.ReturnType == typeof(IReadOnlyList<(Guid, string?, DateTime?)>))
            {
                m_moviePresentationVersions = movieVersionsMethod.CreateDelegate<Func<string[], int,
                    IReadOnlyList<(Guid Id, string? Key, DateTime? PremiereDate)>>>(libraryManager);
            }
        }

        public virtual QueryResult<BaseItemDto> GetResults(HomeScreenSectionPayload payload, IQueryCollection queryCollection)
        {
            DtoOptions? dtoOptions = new DtoOptions
            {
                Fields = new List<ItemFields>
                {
                    ItemFields.PrimaryImageAspectRatio,
                    ItemFields.Path
                },
                EnableImages = true
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
                .Where(x => x.CollectionType == CollectionTypeOptions || x.IsMixedFolder(m_libraryManager))
                .FilterToUserPermitted(m_libraryManager, user);

            DateTime currentDate = DateTime.Now;
            BaseItem[] selectedItems = folders.SelectMany(x =>
            {
                BaseItem item = m_libraryManager.GetParentItem(Guid.Parse(x.ItemId), user?.Id);

                if (item is not Folder folder)
                {
                    folder = m_libraryManager.GetUserRootFolder();
                }

                InternalItemsQuery query = new InternalItemsQuery(user)
                {
                    IncludeItemTypes = new[] { SectionItemKind },
                    Limit = 16,
                    OrderBy = new[] { (ItemSortBy.PremiereDate, SortOrder.Descending) },
                    IsPlayed = isPlayed,
                    ParentId = Guid.Parse(x.ItemId),
                    Recursive = true,
                    MaxPremiereDate = currentDate,
                    MinPremiereDate = new DateTime(1887, 1, 1),
                    EnableTotalRecordCount = false
                };

                if (SectionItemKind == BaseItemKind.Movie && (isPlayed.HasValue
                    || (user != null && HomeScreenSectionsPlugin.Instance?.ServerConfigurationManager.Configuration.EnableGroupingMoviesIntoCollections != true)))
                {
                    query.MinPremiereDate = currentDate.AddMonths(-1);
#if NET10_0_OR_GREATER
                    query.GroupByPresentationUniqueKey = true;
#else
                    query.GroupByPresentationUniqueKey = false;
#endif
                    query.Limit = 17;
                    IReadOnlyList<BaseItem> recentItems = folder.GetItems(query).Items;
                    query.MinPremiereDate = new DateTime(1887, 1, 1);

                    // A date window is authoritative only when each selected presentation
                    // has exactly one physical item of this type, including other folders.
                    bool verified = query.CollapseBoxSetItems != true && recentItems.Count >= 16
                        && (recentItems.Count == 16 || recentItems[15].PremiereDate > recentItems[16].PremiereDate);
                    string[] keys = new string[16];
                    (Guid Id, string Key, DateTime? PremiereDate)[] selectedVersions = new (Guid, string, DateTime?)[16];
                    for (int index = 0; verified && index < 16; index++)
                    {
                        BaseItem recentItem = recentItems[index];
#if !NET10_0_OR_GREATER
                        if (user != null && !recentItem.IsVisible(user))
                        {
                            verified = false;
                            break;
                        }
#endif
                        selectedVersions[index] = (recentItem.Id, recentItem.PresentationUniqueKey, recentItem.PremiereDate);
                        keys[index] = selectedVersions[index].Key;
                        if (string.IsNullOrWhiteSpace(keys[index]))
                        {
                            verified = false;
                            break;
                        }

                        for (int previous = 0; previous < index; previous++)
                        {
                            if (keys[previous] == keys[index] || selectedVersions[previous].PremiereDate == selectedVersions[index].PremiereDate)
                            {
                                verified = false;
                                break;
                            }
                        }
                    }

                    Func<string[], int, IReadOnlyList<(Guid Id, string? Key, DateTime? PremiereDate)>>? movieVersions = m_moviePresentationVersions;
                    if (verified && movieVersions == null)
                    {
                        movieVersions = HomeScreenMovieVersionProjection.Create(m_libraryManager, m_serviceProvider);
                    }

                    if (verified && movieVersions != null)
                    {
                        verified = VerifyMoviePresentationVersions(keys, selectedVersions, movieVersions);
                    }
                    else if (verified && m_presentationUniqueKeys?.PropertyType == typeof(string[]) && m_presentationUniqueKeys.CanWrite)
                    {
                        InternalItemsQuery versionQuery = new InternalItemsQuery
                        {
                            IncludeItemTypes = new[] { SectionItemKind },
                            GroupByPresentationUniqueKey = false,
                            Limit = 17,
                            EnableTotalRecordCount = false,
                            SkipDeserialization = true,
                            DtoOptions = new DtoOptions(false) { EnableImages = false, EnableUserData = false },
#if NET10_0_OR_GREATER
                            IncludeOwnedItems = true
#endif
                        };
                        m_presentationUniqueKeys.SetValue(versionQuery, keys);
                        IReadOnlyList<BaseItem> versions = m_libraryManager.GetItemList(versionQuery);
                        verified = versions.Count == 16;
                        bool[] matched = new bool[16];
                        foreach (BaseItem version in versions)
                        {
                            if (!verified)
                            {
                                break;
                            }

                            bool found = false;
                            for (int index = 0; index < 16; index++)
                            {
                                if (!matched[index] && selectedVersions[index].Id == version.Id
                                    && selectedVersions[index].Key == version.PresentationUniqueKey
                                    && selectedVersions[index].PremiereDate == version.PremiereDate)
                                {
                                    matched[index] = true;
                                    found = true;
                                    break;
                                }
                            }

                            verified = found;
                        }
                    }
                    else if (verified)
                    {
                        foreach (BaseItem recentItem in recentItems.Take(16))
                        {
                            IReadOnlyList<Guid> versionIds = m_libraryManager.GetItemIds(new InternalItemsQuery
                            {
                                PresentationUniqueKey = recentItem.PresentationUniqueKey,
                                GroupByPresentationUniqueKey = false,
                                Limit = 2,
#if NET10_0_OR_GREATER
                                IncludeOwnedItems = true
#endif
                            });
                            if (versionIds.Count != 1 || versionIds[0] != recentItem.Id)
                            {
                                verified = false;
                                break;
                            }
                        }
                    }

                    query.GroupByPresentationUniqueKey = true;
                    query.Limit = 16;
                    if (verified && (isPlayed.HasValue
                        || (user != null && HomeScreenSectionsPlugin.Instance?.ServerConfigurationManager.Configuration.EnableGroupingMoviesIntoCollections != true)))
                    {
                        return recentItems.Take(16).ToArray();
                    }
                }

#if NET10_0_OR_GREATER
                return folder.GetItems(query).Items;
#else
                if (user == null)
                {
                    return folder.GetItems(query).Items;
                }

                List<BaseItem> visibleItems = new List<BaseItem>();
                HashSet<Guid> seenItems = new HashSet<Guid>();
                int startIndex = 0;
                while (visibleItems.Count < 16)
                {
                    query.StartIndex = startIndex;
                    IReadOnlyList<BaseItem> page = folder.GetItems(query).Items;
                    foreach (BaseItem candidate in page)
                    {
                        if (seenItems.Add(candidate.Id) && candidate.IsVisible(user))
                        {
                            visibleItems.Add(candidate);
                            if (visibleItems.Count == 16)
                            {
                                break;
                            }
                        }
                    }

                    if (page.Count < 16)
                    {
                        break;
                    }

                    startIndex += page.Count;
                }

                return visibleItems;
#endif
            })
            .DistinctBy(x => x.Id)
            .OrderByDescending(x => x.PremiereDate)
            .Take(16)
            .ToArray();

            return new QueryResult<BaseItemDto>(Array.ConvertAll(selectedItems,
                i => m_dtoService.GetBaseItemDto(i, dtoOptions, user)));
        }
        
        private bool VerifyMoviePresentationVersions(string[] keys, (Guid Id, string Key, DateTime? PremiereDate)[] selectedVersions,
            Func<string[], int, IReadOnlyList<(Guid Id, string? Key, DateTime? PremiereDate)>> movieVersions)
        {
            IReadOnlyList<(Guid Id, string? Key, DateTime? PremiereDate)> versions = movieVersions(keys, 17);
            if (versions.Count != 16)
            {
                return false;
            }

            bool[] matched = new bool[16];
            foreach ((Guid Id, string? Key, DateTime? PremiereDate) version in versions)
            {
                bool found = false;
                for (int index = 0; index < 16; index++)
                {
                    if (!matched[index] && selectedVersions[index].Id == version.Id
                        && selectedVersions[index].Key == version.Key
                        && selectedVersions[index].PremiereDate == version.PremiereDate)
                    {
                        matched[index] = true;
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    return false;
                }
            }

            return true;
        }

        public IEnumerable<IHomeScreenSection> CreateInstances(Guid? userId, int instanceCount)
        {
            User? user = m_userManager.GetUserById(userId ?? Guid.Empty);

            BaseItemDto? originalPayload = null;
            
            // Get only collection folders for the section type that the user can access
            var libraryFolders = m_libraryManager.GetUserRootFolder()
                .GetChildren(user, true)
                .OfType<Folder>()
                .Where(x => (x as ICollectionFolder)?.CollectionType == CollectionType)
                .ToArray();
            
            // Check if there's a configured default library, otherwise use first available
            var folder = !string.IsNullOrEmpty(LibraryId)
                ? libraryFolders.FirstOrDefault(x => x.Id.ToString() == LibraryId)
                : null;
            
            // Fall back to first movies library if no configured library found
            folder ??= libraryFolders.FirstOrDefault();
            
            if (folder != null)
            {
                DtoOptions dtoOptions = new DtoOptions();
                dtoOptions.Fields =
                    [..dtoOptions.Fields, ItemFields.PrimaryImageAspectRatio, ItemFields.DisplayPreferencesId];
                
                originalPayload = Array.ConvertAll(new[] { folder }, i => m_dtoService.GetBaseItemDto(i, dtoOptions, user)).First();
            }

            LatestSectionBase sectionBase = CreateInstance();
            sectionBase.DisplayText = DisplayText;
            sectionBase.AdditionalData = AdditionalData;
            sectionBase.OriginalPayload = originalPayload;

            yield return sectionBase;
        }

        public HomeScreenSectionInfo GetInfo()
        {
            return new HomeScreenSectionInfo
            {
                Section = Section,
                DisplayText = DisplayText,
                AdditionalData = AdditionalData,
                Route = Route,
                Limit = Limit ?? 1,
                OriginalPayload = OriginalPayload,
                ViewMode = DefaultViewMode,
                AllowHideWatched = true,
                PluginConfigurationOptions = (this as IHomeScreenSection).GetPluginConfigurationOptions().ToArray()
            };
        }
        
        protected abstract LatestSectionBase CreateInstance();
    }
}