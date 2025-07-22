using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Library;

namespace Jellyfin.Plugin.HomeScreenSections.Model.Dto
{
    
    
    
    public class HomeScreenSectionPayload
    {
        
        
        
        public Guid UserId { get; set; }

        
        
        
        
        public string? AdditionalData { get; set; }

        /// <summary>
        /// User-specific settings for modular home sections.
        /// </summary>
        public ModularHomeUserSettings? UserSettings { get; set; }
        
        /// <summary>
        /// Item IDs that have already been recommended by other sections, used for duplicate prevention.
        /// </summary>
        public HashSet<Guid>? AlreadyRecommendedIds { get; set; }
        
        /// <summary>
        /// Determines the effective setting for a section based on admin configuration and user preferences.
        /// </summary>
        /// <param name="sectionId">The section ID to check.</param>
        /// <param name="overrideItem">The type of setting to check.</param>
        /// <returns>True or false based on the effective setting.</returns>
        private bool GetEffectiveSetting(string sectionId, UserOverrideItem overrideItem)
        {
            if (string.IsNullOrEmpty(sectionId))
                return true;
                
            var adminConfig = HomeScreenSectionsPlugin.Instance?.Configuration;
            var sectionSettings = adminConfig?.SectionSettings?.FirstOrDefault(s => s.SectionId == sectionId);
            
            if (sectionSettings != null)
            {
                // Check if admin allows user override for this setting
                var overrideSetting = sectionSettings.UserOverrideSettings?.FirstOrDefault(s => s.Item == overrideItem);
                bool userCanOverride = overrideSetting?.AllowUserOverride ?? true;
                
                if (userCanOverride && UserSettings != null)
                {
                    // Use user's preference if they can override
                    var userSectionSettings = UserSettings.GetSectionSettings(sectionId);
                    return overrideItem switch
                    {
                        UserOverrideItem.ShowPlayedItems => userSectionSettings.ShowPlayedItems,
                        UserOverrideItem.PreventDuplicates => userSectionSettings.PreventDuplicates,
                        _ => true
                    };
                }
                else
                {
                    // Use admin's default setting if user can't override
                    return overrideItem switch
                    {
                        UserOverrideItem.ShowPlayedItems => sectionSettings.ShowPlayedItems,
                        UserOverrideItem.PreventDuplicates => sectionSettings.PreventDuplicates,
                        _ => true
                    };
                }
            }
            
            // Default to true if no configuration found
            return true;
        }
        
        /// <summary>
        /// Determines the effective "Show played items" setting for a section,
        /// considering both admin permissions and user preferences.
        /// </summary>
        /// <param name="sectionId">The section ID to check.</param>
        /// <returns>True if played items should be shown, false otherwise.</returns>
        public bool GetEffectiveShowPlayedItems(string sectionId)
        {
            return GetEffectiveSetting(sectionId, UserOverrideItem.ShowPlayedItems);
        }
        
        /// <summary>
        /// Determines the effective "Prevent duplicates" setting for a section,
        /// considering both admin permissions and user preferences.
        /// </summary>
        /// <param name="sectionId">The section ID to check.</param>
        /// <returns>True if duplicates should be prevented, false otherwise.</returns>
        public bool GetEffectivePreventDuplicates(string sectionId)
        {
            return GetEffectiveSetting(sectionId, UserOverrideItem.PreventDuplicates);
        }
    }
}
