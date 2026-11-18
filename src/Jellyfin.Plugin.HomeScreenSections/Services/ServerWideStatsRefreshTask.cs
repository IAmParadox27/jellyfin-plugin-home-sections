using Jellyfin.Plugin.HomeScreenSections.Helpers;
using Jellyfin.Plugin.HomeScreenSections.JellyfinVersionSpecific;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.HomeScreenSections.Services
{
    public class ServerWideStatsRefreshTask : IScheduledTask
    {
        public string Name => "HSS Server-Wide Stats Refresh";

        public string Key => "Jellyfin.Plugin.HomeScreenSections.ServerWideStatsRefresh";

        public string Description => "Precomputes server-wide Most Played and Recently Played rankings (aggregated across every user account) so individual page loads don't have to recompute them.";

        public string Category => "Maintenance";

        private readonly ServerWideStatsCache m_cache;
        private readonly IUserManager m_userManager;
        private readonly ILibraryManager m_libraryManager;
        private readonly IUserDataManager m_userDataManager;
        private readonly ILogger<ServerWideStatsRefreshTask> m_logger;

        public ServerWideStatsRefreshTask(
            ServerWideStatsCache cache,
            IUserManager userManager,
            ILibraryManager libraryManager,
            IUserDataManager userDataManager,
            ILogger<ServerWideStatsRefreshTask> logger)
        {
            m_cache = cache;
            m_userManager = userManager;
            m_libraryManager = libraryManager;
            m_userDataManager = userDataManager;
            m_logger = logger;
        }

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => StartupServiceHelper.GetStartupTrigger()
            .Concat(StartupServiceHelper.GetDailyTrigger(TimeSpan.FromHours(4)));

        public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            try
            {
                var users = m_userManager.GetAllUsers().ToList();
                var mostPlayedScores = new Dictionary<Guid, double>();
                var recentlyPlayedMax = new Dictionary<Guid, DateTime>();

                int userIndex = 0;
                foreach (var user in users)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    VirtualFolderInfo[] folders = m_libraryManager.GetVirtualFolders()
                        .Where(x => x.CollectionType == CollectionTypeOptions.movies || x.CollectionType == CollectionTypeOptions.tvshows || x.IsMixedFolder(m_libraryManager))
                        .FilterToUserPermitted(m_libraryManager, user);

                    var playedMovies = folders.SelectMany(x =>
                    {
                        var item = m_libraryManager.GetParentItem(Guid.Parse(x.ItemId), user.Id);
                        if (item is not Folder folder)
                        {
                            folder = m_libraryManager.GetUserRootFolder();
                        }

                        return folder.GetItems(new InternalItemsQuery(user)
                        {
                            ParentId = Guid.Parse(x.ItemId ?? Guid.Empty.ToString()),
                            IncludeItemTypes = new[] { BaseItemKind.Movie },
                            IsPlayed = true,
                            Recursive = true,
                            DtoOptions = new DtoOptions { Fields = Array.Empty<ItemFields>(), EnableImages = false }
                        }).Items;
                    }).ToList();

                    foreach (var movie in playedMovies)
                    {
                        var userData = m_userDataManager.GetUserData(user, movie);
                        int playCount = userData?.PlayCount ?? 0;
                        if (playCount <= 0)
                        {
                            continue;
                        }

                        mostPlayedScores[movie.Id] = mostPlayedScores.GetValueOrDefault(movie.Id) + playCount;

                        if (userData?.LastPlayedDate is DateTime moviePlayed)
                        {
                            if (!recentlyPlayedMax.TryGetValue(movie.Id, out var existing) || moviePlayed > existing)
                            {
                                recentlyPlayedMax[movie.Id] = moviePlayed;
                            }
                        }
                    }

                    // Average per episode, summed across users.
                    var playedEpisodes = folders.SelectMany(x =>
                    {
                        var item = m_libraryManager.GetParentItem(Guid.Parse(x.ItemId), user.Id);
                        if (item is not Folder folder)
                        {
                            folder = m_libraryManager.GetUserRootFolder();
                        }

                        return folder.GetItems(new InternalItemsQuery(user)
                        {
                            ParentId = Guid.Parse(x.ItemId ?? Guid.Empty.ToString()),
                            IncludeItemTypes = new[] { BaseItemKind.Episode },
                            IsPlayed = true,
                            Recursive = true,
                            DtoOptions = new DtoOptions { Fields = Array.Empty<ItemFields>(), EnableImages = false }
                        }).Items;
                    }).OfType<Episode>().ToList();

                    foreach (var episode in playedEpisodes)
                    {
                        var userData = m_userDataManager.GetUserData(user, episode);
                        if (userData?.LastPlayedDate is DateTime episodePlayed)
                        {
                            if (!recentlyPlayedMax.TryGetValue(episode.Id, out var existing) || episodePlayed > existing)
                            {
                                recentlyPlayedMax[episode.Id] = episodePlayed;
                            }
                        }
                    }

                    foreach (var seriesGroup in playedEpisodes.Where(e => e.Series != null).GroupBy(e => e.Series!.Id))
                    {
                        double averageForUser = seriesGroup.Average(e => m_userDataManager.GetUserData(user, e)?.PlayCount ?? 0);
                        mostPlayedScores[seriesGroup.Key] = mostPlayedScores.GetValueOrDefault(seriesGroup.Key) + averageForUser;
                    }

                    userIndex++;
                    progress.Report(users.Count == 0 ? 100 : (double)userIndex / users.Count * 100);
                }

                m_cache.MostPlayedItemIds = mostPlayedScores
                    .OrderByDescending(x => x.Value)
                    .Take(50)
                    .Select(x => x.Key)
                    .ToList();

                m_cache.RecentlyPlayedItemIds = recentlyPlayedMax
                    .OrderByDescending(x => x.Value)
                    .Take(50)
                    .Select(x => x.Key)
                    .ToList();

                m_cache.LastComputed = DateTime.UtcNow;

                m_logger.LogInformation(
                    "Server-wide stats refresh complete: {MostPlayedCount} most-played, {RecentlyPlayedCount} recently-played items cached",
                    m_cache.MostPlayedItemIds.Count,
                    m_cache.RecentlyPlayedItemIds.Count);
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "Error computing server-wide stats");
            }

            return Task.CompletedTask;
        }
    }
}
