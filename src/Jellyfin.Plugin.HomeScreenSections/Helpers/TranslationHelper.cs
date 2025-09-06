using System.Net.Http.Headers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.HomeScreenSections.Helpers;

public static class TranslationHelper
{
    public static async Task<string?> TranslateAsync(string text, string srcLanguage, string destLanguage)
    {
        if (HomeScreenSectionsPlugin.Instance.Configuration.LibreTranslateUrl != null)
        {
            try
            {
                JObject jsonPayload = new JObject();
                jsonPayload["q"] = text;
                jsonPayload["source"] = srcLanguage;
                jsonPayload["target"] = destLanguage;
                jsonPayload["api_key"] = HomeScreenSectionsPlugin.Instance.Configuration.LibreTranslateApiKey;

                HttpClient client = new HttpClient();
                HttpResponseMessage response = await client.PostAsync(
                    $"{HomeScreenSectionsPlugin.Instance.Configuration.LibreTranslateUrl}/translate",
                    new StringContent(jsonPayload.ToString(Formatting.None),
                        MediaTypeHeaderValue.Parse("application/json")));

                JObject responseObj = JObject.Parse(await response.Content.ReadAsStringAsync());

                return responseObj.Value<string>("translatedText");
            }
            catch
            {
                // ignored
            }
        }

        return text;
    }
}