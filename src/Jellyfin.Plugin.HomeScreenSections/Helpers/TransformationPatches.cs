using System.Reflection;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.HomeScreenSections.Attributes;
using Jellyfin.Plugin.HomeScreenSections.Model;
using MediaBrowser.Common.Net;

namespace Jellyfin.Plugin.HomeScreenSections.Helpers
{
    public static class TransformationPatches
    {
        public static string LoadSections(PatchRequestPayload content)
        {
            // replace `",loadSections:` with itself followed by our function followed by `",originalLoadSections:`
            Stream replacementStream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"{typeof(HomeScreenSectionsPlugin).Namespace}.Controllers.loadSections.js")!;
            using TextReader replacementTextReader = new StreamReader(replacementStream);
        
            string[] parts = content.Contents!.Split(",loadSections:", StringSplitOptions.RemoveEmptyEntries);
            Regex variableFind = new Regex(@"var\s+([a-zA-Z][^=]*)=");
            string thisVariableName = variableFind.Matches(parts[0]).Last().Groups[1].Value;
            string replacementText = replacementTextReader.ReadToEnd()
                .Replace("{{this_hook}}", thisVariableName)
                .Replace("{{layoutmanager_hook}}", "n"); // TODO: lookup the first "assigned" variable after `var`

            if (JellyfinVersionAttribute.GetVersion()?.StartsWith("10.10.7") ?? false)
            {
                replacementText = replacementText.Replace("{{cardbuilder_hook}}", "h");
            }
            else if (JellyfinVersionAttribute.GetVersion()?.StartsWith("10.11.0") ?? false)
            {
                replacementText = replacementText.Replace("{{cardbuilder_hook}}", "u");
            }
            
            string regex = content.Contents.Replace(",loadSections:", $",loadSections:{replacementText},originalLoadSections:");

            return regex;
        }

        public static string IndexHtml(PatchRequestPayload content)
        {
            NetworkConfiguration networkConfiguration = HomeScreenSectionsPlugin.Instance.ServerConfigurationManager.GetNetworkConfiguration();
            var pluginConfig = HomeScreenSectionsPlugin.Instance.Configuration;

            string rootPath = "";
            if (!string.IsNullOrWhiteSpace(networkConfiguration.BaseUrl))
            {
                rootPath = $"/{networkConfiguration.BaseUrl.TrimStart('/').Trim()}";
            }
            
            string pluginVersion = HomeScreenSectionsPlugin.Instance.GetCurrentPluginVersion();

            string cacheParam;
            if (pluginConfig.DeveloperMode)
            {
                // Developer mode: Add timestamp
                cacheParam = $"?v={pluginVersion}&t={DateTimeOffset.UtcNow.Ticks}";
            }
            else
            {
                // Normal mode: Use version + cache bust counter
                cacheParam = $"?v={pluginVersion}&c={pluginConfig.CacheBustCounter}";
            }

            string replacementText0 = $"<link rel=\"stylesheet\" href=\"{rootPath}/HomeScreen/home-screen-sections.css{cacheParam}\" />";
            string replacementText1 = $"<script type=\"text/javascript\" plugin=\"Jellyfin.Plugin.HomeScreenSections\" src=\"{rootPath}/HomeScreen/home-screen-sections.js{cacheParam}\" defer></script>";
            
            return content.Contents!
                .Replace("</head>", $"{replacementText0}</head>")
                .Replace("</body>", $"{replacementText1}</body>");
        }
    }
}