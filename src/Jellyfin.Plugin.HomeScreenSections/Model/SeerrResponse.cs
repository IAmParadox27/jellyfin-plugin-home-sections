using System.Net;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.HomeScreenSections.Model
{
    public class SeerrResponse
    {
        public HttpStatusCode StatusCode { get; init; }

        public required JObject Response { get; init; }
    }
}
