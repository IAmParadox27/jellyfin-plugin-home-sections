using System.Text.Json.Serialization;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Plugin.HomeScreenSections.Services
{
    [JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
    [JsonSerializable(typeof(QueryResult<HomeScreenSectionInfo>))]
    [JsonSerializable(typeof(HomeScreenBootstrap))]
    internal partial class HomeScreenJsonSerializerContext : JsonSerializerContext
    {
    }
}
