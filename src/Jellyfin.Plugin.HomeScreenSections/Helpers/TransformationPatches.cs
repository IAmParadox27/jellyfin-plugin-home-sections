using System.Reflection;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.HomeScreenSections.Attributes;
using Jellyfin.Plugin.HomeScreenSections.Model;
using MediaBrowser.Common.Net;

namespace Jellyfin.Plugin.HomeScreenSections.Helpers
{
    public static class TransformationPatches
    {
        private static readonly Regex s_variableFind = new(@"var\s+(?<name>[a-zA-Z][^=]*)=", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromMilliseconds(250));

        private static readonly Lazy<string> s_loadSectionsTemplate = new(() =>
        {
            using Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream($"{typeof(HomeScreenSectionsPlugin).Namespace}.Controllers.loadSections.js")!;
            using TextReader r = new StreamReader(s);
            return r.ReadToEnd();
        });

        public static string LoadSections(PatchRequestPayload content)
        {
            // replace `",loadSections:` with itself followed by our function followed by `",originalLoadSections:`
            string[] parts = content.Contents!.Split(",loadSections:", StringSplitOptions.RemoveEmptyEntries);
            string thisVariableName = s_variableFind.Matches(parts[0]).Last().Groups["name"].Value;
            string replacementText = s_loadSectionsTemplate.Value
                .Replace("{{this_hook}}", thisVariableName)
                .Replace("{{layoutmanager_hook}}", "n"); // NOTE: lookup the first "assigned" variable after `var`

            if (JellyfinVersionAttribute.GetVersion()?.StartsWith("10.10.7", StringComparison.Ordinal) ?? false)
            {
                replacementText = replacementText.Replace("{{cardbuilder_hook}}", "h");
            }
            else if (JellyfinVersionAttribute.GetVersion()?.StartsWith("10.11", StringComparison.Ordinal) ?? false)
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