using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.HomeScreenSections.Services
{
    internal class HomeScreenPayloadConverter : JsonConverter<object>
    {
        private readonly JsonSerializerOptions m_nativeOptions;

        public HomeScreenPayloadConverter(JsonSerializerOptions nativeOptions)
        {
            m_nativeOptions = nativeOptions;
        }

        public override bool HandleNull => true;

        public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotSupportedException("This converter is only used by the section output formatter.");
        }

        public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
        {
            // Keep arbitrary section payloads on Jellyfin's original policies and metadata cache.
            JsonSerializer.Serialize<object>(writer, value, m_nativeOptions);
        }
    }
}
