using System.Text.Json;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Mvc.Formatters;

namespace Jellyfin.Plugin.HomeScreenSections.Services
{
    internal class HomeScreenJsonOutputFormatter : SystemTextJsonOutputFormatter
    {
        public HomeScreenJsonOutputFormatter(SystemTextJsonOutputFormatter formatter) : base(CreateOptions(formatter))
        {
            SupportedMediaTypes.Clear();
            foreach (string mediaType in formatter.SupportedMediaTypes)
            {
                SupportedMediaTypes.Add(mediaType);
            }

            SupportedEncodings.Clear();
            foreach (System.Text.Encoding encoding in formatter.SupportedEncodings)
            {
                SupportedEncodings.Add(encoding);
            }
        }

        protected override bool CanWriteType(Type? type)
        {
            return type == typeof(QueryResult<HomeScreenSectionInfo>) || type == typeof(HomeScreenBootstrap);
        }

        private static JsonSerializerOptions CreateOptions(SystemTextJsonOutputFormatter formatter)
        {
            JsonSerializerOptions options = new JsonSerializerOptions(formatter.SerializerOptions);
            options.TypeInfoResolverChain.Insert(0, HomeScreenJsonSerializerContext.Default);
            if (formatter.SerializerOptions.ReferenceHandler == null)
            {
                options.Converters.Insert(0, new HomeScreenConfigurationOptionsConverter());
                options.Converters.Insert(0, new HomeScreenPayloadConverter(formatter.SerializerOptions));
            }
            return options;
        }
    }
}
