using Jellyfin.Plugin.HomeScreenSections.JellyfinVersionSpecific;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.HomeScreenSections.Tests.JellyfinVersionSpecific;

public class StartupServiceHelperTests
{
    [Fact]
    public void GetStartupTrigger_yields_single_startup_trigger()
    {
        List<TaskTriggerInfo> triggers = [.. StartupServiceHelper.GetStartupTrigger()];

        Assert.Single(triggers);
        Assert.Equal(TaskTriggerInfoType.StartupTrigger, triggers[0].Type);
    }

    [Fact]
    public void GetDailyTrigger_yields_single_daily_trigger_with_requested_time()
    {
        TimeSpan timeOfDay = new TimeSpan(3, 0, 0);

        List<TaskTriggerInfo> triggers = [.. StartupServiceHelper.GetDailyTrigger(timeOfDay)];

        Assert.Single(triggers);
        Assert.Equal(TaskTriggerInfoType.DailyTrigger, triggers[0].Type);
        Assert.Equal((long?)timeOfDay.Ticks, triggers[0].TimeOfDayTicks);
    }
}
