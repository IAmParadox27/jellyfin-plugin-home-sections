using System.Diagnostics;
using System.IO.Pipes;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.HomeScreenSections.Configuration;
using Jellyfin.Plugin.HomeScreenSections.Controllers;
using Jellyfin.Plugin.HomeScreenSections.Helpers;
using Jellyfin.Plugin.HomeScreenSections.JellyfinVersionSpecific;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.HomeScreenSections.Services
{
    public class StartupService : IScheduledTask
    {
        public string Name => "HomeScreenSections Startup";

        public string Key => "Jellyfin.Plugin.HomeScreenSections.Startup";
        
        public string Description => "Startup Service for HomeScreenSections";
        
        public string Category => "Startup Services";
        
        private readonly IServerApplicationHost m_serverApplicationHost;
        private readonly IApplicationPaths m_applicationPaths;
        private readonly ILogger<HomeScreenSectionsPlugin> m_logger;
        private readonly IHomeScreenManager m_homeScreenManager;

        public StartupService(IServerApplicationHost serverApplicationHost, IApplicationPaths applicationPaths, ILogger<HomeScreenSectionsPlugin> logger, IHomeScreenManager homeScreenManager)
        {
            m_serverApplicationHost = serverApplicationHost;
            m_applicationPaths = applicationPaths;
            m_logger = logger;
            m_homeScreenManager = homeScreenManager;
        }

        public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            PatchHelpers.SetupPatches();
            
            // Look through the web path and find the file that contains `",loadSections:`
            List<JObject> payloads = new List<JObject>();
            {
                JObject payload = new JObject();
                payload.Add("id", "e531b5a0-5493-42b0-b632-619e2d06db5c");
                payload.Add("fileNamePattern", "index.html");
                payload.Add("callbackAssembly", GetType().Assembly.FullName);
                payload.Add("callbackClass", typeof(TransformationPatches).FullName);
                payload.Add("callbackMethod", nameof(TransformationPatches.IndexHtml));
                payloads.Add(payload);
            }
            
            string[] allJsChunks = Directory.GetFiles(m_applicationPaths.WebPath, "*.chunk.js", SearchOption.AllDirectories);
            foreach (string jsChunk in allJsChunks)
            {
                if (File.ReadAllText(jsChunk).Contains(",loadSections:"))
                {
                    string fileName = Path.GetFileName(jsChunk);
                    Regex r = new Regex(@"([^.]+)\.([^.]+)\.chunk.js");
                    
                    JObject payload = new JObject();
                    payload.Add("id", "ea4045f3-6604-4ba4-9581-f91f96bbd2ae");
                    payload.Add("fileNamePattern", r.Match(fileName).Groups[1].Value + "\\.[^.]+\\.chunk\\.js");
                    payload.Add("callbackAssembly", GetType().Assembly.FullName);
                    payload.Add("callbackClass", typeof(TransformationPatches).FullName);
                    payload.Add("callbackMethod", nameof(TransformationPatches.LoadSections));
                    payloads.Add(payload);
                    break;
                }
            }
            
            Assembly? fileTransformationAssembly =
                AssemblyLoadContext.All.SelectMany(x => x.Assemblies).FirstOrDefault(x =>
                    x.FullName?.Contains(".FileTransformation") ?? false);

            if (fileTransformationAssembly != null)
            {
                Type? pluginInterfaceType = fileTransformationAssembly.GetType("Jellyfin.Plugin.FileTransformation.PluginInterface");

                if (pluginInterfaceType != null)
                {
                    foreach (JObject payload in payloads)
                    {
                        pluginInterfaceType.GetMethod("RegisterTransformation")?.Invoke(null, new object?[] { payload });
                    }
                }
            }

            foreach (SectionSettings section in HomeScreenSectionsPlugin.Instance.Configuration.SectionSettings)
            {
                // Calling this to perform migration of old configuration options if there are any left
                HomeScreenController.GetAdminConfigurationOptions(section.SectionId, m_homeScreenManager);
            }
        }

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => StartupServiceHelper.GetDefaultTriggers();
    }
}