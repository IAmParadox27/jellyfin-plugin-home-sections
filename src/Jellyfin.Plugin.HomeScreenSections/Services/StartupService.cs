using System.Diagnostics;
using System.IO.Pipes;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.HomeScreenSections.Helpers;
using Jellyfin.Plugin.HomeScreenSections.JellyfinVersionSpecific;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
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
        private readonly ILibraryManager m_libraryManager;
        private readonly IServerConfigurationManager m_serverConfigurationManager;
        private readonly ILogger<HomeScreenSectionsPlugin> m_logger;

        private Dictionary<string, Guid> m_registeredTransforms = new Dictionary<string, Guid>();

        public StartupService(IServerApplicationHost serverApplicationHost, 
            IApplicationPaths applicationPaths, 
            ILibraryManager libraryManager,
            IServerConfigurationManager serverConfigurationManager,
            ILogger<HomeScreenSectionsPlugin> logger)
        {
            m_serverApplicationHost = serverApplicationHost;
            m_applicationPaths = applicationPaths;
            m_libraryManager = libraryManager;
            m_serverConfigurationManager = serverConfigurationManager;
            m_logger = logger;
        }

        public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            PatchHelpers.SetupPatches();

            RegisterPluginPage();
            
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
                    
                    Guid guid = m_registeredTransforms.GetValueOrDefault(jsChunk, Guid.NewGuid());
                    m_registeredTransforms[jsChunk] = guid;
                    m_logger.LogInformation($"Found loadSections in `{fileName}` registering transformation for it with ID '{guid}'");
                    
                    JObject payload = new JObject();
                    payload.Add("id", guid.ToString());
                    payload.Add("fileNamePattern", r.Match(fileName).Groups[1].Value + "\\.[^.]+\\.chunk\\.js");
                    payload.Add("callbackAssembly", GetType().Assembly.FullName);
                    payload.Add("callbackClass", typeof(TransformationPatches).FullName);
                    payload.Add("callbackMethod", nameof(TransformationPatches.LoadSections));
                    payloads.Add(payload);
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
            else
            {
                m_logger.LogWarning("FileTransformation plugin not found. Please ensure you install FileTransformation otherwise HomeScreenSections will not work.");
            }

            Assembly? pluginPagesAssembly =
                AssemblyLoadContext.All.SelectMany(x => x.Assemblies).FirstOrDefault(x =>
                    x.FullName?.Contains(".PluginPages") ?? false);

            if (pluginPagesAssembly == null)
            {
                m_logger.LogWarning("PluginPages plugin not found. Please ensure you install PluginPages otherwise you will not be able to have user overrides in HomeScreenSections.");
            }
            
            List<VirtualFolderInfo> libraries = m_libraryManager.GetVirtualFolders();

            foreach (VirtualFolderInfo library in libraries)
            {
                HomeScreenSectionsPlugin.Instance.CollectionFolderMixedStatus[library.Name] =
                    library.IsMixedFolder(m_libraryManager);
            }
        }

        private void RegisterPluginPage()
        {
            int pluginPageConfigVersion = 2;
            
            Assembly? pluginPagesAssembly = AssemblyLoadContext.All.SelectMany(x => x.Assemblies).FirstOrDefault(x => x.FullName?.Contains("Jellyfin.Plugin.PluginPages") ?? false);

            Type[]? types = pluginPagesAssembly?.GetTypes();
            Type? pluginInterface = types?
                .FirstOrDefault(x => x.FullName?.Contains("Jellyfin.Plugin.PluginPages.PluginInterface") ?? false);

            Version earliestVersionWithSubUrls = new Version("2.4.1.0");
            bool supportsSubUrls = pluginPagesAssembly != null && pluginPagesAssembly.GetName().Version >= earliestVersionWithSubUrls;
                
            string rootUrl = m_serverConfigurationManager.GetNetworkConfiguration().BaseUrl.TrimStart('/').Trim();
            if (!string.IsNullOrEmpty(rootUrl))
            {
                rootUrl = $"/{rootUrl}";
            }
            
            JObject payload = new JObject
            {
                { "Id", typeof(HomeScreenSectionsPlugin).Namespace },
                { "Url", $"{(supportsSubUrls ? "" : rootUrl)}/ModularHomeViews/settings" },
                { "DisplayText", "Modular Home" },
                { "Icon", "ballot" },
                { "Version", pluginPageConfigVersion },
                { "IsEnabledAssembly", Assembly.GetExecutingAssembly().FullName },
                { "IsEnabledClass", nameof(PluginInterface) },
                { "IsEnabledMethod", nameof(PluginInterface.IsModularHomePageEnabled) }
            };
            
            if (pluginInterface != null)
            {
                CleanupLegacyPluginPage();
                pluginInterface.GetMethod("RegisterPage")?.Invoke(null, new object?[] { payload });
            }
            else
            {
                LegacyRegisterPluginPage(payload, pluginPageConfigVersion);
            }
        }

        private void CleanupLegacyPluginPage()
        {
            string pluginPagesConfig = Path.Combine(m_applicationPaths.PluginConfigurationsPath, "Jellyfin.Plugin.PluginPages", "config.json");
        
            JObject config = new JObject();
            if (!File.Exists(pluginPagesConfig))
            {
                FileInfo info = new FileInfo(pluginPagesConfig);
                info.Directory?.Create();
            }
            else
            {
                config = JObject.Parse(File.ReadAllText(pluginPagesConfig));
            }

            if (config.ContainsKey("pages"))
            {
                if (config.Value<JArray>("pages")!.FirstOrDefault(x =>
                        x.Value<string>("Id") == typeof(HomeScreenSectionsPlugin).Namespace) is JObject hssPageConfig)
                {
                    config.Value<JArray>("pages")!.Remove(hssPageConfig);
                    
                    File.WriteAllText(pluginPagesConfig, config.ToString(Formatting.Indented));
                }
            }
        }
        
        private void LegacyRegisterPluginPage(JObject payload, int pluginPageConfigVersion)
        {
            string pluginPagesConfig = Path.Combine(m_applicationPaths.PluginConfigurationsPath, "Jellyfin.Plugin.PluginPages", "config.json");
        
            JObject config = new JObject();
            if (!File.Exists(pluginPagesConfig))
            {
                FileInfo info = new FileInfo(pluginPagesConfig);
                info.Directory?.Create();
            }
            else
            {
                config = JObject.Parse(File.ReadAllText(pluginPagesConfig));
            }

            if (!config.ContainsKey("pages"))
            {
                config.Add("pages", new JArray());
            }

            JObject? hssPageConfig = config.Value<JArray>("pages")!.FirstOrDefault(x =>
                x.Value<string>("Id") == typeof(HomeScreenSectionsPlugin).Namespace) as JObject;

            if (hssPageConfig != null)
            {
                if ((hssPageConfig.Value<int?>("Version") ?? 0) < pluginPageConfigVersion)
                {
                    config.Value<JArray>("pages")!.Remove(hssPageConfig);
                }
            }
            
            if (!config.Value<JArray>("pages")!.Any(x => x.Value<string>("Id") == typeof(HomeScreenSectionsPlugin).Namespace))
            {
                config.Value<JArray>("pages")!.Add(payload);
        
                File.WriteAllText(pluginPagesConfig, config.ToString(Formatting.Indented));
            }
        }

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => StartupServiceHelper.GetStartupTrigger();
    }
}