using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.HomeScreenSections.Library
{
    public interface IHomeScreenManager
    {
        void RegisterResultsDelegate<T>() where T : IHomeScreenSection;

        void RegisterResultsDelegate<T>(T handler) where T : IHomeScreenSection;
        
        IEnumerable<IHomeScreenSection> GetSectionTypes();

        QueryResult<BaseItemDto> InvokeResultsDelegate(string key, HomeScreenSectionPayload payload, IQueryCollection queryCollection);

        bool GetUserFeatureEnabled(Guid userId);

        void SetUserFeatureEnabled(Guid userId, bool enabled);

        ModularHomeUserSettings? GetUserSettings(Guid userId);

        bool UpdateUserSettings(Guid userId, ModularHomeUserSettings userSettings);
    }

    public interface IHomeScreenSection
    {
        public string? Section { get; }

        public string? DisplayText { get; set; }

        public int? Limit { get; }

        public string? Route { get; }

        public string? AdditionalData { get; set; }

        public object? OriginalPayload { get; }
        
        public QueryResult<BaseItemDto> GetResults(HomeScreenSectionPayload payload, IQueryCollection queryCollection);

        public IHomeScreenSection CreateInstance(Guid? userId, IEnumerable<IHomeScreenSection>? otherInstances = null);

        public HomeScreenSectionInfo GetInfo();
    }

    public class HomeScreenSectionInfo
    {
        public string? Section { get; set; }

        public string? DisplayText { get; set; }

        public int Limit { get; set; } = 1;

        public string? Route { get; set; }

        public string? AdditionalData { get; set; }
        
        public string? ContainerClass { get; set; }

        public SectionViewMode? ViewMode { get; set; } = null;

        public bool DisplayTitleText { get; set; } = true;
        
        public bool ShowDetailsMenu { get; set; } = true;

        public object? OriginalPayload { get; set; }
        
        public bool AllowViewModeChange { get; set; } = true;

        public bool AllowHideWatched { get; set; } = false;
    }

    public class ModularHomeUserSettings
    {
        public Guid UserId { get; set; }

        public List<string> EnabledSections { get; set; } = new List<string>();
    }

    public static class HomeScreenSectionExtensions
    {
        public static HomeScreenSectionInfo AsInfo(this IHomeScreenSection section)
        {
            return section.GetInfo();
        }
    }
}
