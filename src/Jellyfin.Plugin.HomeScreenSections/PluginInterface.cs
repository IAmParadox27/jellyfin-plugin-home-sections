using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.Loader;
using Jellyfin.Plugin.HomeScreenSections.HomeScreen.Sections;
using Jellyfin.Plugin.HomeScreenSections.Library;
using Jellyfin.Plugin.HomeScreenSections.Model;
using MediaBrowser.Controller;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.HomeScreenSections
{
    public static class PluginInterface
    {
        public static void RegisterSection(JObject rawPayload)
        {
            IHomeScreenManager homeScreenManager = HomeScreenSectionsPlugin.Instance.ServiceProvider.GetRequiredService<IHomeScreenManager>();
            IServerApplicationHost serverApplicationHost = HomeScreenSectionsPlugin.Instance.ServiceProvider.GetRequiredService<IServerApplicationHost>();

            SectionRegisterPayload? payload = rawPayload.ToObject<SectionRegisterPayload>();
            
            if (payload != null)
            {
                homeScreenManager.RegisterResultsDelegate(new PluginDefinedSection(payload.Id, payload.DisplayText!, payload.Route, payload.AdditionalData)
                {
                    OnGetResults = sectionPayload =>
                    {
                        JObject jsonPayload = JObject.FromObject(sectionPayload);
                        
                        if (payload.ResultsAssembly != null && payload.ResultsClass != null && payload.ResultsMethod != null)
                        {
                            Type? resultsHandlerClass = AssemblyLoadContext.All.SelectMany(x => x.Assemblies)
                                .FirstOrDefault(x => x.FullName == payload.ResultsAssembly)?.GetTypes()
                                .FirstOrDefault(x => x.FullName == payload.ResultsClass);

                            if (resultsHandlerClass != null)
                            {
                                object resultsHandler = ActivatorUtilities.CreateInstance(HomeScreenSectionsPlugin.Instance.ServiceProvider, resultsHandlerClass);
                                
                                MethodInfo? resultsHandlerMethod = resultsHandlerClass.GetMethod(payload.ResultsMethod);

                                object? payloadObj = jsonPayload.ToObject(resultsHandlerMethod!.GetParameters()[0].ParameterType);

                                if (payloadObj != null)
                                {
                                    QueryResult<BaseItemDto>? results = resultsHandlerMethod?.Invoke(resultsHandler, new [] { payloadObj }) as QueryResult<BaseItemDto>;
                                    
                                    return results ?? new QueryResult<BaseItemDto>();
                                }
                            }
                        }
                        
                        if (payload.ResultsEndpoint != null)
                        {
                            string? publishedServerUrl = serverApplicationHost.GetType()
                                .GetProperty("PublishedServerUrl", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(serverApplicationHost) as string;
                    
                            HttpClient client = new HttpClient();
                            client.BaseAddress = new Uri(publishedServerUrl ?? $"http://localhost:{serverApplicationHost.HttpPort}");
                        
                            HttpResponseMessage responseMessage = client.PostAsync(payload.ResultsEndpoint, 
                                new StringContent(jsonPayload.ToString(Formatting.None), MediaTypeHeaderValue.Parse("application/json"))).GetAwaiter().GetResult();

                            return JsonConvert.DeserializeObject<QueryResult<BaseItemDto>>(responseMessage.Content.ReadAsStringAsync().GetAwaiter().GetResult()) ?? new QueryResult<BaseItemDto>();
                        }

                        return new QueryResult<BaseItemDto>();
                    }
                });
            }
        }
    }
}