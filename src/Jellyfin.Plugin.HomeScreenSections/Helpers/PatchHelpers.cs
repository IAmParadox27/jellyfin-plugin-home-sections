using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Channels;
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
    private static bool s_patched;

    public static void SetupPatches()
    {
        if (s_patched)
        {
            return;
        }
        
        HarmonyMethod streamyfinConfigurationPatch = new HarmonyMethod(typeof(PatchHelpers).GetMethod(nameof(PatchHelpers.Patch_Streamyfin_Configuration), BindingFlags.NonPublic | BindingFlags.Static));

        Type? streamyfinControllerType = AssemblyLoadContext.All.SelectMany(x => x.Assemblies)
            .FirstOrDefault(x => x.FullName?.Contains("Jellyfin.Plugin.Streamyfin", StringComparison.Ordinal) ?? false)?
            .GetTypes()
            .FirstOrDefault(x => string.Equals(x.Name, "StreamyfinController", StringComparison.Ordinal));

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

        if (__result is not ContentResult contentResult || contentResult.Content == null ||
            __instance is not ControllerBase controller)
        {
            return;
        }

        contentResult.Content = RewriteStreamyfinHomeSections(contentResult.Content, controller);
    }

    private static string RewriteStreamyfinHomeSections(string content, ControllerBase controller)
    {
        JObject parsedOutput = JObject.Parse(content);
        string? userIdString = controller.User.Claims
            .FirstOrDefault(x => x.Type.Equals("Jellyfin-UserId", StringComparison.OrdinalIgnoreCase))
            ?.Value;
        Guid userId = string.IsNullOrEmpty(userIdString) ? Guid.Empty : Guid.Parse(userIdString);

        HomeScreenSectionService hssService = HomeScreenSectionsPlugin.Instance.ServiceProvider
            .GetRequiredService<HomeScreenSectionService>();
        IReadOnlyList<HomeScreenSectionInfo> sections =
            hssService.MonitorLiveUpdatedSectionsForUser(userId, "en", 1) ?? Array.Empty<HomeScreenSectionInfo>();

        JArray? sectionsArr = parsedOutput.Value<JObject>("settings")
            ?.Value<JObject>("home")
            ?.Value<JObject>("value")
            ?.Value<JArray>("sections");

        if (sectionsArr != null)
        {
            ReplaceStreamyfinSections(sectionsArr, sections);
        }

        return parsedOutput.ToString();
    }

    private static void ReplaceStreamyfinSections(JArray sectionsArr, IReadOnlyList<HomeScreenSectionInfo> sections)
    {
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

        sectionsArr.Clear();
        foreach (HomeScreenSectionInfo info in sections)
        {
            if ((info.Section?.StartsWith("Discover", StringComparison.Ordinal) ?? false) ||
                (info.Section?.StartsWith("Upcoming", StringComparison.Ordinal) ?? false) ||
                string.Equals(info.Section, "MyMedia", StringComparison.Ordinal))
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
}