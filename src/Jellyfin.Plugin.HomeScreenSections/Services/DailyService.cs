using System.Reflection;
using Jellyfin.Plugin.HomeScreenSections.JellyfinVersionSpecific;
using Jellyfin.Plugin.HomeScreenSections.Library;
using MediaBrowser.Model.Tasks;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.HomeScreenSections.Services
{
    public class DailyService : IScheduledTask
    {
        public string Name => "HSS Daily Translation Cache";
        public string Key => "Jellyfin.Plugin.HomeScreenSections.DailyTranslationCache";
        public string Description => "Goes to GitHub and downloads the latest translation files.";
        public string Category => "Maintenance";

        private readonly ITranslationManager m_translationManager;
        
        // Trailing slash included to avoid getting the folder from the github trees JSON data
        private const string c_locPath = "src/Jellyfin.Plugin.HomeScreenSections/_Localization/";

        public DailyService(ITranslationManager translationManager)
        {
            m_translationManager = translationManager;
        }
        
        public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/143.0.0.0 Safari/537.36");
            
            string? gitBranch = Assembly.GetExecutingAssembly()
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(x => x.Key == "GitBranch")?.Value;

            if (!string.IsNullOrEmpty(gitBranch))
            {
                HttpResponseMessage treesResponse = await client.GetAsync(new Uri($"https://api.github.com/repos/IAmParadox27/jellyfin-plugin-home-sections/git/trees/{gitBranch}?recursive=1"), cancellationToken);

                // After getting the trees we say we're 10% just to signify something has happened
                double currentProgress = 0.1;
                progress.Report(currentProgress);
                
                string treesJsonRaw = await treesResponse.Content.ReadAsStringAsync(cancellationToken);

                JObject treesObj = JObject.Parse(treesJsonRaw);
                IEnumerable<JObject>? data = treesObj.Value<JArray>("tree")?.OfType<JObject>().Where(x => x.Value<string>("path")?.StartsWith(c_locPath) ?? false);
                if (data != null)
                {
                    string[] blobUrls = data.Select(x => x.Value<string>("path")).Where(x => x != null).Select(x => x!).ToArray();
                    
                    double progressIncrement = 0.9 / blobUrls.Length;
                    
                    foreach (string blobUrl in blobUrls)
                    {
                        HttpResponseMessage blobResponse = await client.GetAsync(new Uri($"https://raw.githubusercontent.com/IAmParadox27/jellyfin-plugin-home-sections/refs/heads/{gitBranch}/{blobUrl}"), cancellationToken);
                        string blobJsonRaw = await blobResponse.Content.ReadAsStringAsync(cancellationToken);
                        
                        string languageCode = Path.GetFileNameWithoutExtension(blobUrl);
                        JObject languagePack = JObject.Parse(blobJsonRaw);
                        
                        m_translationManager.UpdateTranslationPack(languageCode, languagePack);
                        
                        currentProgress += progressIncrement;
                        progress.Report(currentProgress);
                    }
                }
            }
            
            progress.Report(1);
        }

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => StartupServiceHelper.GetStartupTrigger()
            .Concat(StartupServiceHelper.GetDailyTrigger(TimeSpan.FromHours(3)));

    }
}