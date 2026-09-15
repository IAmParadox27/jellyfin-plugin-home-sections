using System.Text.Json;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.HomeScreenSections.Configuration;

namespace Jellyfin.Plugin.HomeScreenSections.Services
{
    internal class HomeScreenConfigurationOptionsConverter : JsonConverter<PluginConfigurationOption[]>
    {
        public override PluginConfigurationOption[]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotSupportedException("This converter is only used by the section output formatter.");
        }

        public override void Write(Utf8JsonWriter writer, PluginConfigurationOption[] value, JsonSerializerOptions options)
        {
            // Rows without configuration do not need the element's serialization metadata.
            writer.WriteStartArray();
            foreach (PluginConfigurationOption option in value)
            {
                JsonSerializer.Serialize(writer, option, options);
            }
            writer.WriteEndArray();
        }
    }
}
