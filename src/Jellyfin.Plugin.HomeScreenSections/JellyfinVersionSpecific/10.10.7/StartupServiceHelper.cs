using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.HomeScreenSections.JellyfinVersionSpecific
{
    public static class StartupServiceHelper
    {
        public static IEnumerable<TaskTriggerInfo> GetStartupTrigger()
        {
            yield return new TaskTriggerInfo()
            {
                Type = TaskTriggerInfo.TriggerStartup
            };
        }

        public static IEnumerable<TaskTriggerInfo> GetDailyTrigger(TimeSpan timeOfDay)
        {
            yield return new TaskTriggerInfo()
            {
                Type = TaskTriggerInfo.TriggerDaily,
                TimeOfDayTicks = timeOfDay.Ticks
            };
        }
    }
}