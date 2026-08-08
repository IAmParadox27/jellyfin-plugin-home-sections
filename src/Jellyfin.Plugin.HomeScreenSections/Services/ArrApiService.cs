using System.Text.Json;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.HomeScreenSections.Configuration;

namespace Jellyfin.Plugin.HomeScreenSections.Services
{
    public enum ArrServiceType
    {
        Sonarr,
        Radarr,
        Lidarr,
        Readarr
    }

    public class ArrApiService
    {
        private static readonly System.Text.Json.JsonSerializerOptions s_calendarJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly ILogger<ArrApiService> _logger;
        private readonly HttpClient _httpClient;

        public ArrApiService(ILogger<ArrApiService> logger, HttpClient httpClient)
        {
            _logger = logger;
            _httpClient = httpClient;
        }
        
        private static PluginConfiguration Config => HomeScreenSectionsPlugin.Instance?.Configuration ?? new PluginConfiguration();

        public async Task<T[]?> GetArrCalendarAsync<T>(ArrServiceType serviceType, DateTime startDate, DateTime endDate)
        {
            (string? url, string? apiKey, string? serviceName) = GetServiceConfig(serviceType);

            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(apiKey))
            {
                PluginLog.ArrUrlOrKeyMissing(_logger, serviceName);
                return null;
            }

            try
            {
                return await FetchCalendarItemsAsync<T>(serviceType, url, apiKey, serviceName, startDate, endDate);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException or InvalidOperationException)
            {
                if (ex is JsonException)
                {
                    PluginLog.ArrCalendarJsonError(_logger, (JsonException)ex, serviceName);
                }
                else if (ex is HttpRequestException)
                {
                    PluginLog.ArrCalendarHttpError(_logger, (HttpRequestException)ex, serviceName);
                }
                else
                {
                    PluginLog.ArrCalendarUnexpectedError(_logger, ex, serviceName);
                }

                return null;
            }
        }

        private async Task<T[]?> FetchCalendarItemsAsync<T>(
            ArrServiceType serviceType,
            string url,
            string apiKey,
            string? serviceName,
            DateTime startDate,
            DateTime endDate)
        {
            string requestUrl = BuildCalendarRequestUrl(serviceType, url, startDate, endDate);

            using HttpRequestMessage request = new(HttpMethod.Get, requestUrl);
            request.Headers.Add("X-API-KEY", apiKey);

            PluginLog.FetchingArrCalendar(_logger, serviceName, requestUrl);

            HttpResponseMessage response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                PluginLog.ArrCalendarHttpFailed(_logger, serviceName, response.StatusCode, response.ReasonPhrase);
                return null;
            }

            string jsonContent = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrEmpty(jsonContent))
            {
                PluginLog.ArrCalendarEmpty(_logger, serviceName);
                return [];
            }

            T[]? calendarItems = JsonSerializer.Deserialize<T[]>(jsonContent, s_calendarJsonOptions);
            PluginLog.ArrCalendarFetched(_logger, calendarItems?.Length ?? 0, serviceName);
            return calendarItems ?? [];
        }

        private static string BuildCalendarRequestUrl(ArrServiceType serviceType, string url, DateTime startDate, DateTime endDate)
        {
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            string startParam = startDate.ToString("yyyy-MM-ddTHH:mm:ssZ", culture);
            string endParam = endDate.ToString("yyyy-MM-ddTHH:mm:ssZ", culture);
            (string queryParams, string apiVersion) = serviceType switch
            {
                ArrServiceType.Sonarr => ($"includeSeries=true&start={startParam}&end={endParam}", "v3"),
                ArrServiceType.Radarr => ($"start={startParam}&end={endParam}", "v3"),
                ArrServiceType.Lidarr => ($"start={startParam}&end={endParam}", "v1"),
                ArrServiceType.Readarr => ($"includeAuthor=true&start={startParam}&end={endParam}", "v1"),
                _ => ($"start={startParam}&end={endParam}", "v3")
            };
            return $"{url.TrimEnd('/')}/api/{apiVersion}/calendar?{queryParams}";
        }

        private static (string? url, string? apiKey, string serviceName) GetServiceConfig(ArrServiceType serviceType)
        {
            return serviceType switch
            {
                ArrServiceType.Sonarr => (Config.Sonarr.Url, Config.Sonarr.ApiKey, "Sonarr"),
                ArrServiceType.Radarr => (Config.Radarr.Url, Config.Radarr.ApiKey, "Radarr"),
                ArrServiceType.Lidarr => (Config.Lidarr.Url, Config.Lidarr.ApiKey, "Lidarr"),
                ArrServiceType.Readarr => (Config.Readarr.Url, Config.Readarr.ApiKey, "Readarr"),
                _ => throw new ArgumentOutOfRangeException(nameof(serviceType), serviceType, "Unsupported service type")
            };
        }

        public static DateTime CalculateEndDate(DateTime startDate, int timeframeValue, TimeframeUnit timeframeUnit)
        {
            return timeframeUnit switch
            {
                TimeframeUnit.Days => startDate.AddDays(timeframeValue),
                TimeframeUnit.Weeks => startDate.AddDays(timeframeValue * 7),
                TimeframeUnit.Months => startDate.AddMonths(timeframeValue),
                TimeframeUnit.Years => startDate.AddYears(timeframeValue),
                _ => startDate.AddDays(timeframeValue)
            };
        }

        public static string FormatDate(DateTime date, string format, string delimiter)
        {
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            return format.ToUpperInvariant() switch
            {
                "YYYY/MM/DD" => date.ToString($"yyyy{delimiter}MM{delimiter}dd", culture),
                "DD/MM/YYYY" => date.ToString($"dd{delimiter}MM{delimiter}yyyy", culture),
                "MM/DD/YYYY" => date.ToString($"MM{delimiter}dd{delimiter}yyyy", culture),
                "DD/MM" => date.ToString($"dd{delimiter}MM", culture),
                "MM/DD" => date.ToString($"MM{delimiter}dd", culture),
                _ => date.ToString($"yyyy{delimiter}MM{delimiter}dd", culture)
            };
        }
    }
}