using Jellyfin.Extensions;
using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Helpers;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using Jellyfin.Plugin.HomeScreenSections.Services;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections;

public class DecadeSection : IHomeScreenSection
{
    private const int MinItemsForDecade = 5;

    public string? Section => "Decade";
    public string? DisplayText { get; set; } = "Decade";
    public string? AdminDescription => "A \"Best of the 1990s\"-style row, weighted towards decades the user actually watches. Each user sees a different decade.";
    public int? Limit => 3;
    public string? Route => null;
    public string? AdditionalData { get; set; }
    public object? OriginalPayload => null;
    public TranslationMetadata? TranslationMetadata { get; private set; }

    private readonly IUserManager m_userManager;
    private readonly ILibraryManager m_libraryManager;
    private readonly IUserDataManager m_userDataManager;
    private readonly IDtoService m_dtoService;
    private readonly PerUserComputedStatsCache m_statsCache;

    public DecadeSection(IUserManager userManager, ILibraryManager libraryManager, IUserDataManager userDataManager, IDtoService dtoService, PerUserComputedStatsCache statsCache)
    {
        m_userManager = userManager;
        m_libraryManager = libraryManager;
        m_userDataManager = userDataManager;
        m_dtoService = dtoService;
        m_statsCache = statsCache;
    }

    public QueryResult<BaseItemDto> GetResults(HomeScreenSectionPayload payload, IQueryCollection queryCollection)
    {
        if (!int.TryParse(payload.AdditionalData, out int decadeStart))
        {
            return new QueryResult<BaseItemDto>();
        }

        User? user = m_userManager.GetUserById(payload.UserId);

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
            .Where(x => x.CollectionType == CollectionTypeOptions.movies || x.CollectionType == CollectionTypeOptions.tvshows || x.IsMixedFolder(m_libraryManager))
            .FilterToUserPermitted(m_libraryManager, user);

        int[] decadeYears = Enumerable.Range(decadeStart, 10).ToArray();

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
                EnableTotalRecordCount = false,
                IsPlayed = isPlayed,
                DtoOptions = dtoOptions,
                Years = decadeYears
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

        VirtualFolderInfo[] folders = m_libraryManager.GetVirtualFolders()
            .FilterToUserPermitted(m_libraryManager, user);

        Guid[] folderIds = folders
            .Select(x => Guid.Parse(x.ItemId ?? Guid.Empty.ToString()))
            .Where(x => x != Guid.Empty)
            .ToArray();

        if (folderIds.Length == 0)
        {
            yield break;
        }

        // Expensive to redo every load - cache it per user instead.
        if (!m_statsCache.TryGetOrCompute(
            user.Id,
            "decade-stats",
            TimeSpan.FromHours(24),
            () => ComputeDecadeStats(user, folderIds),
            out (Dictionary<int, int> Scores, int[] Availability) decadeStats))
        {
            yield break;
        }

        Dictionary<int, int> decadeScores = decadeStats.Scores;
        int[] decadeAvailability = decadeStats.Availability;

        if (decadeAvailability.Length == 0)
        {
            yield break;
        }

        Random rnd = new Random();
        List<int> pickedDecades = new List<int>();

        while (pickedDecades.Count < instanceCount)
        {
            int[] availableDecades = decadeAvailability.Where(x => !pickedDecades.Contains(x)).ToArray();

            if (availableDecades.Length == 0)
            {
                break;
            }

            (int Decade, int Score)[] scored = availableDecades.Select(d => (Decade: d, Score: decadeScores.GetValueOrDefault(d, 0))).ToArray();
            int totalScore = scored.Sum(x => x.Score);

            int selectedDecade;
            if (totalScore > 0)
            {
                int randomScore = rnd.Next(0, totalScore);
                selectedDecade = scored.Last().Decade;

                foreach ((int Decade, int Score) decadeScore in scored)
                {
                    randomScore -= decadeScore.Score;

                    if (randomScore < 0)
                    {
                        selectedDecade = decadeScore.Decade;
                        break;
                    }
                }
            }
            else
            {
                selectedDecade = availableDecades[rnd.Next(0, availableDecades.Length)];
            }

            pickedDecades.Add(selectedDecade);

            yield return new DecadeSection(m_userManager, m_libraryManager, m_userDataManager, m_dtoService, m_statsCache)
            {
                AdditionalData = selectedDecade.ToString(),
                DisplayText = $"Best of the {selectedDecade}s",
                TranslationMetadata = new TranslationMetadata
                {
                    Type = TranslationType.Pattern,
                    AdditionalContent = $"{selectedDecade}s",
                    TranslateAdditionalContent = false
                }
            };
        }
    }

    private (Dictionary<int, int> Scores, int[] Availability) ComputeDecadeStats(User user, Guid[] folderIds)
    {
        // Score decades by how much the user has actually watched from them.
        List<BaseItem> playedItems = folderIds.SelectMany(folderId =>
        {
            BaseItem? item = m_libraryManager.GetParentItem(folderId, user.Id);

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

        Dictionary<int, int> decadeScores = playedItems
            .Where(x => x.ProductionYear.HasValue)
            .GroupBy(x => (x.ProductionYear!.Value / 10) * 10)
            .ToDictionary(g => g.Key, g => g.Count());

        // Only offer decades that actually have enough content available, regardless of watch history.
        List<BaseItem> availableItems = folderIds.SelectMany(folderId =>
        {
            BaseItem? item = m_libraryManager.GetParentItem(folderId, user.Id);

            if (item is not Folder folder)
            {
                folder = m_libraryManager.GetUserRootFolder();
            }

            return folder.GetItems(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
                Recursive = true,
                ParentId = folderId,
                DtoOptions = new DtoOptions { Fields = Array.Empty<ItemFields>(), EnableImages = false }
            }).Items;
        }).ToList();

        int[] decadeAvailability = availableItems
            .Where(x => x.ProductionYear.HasValue)
            .GroupBy(x => (x.ProductionYear!.Value / 10) * 10)
            .Where(g => g.Count() >= MinItemsForDecade)
            .Select(g => g.Key)
            .ToArray();

        return (decadeScores, decadeAvailability);
    }

    public HomeScreenSectionInfo GetInfo()
    {
        return new HomeScreenSectionInfo
        {
            Section = Section,
            AdminTranslationKey = "DecadeSectionName",
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
