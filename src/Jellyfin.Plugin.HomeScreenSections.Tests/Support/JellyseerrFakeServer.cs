using System.Net;
using System.Text;

namespace Jellyfin.Plugin.HomeScreenSections.Tests.Support;

/// <summary>
/// Minimal local HTTP server used to stand in for Jellyseerr: the discover/requests
/// sections construct their own HttpClient pointed at Instance.Configuration.JellyseerrUrl,
/// so a loopback listener is the only seam available without production refactors.
/// </summary>
public sealed class JellyseerrFakeServer : IDisposable
{
    private readonly HttpListener m_listener;
    private readonly Func<string, (int StatusCode, string Json)> m_responder;
    private readonly Thread m_worker;
    private readonly List<string> m_requestsReceived = [];
    private volatile bool m_disposed;

    private JellyseerrFakeServer(int port, Func<string, (int StatusCode, string Json)> responder)
    {
        m_responder = responder;
        // Production code (PluginInterface/HomeScreenController) builds "http://localhost:{port}",
        // while sections point directly at BaseUrl. Bind the "localhost" prefix so requests to
        // either host name land here; on Linux "localhost" can resolve to ::1 and a 127.0.0.1-only
        // binding would miss it (the RegisterSection test failed on CI for exactly this reason).
        BaseUrl = $"http://localhost:{port}/";
        m_listener = new HttpListener();
        m_listener.Prefixes.Add(BaseUrl);
        try
        {
            m_listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        }
        catch (HttpListenerException)
        {
            // Some platforms treat the localhost prefix as already covering 127.0.0.1.
        }
        m_listener.Start();

        m_worker = new Thread(AcceptLoop)
        {
            IsBackground = true
        };
        m_worker.Start();
    }

    public string BaseUrl { get; }

    public IReadOnlyList<string> RequestsReceived
    {
        get
        {
            lock (m_requestsReceived)
            {
                return m_requestsReceived.ToArray();
            }
        }
    }

    public static JellyseerrFakeServer Start(Func<string, (int StatusCode, string Json)> responder)
    {
        // Find a free loopback port, then hand it to HttpListener (tiny race window is acceptable in tests).
        System.Net.Sockets.TcpListener probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        return new JellyseerrFakeServer(port, responder);
    }

    private void AcceptLoop()
    {
        while (!m_disposed)
        {
            HttpListenerContext context;
            try
            {
                context = m_listener.GetContext();
            }
            catch (HttpListenerException)
            {
                return; // listener stopped
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            try
            {
                string pathAndQuery = context.Request.Url?.PathAndQuery ?? string.Empty;
                lock (m_requestsReceived)
                {
                    m_requestsReceived.Add(pathAndQuery);
                }

                (int statusCode, string json) = m_responder(pathAndQuery);
                byte[] body = Encoding.UTF8.GetBytes(json);
                context.Response.StatusCode = statusCode;
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = body.Length;
                context.Response.OutputStream.Write(body, 0, body.Length);
                context.Response.Close();
            }
            catch (HttpListenerException)
            {
                // Client went away mid-response; the next request still gets served.
            }
            catch (IOException)
            {
                // Broken pipe while writing the response body.
            }
            catch (ObjectDisposedException)
            {
                return;
            }
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        m_disposed = true;
        try
        {
            m_listener.Stop();
            m_listener.Close();
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
