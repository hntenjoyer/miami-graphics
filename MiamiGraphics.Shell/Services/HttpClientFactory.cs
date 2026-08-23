using System.Net.Http;

namespace MiamiGraphics.Shell.Services;

public static class HttpClientFactory
{
    public static HttpClient CreateFragmenting(TimeSpan? timeout = null)
    {
        var http = new HttpClient(new FragmentingHttpHandler(), disposeHandler: true);
        if (timeout.HasValue) http.Timeout = timeout.Value;
        return http;
    }
}
