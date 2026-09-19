using Jellyfin.Extensions;
using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Helpers;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using Jellyfin.Plugin.HomeScreenSections.Services;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections;

public class StudioSection : IHomeScreenSection
{
    public string? Section => "Studio";
    public string? DisplayText { get; set; } = "By Studio/Network";
    public string? AdminDescription => "More content from a studio or network the user watches a lot of (e.g. \"More from Studio - Warner Bros. Pictures\" or \"More from Network - HBO\"). Each user sees a different studio, chosen by their own watch history.";
    public int? Limit => 3;
    public string? Route { get; private set; }
    public string? AdditionalData { get; set; }
    public object? OriginalPayload { get; private set; }
    public TranslationMetadata? TranslationMetadata { get; private set; }

    private readonly IUserManager m_userManager;
    private readonly ILibraryManager m_libraryManager;
    private readonly IUserDataManager m_userDataManager;
    private readonly IDtoService m_dtoService;
    private readonly PerUserComputedStatsCache m_statsCache;

    public StudioSection(IUserManager userManager, ILibraryManager libraryManager, IUserDataManager userDataManager, IDtoService dtoService, PerUserComputedStatsCache statsCache)
    {
        m_userManager = userManager;
        m_libraryManager = libraryManager;
        m_userDataManager = userDataManager;
        m_dtoService = dtoService;
        m_statsCache = statsCache;
    }

    public QueryResult<BaseItemDto> GetResults(HomeScreenSectionPayload payload, IQueryCollection queryCollection)
    {
        if (string.IsNullOrWhiteSpace(payload.AdditionalData))
        {
            return new QueryResult<BaseItemDto>();
        }

        User? user = m_userManager.GetUserById(payload.UserId);

        Studio? studio = m_libraryManager.GetStudio(payload.AdditionalData);

        if (studio == null)
        {
            return new QueryResult<BaseItemDto>();
        }

        DtoOptions? dtoOptions = new DtoOptions
        {
            Fields = new[]
            {
                ItemFields.PrimaryImageAspectRatio,
                ItemFields.MediaSourceCount
            }
        };

        PluginConfiguration? config = HomeScreenSectionsPlugin.Instance?.Configuration;
        SectionSettings? sectionSettings = config?.SectionSettings.FirstOrDefault(x => x.SectionId == Section);
        bool? isPlayed = sectionSettings?.HideWatchedItems == true ? false : null;

        VirtualFolderInfo[] folders = m_libraryManager.GetVirtualFolders()
            .FilterToUserPermitted(m_libraryManager, user);

        List<BaseItem> items = folders.SelectMany(x =>
        {
            BaseItem? item = m_libraryManager.GetParentItem(Guid.Parse(x.ItemId), user?.Id);

            if (item is not Folder folder)
            {
                folder = m_libraryManager.GetUserRootFolder();
            }

            return folder.GetItems(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
                OrderBy = new[] { (ItemSortBy.Random, SortOrder.Descending) },
                ParentId = Guid.Parse(x.ItemId ?? Guid.Empty.ToString()),
                Recursive = true,
                Limit = 24,
                IsPlayed = isPlayed,
                DtoOptions = dtoOptions,
                StudioIds = new[] { studio.Id }
            }).Items;
        }).GroupBy(x => x.Id).Select(x => x.First()).ToList();

        items.Shuffle();

