using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.HomeScreenSections.Helpers;

internal static class JellyseerrHelper
{
    internal static HttpClient CreateClient(string jellyseerrUrl)
    {
        IHttpClientFactory? factory = HomeScreenSectionsPlugin.Instance.ServiceProvider.GetService<IHttpClientFactory>();
        HttpClient client = factory?.CreateClient() ?? new HttpClient();
        client.BaseAddress = new Uri(jellyseerrUrl);
        client.DefaultRequestHeaders.Add("X-Api-Key", HomeScreenSectionsPlugin.Instance.Configuration.JellyseerrApiKey);
        return client;
    }

    internal static int? ResolveUserId(HttpClient client, string username)
    {
        HttpResponseMessage usersResponse = client.GetAsync($"/api/v1/user?q={Uri.EscapeDataString(username)}").GetAwaiter().GetResult();
        string userResponseRaw = usersResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        return JObject.Parse(userResponseRaw).Value<JArray>("results")?.OfType<JObject>()
            .FirstOrDefault(x => string.Equals(x.Value<string>("jellyfinUsername"), username, StringComparison.Ordinal))
            ?.Value<int>("id");
    }
}
