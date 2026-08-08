using Jellyfin.Plugin.HomeScreenSections.JellyfinVersionSpecific;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.HomeScreenSections.Services
{
    public class ImageCacheCleanupTask : IScheduledTask
    {
        public string Name => "Home Sections Image Cache Cleanup";

        public string Key => "Jellyfin.Plugin.HomeScreenSections.ImageCacheCleanup";
        
        public string Description => "Cleans up expired cached images from the Home Screen Sections plugin";
        
        public string Category => "Maintenance";
        
        private readonly ImageCacheService _imageCacheService;
        private readonly ILogger<ImageCacheCleanupTask> _logger;

        public ImageCacheCleanupTask(ImageCacheService imageCacheService, ILogger<ImageCacheCleanupTask> logger)
        {
            _imageCacheService = imageCacheService;
            _logger = logger;
        }

        public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            try
            {
                PluginLog.ImageCacheCleanupStarted(_logger);
                progress?.Report(0);
                
                _imageCacheService.ClearExpiredCache();
                
                progress?.Report(100);
                PluginLog.ImageCacheCleanupCompleted(_logger);
                
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                PluginLog.ImageCacheCleanupError(_logger, ex);
                throw;
            }
        }

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => StartupServiceHelper.GetDailyTrigger(TimeSpan.FromHours(3));
    }
}
