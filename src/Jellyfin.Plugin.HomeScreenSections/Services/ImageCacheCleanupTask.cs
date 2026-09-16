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
        
        private readonly ImageCacheService m_imageCacheService;
        private readonly ILogger<ImageCacheCleanupTask> m_logger;

        public ImageCacheCleanupTask(ImageCacheService imageCacheService, ILogger<ImageCacheCleanupTask> logger)
        {
            m_imageCacheService = imageCacheService;
            m_logger = logger;
        }

        public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            try
            {
                m_logger.LogInformation("Starting image cache cleanup");
                progress?.Report(0);
                
                m_imageCacheService.ClearExpiredCache();
                
                progress?.Report(100);
                m_logger.LogInformation("Image cache cleanup completed");
                
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "Error during image cache cleanup");
                throw;
            }
        }

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => StartupServiceHelper.GetDailyTrigger(TimeSpan.FromHours(3));
    }
}
