using System.Text.Json.Serialization;
using Jellyfin.Plugin.HomeScreenSections.Library;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Plugin.HomeScreenSections.Services
{
    [JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
    [JsonSerializable(typeof(QueryResult<HomeScreenSectionInfo>))]
    internal partial class HomeScreenJsonSerializerContext : JsonSerializerContext
    {
    }
}
