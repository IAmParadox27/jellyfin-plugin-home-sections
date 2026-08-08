using System.Net;

namespace Jellyfin.Plugin.HomeScreenSections.Tests.Support;

/// <summary>
/// Scriptable HttpMessageHandler so services that take an HttpClient can be tested
/// without touching the network.
/// </summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> m_responder;
    private readonly List<HttpRequestMessage> m_requests = [];

    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        m_responder = responder;
    }

    public IReadOnlyList<HttpRequestMessage> Requests => m_requests;

    public static FakeHttpMessageHandler RespondingWithJson(string json)
    {
        return new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        });
    }

    public static FakeHttpMessageHandler RespondingWithStatus(HttpStatusCode statusCode)
    {
        return new FakeHttpMessageHandler(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(string.Empty)
        });
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        m_requests.Add(request);
        return Task.FromResult(m_responder(request));
    }
}
