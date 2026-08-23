using System.Net.Http;

namespace MiamiGraphics.Installer.Services;

public static class GeoCheck
{
    private static readonly string[] TraceUrls =
    {
        "https://miamigraphicsstorage.uk/cdn-cgi/trace",
        "https://cdn.miamigraphicsstorage.uk/cdn-cgi/trace",
    };

    private static readonly HashSet<string> BlockedCountries =
        new(StringComparer.OrdinalIgnoreCase) { "RU", "BY" };

    public static async Task<bool> IsBlockedRegionAsync()
    {
        foreach (var url in TraceUrls)
        {
            try
            {
                using var http = new HttpClient(new FragmentingHttpHandler(), disposeHandler: true)
                {
                    Timeout = TimeSpan.FromSeconds(6),
                };
                var body = await http.GetStringAsync(url);
                foreach (var line in body.Split('\n'))
                {
                    var t = line.Trim();
                    if (t.StartsWith("loc=", StringComparison.OrdinalIgnoreCase))
                    {
                        var cc = t.Substring(4).Trim();
                        System.Diagnostics.Debug.WriteLine($"[geo] loc={cc}");
                        return BlockedCountries.Contains(cc);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[geo] {url}: {ex.Message}");
            }
        }
        System.Diagnostics.Debug.WriteLine("[geo] trace failed on all hosts -> assume blocked (RU)");
        return true;
    }
}
