using System.Collections.Concurrent;
using Jellyfin.Extensions;
using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Helpers;
using Jellyfin.Plugin.HomeScreenSections.Library;
using MediaBrowser.Controller;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.HomeScreenSections.Services
{
    public class HomeScreenSectionService
    {
        private readonly IDisplayPreferencesManager m_displayPreferencesManager;
        private readonly IHomeScreenManager m_homeScreenManager;
        private readonly ILogger<HomeScreenSectionsPlugin> m_logger;
        private readonly ITranslationManager m_translationManager;
    
        private Dictionary<Guid, UserHomeSections> m_userSections = new Dictionary<Guid, UserHomeSections>();
        
        public HomeScreenSectionService(IDisplayPreferencesManager displayPreferencesManager,
            IHomeScreenManager homeScreenManager, ILogger<HomeScreenSectionsPlugin> logger, 
            ITranslationManager translationManager)
        {
            m_displayPreferencesManager = displayPreferencesManager;
            m_homeScreenManager = homeScreenManager;
            m_logger = logger;
            m_translationManager = translationManager;
        }
    
        public List<HomeScreenSectionInfo> GetSectionsForUser(Guid userId, string? language, int page = 1, int? pageSize = null, Guid? pageHash = null)
        {
            if (m_userSections.TryGetValue(userId, out UserHomeSections? userSections) && userSections.PageHash == pageHash && pageSize != null && page > 1)
            {
                return userSections.Sections.Skip((page - 1) * pageSize ?? 0).Take(pageSize.Value).ToList();
            }
            
            ModularHomeUserSettings? settings = m_homeScreenManager.GetUserSettings(userId);

            List<IHomeScreenSection> sectionTypes = m_homeScreenManager.GetSectionTypes().Where(x => settings?.EnabledSections.Contains(x.Section ?? string.Empty) ?? false).ToList();

            List<(IHomeScreenSection Section, int ConfiguredOrder)> sectionInstances = new List<(IHomeScreenSection, int)>();

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
        
            List<HomeScreenSectionInfo> sections = sectionInstances.Where(x => x.Section != null).Select(x =>
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

            if (pageHash != null)
            {
                m_userSections[userId] = new UserHomeSections
                {
                    PageHash = pageHash.Value,
                    Sections = sections
                };
            }

            return sections.Take(pageSize ?? sections.Count).ToList();
        }
    }

    public class UserHomeSections
    {
        public Guid PageHash { get; set; }
        public List<HomeScreenSectionInfo> Sections { get; set; } = new List<HomeScreenSectionInfo>();
    }
}
