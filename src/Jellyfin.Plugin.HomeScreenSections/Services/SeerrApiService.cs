using System.Net;
using System.Net.Http.Json;
using Jellyfin.Plugin.HomeScreenSections.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.HomeScreenSections.Services
{
    public class SeerrApiService
    {
        public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
        private const long c_maxResponseBytes = 4 * 1024 * 1024;
        private readonly IHttpClientFactory m_httpClientFactory;

        public SeerrApiService(IHttpClientFactory httpClientFactory)
        {
            m_httpClientFactory = httpClientFactory;
        }

        public async Task<int?> GetUserIdAsync(string username, CancellationToken cancellationToken)
        {
            return GetUserId(await GetUserAsync(username, cancellationToken), username);
        }

        public Task<SeerrResponse> GetUserAsync(string username, CancellationToken cancellationToken)
        {
            return GetAsync($"/api/v1/user?q={Uri.EscapeDataString(username)}", null, cancellationToken);
        }

        public static int? GetUserId(SeerrResponse response, string username)
        {
            JArray results = GetResults(response.Response);
            JObject? user = null;
            foreach (JToken result in results)
            {
                if (result is JObject candidate &&
                    candidate["jellyfinUsername"]?.Type == JTokenType.String &&
                    candidate.Value<string>("jellyfinUsername") == username)
                {
                    user = candidate;
                    break;
                }
            }
            if (user == null)
            {
                return null;
            }

            if (user["id"]?.Type != JTokenType.Integer || !int.TryParse(user["id"]?.ToString(), out int userId) || userId <= 0)
            {
                throw new SeerrRequestException(HttpStatusCode.BadGateway, "Seerr returned an invalid user ID.");
            }

            return userId;
        }

        public Task<SeerrResponse> GetAsync(string endpoint, int? userId, CancellationToken cancellationToken)
        {
            return SendAsync(CreateRequest(HttpMethod.Get, endpoint, userId), cancellationToken);
        }

        public Task<SeerrResponse> RequestAsync(DiscoverRequestPayload payload, int userId, CancellationToken cancellationToken)
        {
            HttpRequestMessage request = CreateRequest(HttpMethod.Post, "/api/v1/request", userId);
            try
            {
                request.Content = payload.MediaType == "tv"
                    ? JsonContent.Create(new JellyseerrTvShowRequestPayload { MediaId = payload.MediaId, MediaType = payload.MediaType, Seasons = "all" })
                    : JsonContent.Create(new JellyseerrRequestPayload { MediaId = payload.MediaId, MediaType = payload.MediaType });
                return SendAsync(request, cancellationToken);
            }
            catch
            {
                request.Dispose();
                throw;
            }
        }

        public static JArray GetResults(JObject response)
        {
            if (response["results"] is not JArray results || results.Any(x => x is not JObject))
            {
                throw new SeerrRequestException(HttpStatusCode.BadGateway, "Seerr returned an invalid results array.");
            }

            return results;
        }

        private HttpRequestMessage CreateRequest(HttpMethod method, string endpoint, int? userId)
        {
            string? url = HomeScreenSectionsPlugin.Instance.Configuration.JellyseerrUrl;
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? baseUri) ||
                (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
            {
                throw new SeerrRequestException(HttpStatusCode.BadRequest, "Configure an absolute HTTP or HTTPS Seerr URL.");
            }

            // Keep the existing origin-relative API routes; external display URLs are never used for requests.
            HttpRequestMessage request = new HttpRequestMessage(method, new Uri(baseUri, endpoint));
            request.Headers.Add("X-Api-Key", HomeScreenSectionsPlugin.Instance.Configuration.JellyseerrApiKey);
            if (userId.HasValue)
            {
                request.Headers.Add("X-Api-User", userId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            return request;
        }

        private async Task<SeerrResponse> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using (request)
            {
                try
                {
                    using HttpClient client = m_httpClientFactory.CreateClient();
                    using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    if (!response.IsSuccessStatusCode)
                    {
                        // Preserve failures without reflecting an upstream HTML page, credentials or other private response data.
                        HttpStatusCode statusCode = (int)response.StatusCode >= 400 ? response.StatusCode : HttpStatusCode.BadGateway;
                        throw new SeerrRequestException(statusCode, $"Seerr returned HTTP {(int)response.StatusCode}.");
                    }

                    await response.Content.LoadIntoBufferAsync(c_maxResponseBytes, cancellationToken);
                    string content = await response.Content.ReadAsStringAsync(cancellationToken);
                    return new SeerrResponse() { StatusCode = response.StatusCode, Response = JObject.Parse(content) };
                }
                catch (JsonException)
                {
                    throw new SeerrRequestException(HttpStatusCode.BadGateway, "Seerr returned an invalid JSON object.");
                }
                catch (HttpRequestException)
                {
                    throw new SeerrRequestException(HttpStatusCode.BadGateway, "The Seerr request failed or its response exceeded the size limit.");
                }
            }
        }
    }

    public class SeerrRequestException : Exception
    {
        public HttpStatusCode StatusCode { get; }

        public SeerrRequestException(HttpStatusCode statusCode, string message) : base(message)
        {
            StatusCode = statusCode;
        }
    }
}
