using System.Collections.Concurrent;
using Jellyfin.Extensions;
using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Helpers;
using Jellyfin.Plugin.HomeScreenSections.Library;
using MediaBrowser.Controller;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.HomeScreenSections.Services;

public class HomeScreenSectionService
{
    private readonly IDisplayPreferencesManager m_displayPreferencesManager;
    private readonly IHomeScreenManager m_homeScreenManager;
    private readonly ILogger<HomeScreenSectionsPlugin> m_logger;
    private readonly ITranslationManager m_translationManager;

    public HomeScreenSectionService(IDisplayPreferencesManager displayPreferencesManager,
        IHomeScreenManager homeScreenManager, ILogger<HomeScreenSectionsPlugin> logger, 
        ITranslationManager translationManager)
    {
        m_displayPreferencesManager = displayPreferencesManager;
        m_homeScreenManager = homeScreenManager;
        m_logger = logger;
        m_translationManager = translationManager;
    }
    
    public List<HomeScreenSectionInfo> GetSectionsForUser(Guid userId, string? language)
    {
        // string displayPreferencesId = "usersettings";
        // Guid itemId = displayPreferencesId.GetMD5();
        //
        // DisplayPreferences displayPreferences = m_displayPreferencesManager.GetDisplayPreferences(userId, itemId, "emby");
        ModularHomeUserSettings? settings = m_homeScreenManager.GetUserSettings(userId);

        List<IHomeScreenSection> sectionTypes = m_homeScreenManager.GetSectionTypes().Where(x => settings?.EnabledSections.Contains(x.Section ?? string.Empty) ?? false).ToList();

        List<(IHomeScreenSection Section, int ConfiguredOrder)> sectionInstances = new List<(IHomeScreenSection, int)>();

        // List<string> homeSectionOrderTypes = new List<string>();
        // if (HomeScreenSectionsPlugin.Instance.Configuration.AllowUserOverride)
        // {
        //     foreach (HomeSection section in displayPreferences.HomeSections.OrderBy(x => x.Order))
        //     {
        //         switch (section.Type)
        //         {
        //             case HomeSectionType.SmallLibraryTiles:
        //                 homeSectionOrderTypes.Add("MyMedia");
        //                 break;
        //             case HomeSectionType.Resume:
        //                 homeSectionOrderTypes.Add("ContinueWatching");
        //                 break;
        //             case HomeSectionType.LatestMedia:
        //                 homeSectionOrderTypes.Add("LatestMovies");
        //                 homeSectionOrderTypes.Add("LatestShows");
        //                 break;
        //             case HomeSectionType.NextUp:
        //                 homeSectionOrderTypes.Add("NextUp");
        //                 break;
        //         }
        //     }
        // }

        // foreach (string type in homeSectionOrderTypes)
        // {
        //     IHomeScreenSection? sectionType = sectionTypes.FirstOrDefault(x => x.Section == type);
        //
        //     if (sectionType != null)
        //     {
        //         if (sectionType.Limit > 1)
        //         {
        //             SectionSettings? sectionSettings = HomeScreenSectionsPlugin.Instance.Configuration.SectionSettings.FirstOrDefault(x =>
        //                 x.SectionId == sectionType.Section);
        //
        //             Random rnd = new Random();
        //             int instanceCount = rnd.Next(sectionSettings?.LowerLimit ?? 0, sectionSettings?.UpperLimit ?? sectionType.Limit ?? 1);
        //
        //             for (int i = 0; i < instanceCount; ++i)
        //             {
        //                 sectionInstances.Add(sectionType.CreateInstance(userId, sectionInstances.Where(x => x.GetType() == sectionType.GetType())));
        //             }
        //         }
        //         else if (sectionType.Limit == 1)
        //         {
        //             sectionInstances.Add(sectionType.CreateInstance(userId));
        //         }
        //     }
        // }
        //
        // sectionTypes.RemoveAll(x => homeSectionOrderTypes.Contains(x.Section ?? string.Empty));

        IEnumerable<IGrouping<int, SectionSettings>> groupedOrderedSections = HomeScreenSectionsPlugin.Instance.Configuration.SectionSettings
            .OrderBy(x => x.OrderIndex)
            .GroupBy(x => x.OrderIndex);

        ConcurrentDictionary<int, List<IHomeScreenSection>> groupedSections = new ConcurrentDictionary<int, List<IHomeScreenSection>>();
        Parallel.ForEach(groupedOrderedSections, orderedSections =>
        {
            ConcurrentBag<IHomeScreenSection?> tmpPluginSections = new ConcurrentBag<IHomeScreenSection?>(); // we want these randomly distributed among each other.

            Parallel.ForEach(orderedSections, sectionSettings =>
            {
                IHomeScreenSection? sectionType =
                    sectionTypes.FirstOrDefault(x => x.Section == sectionSettings.SectionId);

                if (sectionType != null)
                {
                    int instanceCount = 1;
                    if (sectionType.Limit > 1)
                    {
                        Random rnd = new Random();
                        instanceCount = rnd.Next(sectionSettings.LowerLimit, sectionSettings.UpperLimit);
                    }

                    try
                    {
                        IEnumerable<IHomeScreenSection> instances = sectionType.CreateInstances(userId, instanceCount);

                        foreach (IHomeScreenSection sectionInstance in instances)
                        {
                            tmpPluginSections.Add(sectionInstance);
                        }
                    }
                    catch (Exception e)
                    {
                        // Adding an error log here to stop issues like #128 from completely breaking the home screen.
                        // Whatever this section is won't work, but the rest of the home screen will still work.
                        m_logger.LogError(e, $"An error occurred while creating section instances for user '{userId}' and section '{sectionType.Section}'.");
                    }
                }
            });

            List<IHomeScreenSection> sectionList = tmpPluginSections.Where(x => x != null).Select(x => x!).ToList();
            sectionList.Shuffle();

            groupedSections.TryAdd(orderedSections.Key, sectionList);
        });

        foreach (int key in groupedSections.Keys.OrderBy(x => x))
        {
            sectionInstances.AddRange(groupedSections[key].Select(x => (x, key)));
        }
        
        return sectionInstances.Where(x => x.Section != null).Select(x =>
        {
            HomeScreenSectionInfo info = x.Section.AsInfo();

            info.OrderIndex = x.ConfiguredOrder;
            info.ViewMode = HomeScreenSectionsPlugin.Instance.Configuration.SectionSettings.FirstOrDefault(y => y.SectionId == info.Section)?.ViewMode ?? info.ViewMode ?? SectionViewMode.Landscape;
            
            if (info.DisplayText != null)
            {
                // Always fallback to "en" if there's no language provided.
                string? translatedResult = m_translationManager.Translate(info.Section!, language?.Trim() ?? "en", info.DisplayText, x.Section.TranslationMetadata);

                info.DisplayText = translatedResult;
            }
            
            return info;
        }).ToList();
    }
}