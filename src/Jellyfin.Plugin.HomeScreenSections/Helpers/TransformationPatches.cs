using System.Reflection;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.HomeScreenSections.Attributes;
using Jellyfin.Plugin.HomeScreenSections.Model;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;

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
                .Replace("{{this_hook}}", thisVariableName);

            // The hooks depend on the jellyfin-web bundle the server is actually serving, so key them off
            // the running server version rather than the version this build was compiled against. A build
            // targeting an older ABI (e.g. 10.11.11) still loads on newer servers (e.g. 12.x), and Jellyfin
            // will happily install that build after an upgrade, which previously injected the 10.11 hooks
            // into the 12.x web client and crashed the home screen ("Cannot read properties of undefined (reading 'tv')").
            string? jellyfinVersion = GetRunningJellyfinVersion();

            if (jellyfinVersion?.StartsWith("10.10.7") ?? false)
            {
                replacementText = replacementText.Replace("{{cardbuilder_hook}}", "h.default");
                replacementText = replacementText.Replace("{{appRouterParent_hook}}", "p");
                replacementText = replacementText.Replace("{{shapebuilder_hook}}", "y");
                replacementText = replacementText.Replace("{{layoutmanager_hook}}", "n"); // TODO: lookup the first "assigned" variable after `var`
            }
            else if (jellyfinVersion?.StartsWith("10.11") ?? false)
            {
                replacementText = replacementText.Replace("{{cardbuilder_hook}}", "u.default");
                replacementText = replacementText.Replace("{{appRouterParent_hook}}", "p");
                replacementText = replacementText.Replace("{{shapebuilder_hook}}", "y");
                replacementText = replacementText.Replace("{{layoutmanager_hook}}", "n"); // TODO: lookup the first "assigned" variable after `var`
            }
            else if (jellyfinVersion?.StartsWith("12.") ?? false)
            {
                replacementText = replacementText.Replace("{{cardbuilder_hook}}", "p.Ay");
                replacementText = replacementText.Replace("{{appRouterParent_hook}}", "T");
                replacementText = replacementText.Replace("{{shapebuilder_hook}}", "I");
                replacementText = replacementText.Replace("{{layoutmanager_hook}}", "r(46782)"); // TODO: lookup the first "assigned" variable after `var`
            }
            
            string regex = content.Contents.Replace(",loadSections:", $",loadSections:{replacementText},originalLoadSections:");

            return regex;
        }

        /// <summary>
        /// Gets the version of the Jellyfin server this plugin is running in. Jellyfin's own assemblies are
        /// resolved from the server at runtime, so their version is the server's version. Falls back to the
        /// version this build was compiled against if it can't be determined.
        /// </summary>
        private static string? GetRunningJellyfinVersion()
        {
            Version? serverVersion = typeof(IServerConfigurationManager).Assembly.GetName().Version;
            if (serverVersion != null && serverVersion.Major > 0)
            {
                return serverVersion.ToString();
            }

            return JellyfinVersionAttribute.GetVersion();
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