        return new QueryResult<BaseItemDto>(m_dtoService.GetBaseItemDtos(items.Take(16).ToArray(), dtoOptions, user));
    }

    public IEnumerable<IHomeScreenSection> CreateInstances(Guid? userId, int instanceCount)
    {
        User? user = userId is null || userId.Value.Equals(default)
            ? null
            : m_userManager.GetUserById(userId.Value);

        if (user == null)
        {
            yield break;
        }

        // Expensive to redo every load - cache it per user instead.
        if (!m_statsCache.TryGetOrCompute(
            user.Id,
            "studio-scores",
            TimeSpan.FromHours(24),
            () => GetStudiosForUser(user),
            out (string Studio, bool IsNetwork, int Score)[] userStudioScores))
        {
            yield break;
        }

        if (userStudioScores.Length == 0)
        {
            yield break;
        }

        Random rnd = new Random();
        List<string> pickedStudios = new List<string>();

        while (pickedStudios.Count < instanceCount)
        {
            (string Studio, bool IsNetwork, int Score)[] availableStudios = userStudioScores.Where(x => !pickedStudios.Contains(x.Studio)).ToArray();

            if (availableStudios.Length == 0)
            {
                break;
            }

            int totalScore = availableStudios.Sum(x => x.Score);
            (string Studio, bool IsNetwork, int Score)? selectedStudio = null;

            if (totalScore > 0)
            {
                int randomScore = rnd.Next(0, totalScore);

                foreach ((string Studio, bool IsNetwork, int Score) studioScore in availableStudios)
                {
                    randomScore -= studioScore.Score;

                    if (randomScore < 0)
                    {
                        selectedStudio = studioScore;
                        break;
                    }
                }

                selectedStudio ??= availableStudios.Last();
            }
            else
            {
                selectedStudio = availableStudios[rnd.Next(0, availableStudios.Length)];
            }

            pickedStudios.Add(selectedStudio.Value.Studio);

            Studio? studioItem = m_libraryManager.GetStudio(selectedStudio.Value.Studio);

            string kind = selectedStudio.Value.IsNetwork ? "Network" : "Studio";

            yield return new StudioSection(m_userManager, m_libraryManager, m_userDataManager, m_dtoService, m_statsCache)
            {
                AdditionalData = selectedStudio.Value.Studio,
                Route = studioItem != null ? "originalpayload" : null,
                OriginalPayload = studioItem != null ? m_dtoService.GetBaseItemDto(studioItem, new DtoOptions(), user) : null,
                DisplayText = $"More from {kind} - {selectedStudio.Value.Studio}"
            };
        }
    }

    private (string Studio, bool IsNetwork, int Score)[] GetStudiosForUser(User user)
    {
        const int likedOrFavouriteScore = 125;
        const int recentlyWatchedScore = 50;
        const int scorePerPlay = 1;

        VirtualFolderInfo[] folders = m_libraryManager.GetVirtualFolders()
            .FilterToUserPermitted(m_libraryManager, user);

        Guid[] folderIds = folders
            .Select(x => Guid.Parse(x.ItemId ?? Guid.Empty.ToString()))
            .Where(x => x != Guid.Empty)
            .ToArray();

        if (folderIds.Length == 0)
        {
            return Array.Empty<(string, bool, int)>();
        }

        List<BaseItem> allPlayedItems = folderIds.SelectMany(folderId =>
        {
            BaseItem? item = m_libraryManager.GetParentItem(folderId, user?.Id);

            if (item is not Folder folder)
            {
                folder = m_libraryManager.GetUserRootFolder();
            }

            return folder.GetItems(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
                Recursive = true,
                IsPlayed = true,
                ParentId = folderId,
            }).Items;
        }).ToList();

        Dictionary<Guid, UserItemData?> userDataCache = new Dictionary<Guid, UserItemData?>();
        foreach (BaseItem mediaItem in allPlayedItems)
        {
            userDataCache[mediaItem.Id] = m_userDataManager.GetUserData(user, mediaItem);
        }

        Dictionary<string, int> playCountByStudio = allPlayedItems
            .Where(mediaItem => mediaItem.Studios.Length > 0)
            .Select(mediaItem => new
            {
                Studio = mediaItem.Studios[0],
                PlayCount = userDataCache.TryGetValue(mediaItem.Id, out UserItemData? ud) ? ud?.PlayCount ?? 0 : 0
            })
            .GroupBy(x => x.Studio)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.PlayCount) * scorePerPlay);

        DateTime cutoffDate = DateTime.Today.Subtract(TimeSpan.FromDays(14));
        Dictionary<string, int> recentlyWatchedByStudio = allPlayedItems
            .Where(mediaItem =>
            {
                if (userDataCache.TryGetValue(mediaItem.Id, out UserItemData? ud) && ud != null)
                {
                    return (ud.LastPlayedDate ?? DateTime.MinValue) > cutoffDate;
                }

                return false;
            })
            .Where(mediaItem => mediaItem.Studios.Length > 0)
            .Select(mediaItem => mediaItem.Studios[0])
            .GroupBy(studio => studio)
            .ToDictionary(g => g.Key, g => g.Count() * recentlyWatchedScore);

        List<BaseItem> likedOrFavoritedItems = folderIds.SelectMany(folderId =>
        {
            BaseItem? item = m_libraryManager.GetParentItem(folderId, user?.Id);

            if (item is not Folder folder)
            {
                folder = m_libraryManager.GetUserRootFolder();
            }

            return folder.GetItems(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
                Recursive = true,
                IsFavoriteOrLiked = true,
                User = user,
                ParentId = folderId,
            }).Items;
        }).ToList();

        Dictionary<string, int> likedByStudio = likedOrFavoritedItems
            .Where(mediaItem => mediaItem.Studios.Length > 0)
            .Select(mediaItem => mediaItem.Studios[0])
            .GroupBy(studio => studio)
            .ToDictionary(g => g.Key, g => g.Count() * likedOrFavouriteScore);

        IEnumerable<string> allStudioNames = playCountByStudio.Keys
            .Concat(recentlyWatchedByStudio.Keys)
            .Concat(likedByStudio.Keys)
            .Distinct();

        // Jellyfin stores a series' network in Studios, so a name that mostly came from series is a network.
        Dictionary<string, int> seriesCountByStudio = allPlayedItems
            .Concat(likedOrFavoritedItems)
            .Where(mediaItem => mediaItem.Studios.Length > 0)
            .GroupBy(mediaItem => mediaItem.Studios[0])
            .ToDictionary(g => g.Key, g => g.Count(mediaItem => mediaItem is Series) * 2 - g.Count());

        (string Studio, bool IsNetwork, int Score)[] result = allStudioNames.Select(studio =>
        {
            int score = 0;
            if (playCountByStudio.TryGetValue(studio, out int playScore))
            {
                score += playScore;
            }

            if (recentlyWatchedByStudio.TryGetValue(studio, out int recentScore))
            {
                score += recentScore;
            }

            if (likedByStudio.TryGetValue(studio, out int likedScore))
            {
                score += likedScore;
            }

            return (Studio: studio, IsNetwork: seriesCountByStudio.GetValueOrDefault(studio) > 0, Score: score);
        }).ToArray();

        return result;
    }

    public HomeScreenSectionInfo GetInfo()
    {
        return new HomeScreenSectionInfo
        {
            Section = Section,
            AdminTranslationKey = "StudioSectionName",
            DisplayText = DisplayText,
            AdditionalData = AdditionalData,
            Route = Route,
            Limit = Limit ?? 1,
            OriginalPayload = OriginalPayload,
            ViewMode = SectionViewMode.Landscape,
            AllowHideWatched = true,
            PluginConfigurationOptions = (this as IHomeScreenSection).GetPluginConfigurationOptions().ToArray()
        };
    }
}
