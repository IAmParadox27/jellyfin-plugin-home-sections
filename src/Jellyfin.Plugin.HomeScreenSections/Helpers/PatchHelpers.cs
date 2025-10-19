using System.Reflection;
using System.Runtime.Loader;
using HarmonyLib;
using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Services;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.HomeScreenSections.Helpers;

public class PatchHelpers
{
    private static Harmony s_harmony = new Harmony("dev.iamparadox.jellyfin.hss");
    private static bool s_patched = false;

    public static void SetupPatches()
    {
        if (s_patched)
        {
            return;
        }
        
        HarmonyMethod streamyfinConfigurationPatch = new HarmonyMethod(typeof(PatchHelpers).GetMethod(nameof(PatchHelpers.Patch_Streamyfin_Configuration), BindingFlags.NonPublic | BindingFlags.Static));

        Type? streamyfinControllerType = AssemblyLoadContext.All.SelectMany(x => x.Assemblies)
            .FirstOrDefault(x => x.FullName?.Contains("Jellyfin.Plugin.Streamyfin") ?? false)?
            .GetTypes()
            .FirstOrDefault(x => x.Name == "StreamyfinController");

        // If the type couldn't be found the user probably doesn't have Streamyfin plugin, so there's nothing
        // we can do about that.
        if (streamyfinControllerType != null)
        {
            s_harmony.Patch(streamyfinControllerType.GetMethod("getConfig"),
                postfix: streamyfinConfigurationPatch);
            s_patched = true;
        }
    }

    private static void Patch_Streamyfin_Configuration(ref object __result, object __instance)
    {
        if (!HomeScreenSectionsPlugin.Instance.Configuration.OverrideStreamyfinHome)
        {
            return;
        }
        
        JObject sectionTemplate = new JObject
        {
            { "title", "" },
            { "orientation", "horizontal" },
            {
                "custom", new JObject()
                {
                    { "endpoint", "" },
                    { "query", new JObject() }
                }
            }
        };
        
        if (__result is ContentResult contentResult && contentResult.Content != null &&
            __instance is ControllerBase controller)
        {
            JObject parsedOutput = JObject.Parse(contentResult.Content);
            
            // Mutate and set back
            // Find the user ID from the authorization
            string? userIdString = controller.User.Claims.FirstOrDefault(x => x.Type.Equals("Jellyfin-UserId", StringComparison.OrdinalIgnoreCase))?.Value;
            Guid userId = string.IsNullOrEmpty(userIdString) ? Guid.Empty : Guid.Parse(userIdString);
            
            HomeScreenSectionService hssService = HomeScreenSectionsPlugin.Instance.ServiceProvider.GetRequiredService<HomeScreenSectionService>();
            List<HomeScreenSectionInfo> sections = hssService.GetSectionsForUser(userId, null);

            JArray? sectionsArr = parsedOutput.Value<JObject>("settings")?.Value<JObject>("home")?.Value<JObject>("value")?.Value<JArray>("sections");

            if (sectionsArr != null)
            {
                sectionsArr.Clear();

                foreach (HomeScreenSectionInfo info in sections)
                {
                    if ((info.Section?.StartsWith("Discover") ?? false) || 
                        (info.Section?.StartsWith("Upcoming") ?? false) ||
                        info.Section == "MyMedia")
                    {
                        continue;
                    }
                    
                    JObject sectionObj = (sectionTemplate.DeepClone() as JObject)!;

                    sectionObj["title"] = info.DisplayText;
                    sectionObj["orientation"] = info.ViewMode == SectionViewMode.Portrait ? "vertical" : "horizontal";
                    sectionObj["custom"]!["endpoint"] = $"/HomeScreen/Section/{info.Section}";
                    sectionObj["custom"]!["query"] = new JObject()
                    {
                        { "additionalData", info.AdditionalData },
                        { "language", "en" }
                    };
                    
                    sectionsArr.Add(sectionObj);
                }
            }

            contentResult.Content = parsedOutput.ToString();
        }
    }
}