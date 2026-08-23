using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiamiGraphics.Core.I18n;

namespace MiamiGraphics.Shell.Services;

public enum SupabaseErrorKind
{
    Network,
    NotProvisioned,
    Server,
    Unauthorized,
    Conflict,
    BadRequest,
}

public sealed class SupabaseException : Exception
{
    public SupabaseErrorKind Kind { get; }
    public int? StatusCode { get; }

    public SupabaseException(SupabaseErrorKind kind, string message, int? statusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
        StatusCode = statusCode;
    }
}

public sealed class SupabaseClient
{

    private static readonly object _regionLock = new();
    private static ServerRegion _cachedRegion = ServerRegion.NotSelected;
    private static string _cachedUrl = ServerRegionConfig.EuUrl;
    private static string[] _cachedBaseUrls = ServerRegionConfig.AllUrls(ServerRegion.Eu);

    public static string Url
    {
        get
        {
            EnsureRegionLoaded();
            return _cachedUrl;
        }
    }

    public const string AnonKey = "";

    private static readonly string? AdminApiTokenEnv = Environment.GetEnvironmentVariable("HG_ADMIN_API_TOKEN");

    public static string? AdminApiTokenOverride { get; set; }

    private static string? AdminApiToken =>
        !string.IsNullOrWhiteSpace(AdminApiTokenOverride) ? AdminApiTokenOverride : AdminApiTokenEnv;

    public static string? UserSessionToken
    {
        get
        {
            if (!_sessionLoaded)
            {
                _userSessionToken = LoadPersistedSessionToken();
                _sessionLoaded = true;
            }
            return _userSessionToken;
        }
        set
        {
            _userSessionToken = value;
            _sessionLoaded = true;
            PersistSessionToken(value);
        }
    }

    private static string? _userSessionToken;
    private static bool _sessionLoaded;

    private static string SessionTokenFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MiamiGraphics", "config", "session.bin");

    private static string? LoadPersistedSessionToken()
    {
        try
        {
            var path = SessionTokenFilePath;
            if (!File.Exists(path)) return null;
            var enc = File.ReadAllBytes(path);
            if (enc.Length == 0) return null;
            var raw = ProtectedData.Unprotect(enc, null, DataProtectionScope.CurrentUser);
            var tok = Encoding.UTF8.GetString(raw);
            return string.IsNullOrWhiteSpace(tok) ? null : tok;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Supabase] session token load failed: {ex.Message}");
            return null;
        }
    }

    private static void PersistSessionToken(string? token)
    {
        try
        {
            var path = SessionTokenFilePath;
            if (string.IsNullOrWhiteSpace(token))
            {
                if (File.Exists(path)) File.Delete(path);
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var raw = Encoding.UTF8.GetBytes(token);
            var enc = ProtectedData.Protect(raw, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(path, enc);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Supabase] session token persist failed: {ex.Message}");
        }
    }
    private static readonly HashSet<string> AdminWriteTables = new(StringComparer.Ordinal)
    {
        "featured_picks", "redux_items", "redux_versions", "gunpacks", "gunpack_guns",
        "gunpack_whitelist", "gunpack_variants", "gta_versions", "gta_presets",
        "library_components", "armor_library",
    };
    private static readonly HashSet<string> AdminRpcs = new(StringComparer.Ordinal)
    {
        "user_build_approve", "user_build_reject",
    };

    private static readonly HashSet<string> IdempotentRpcs = new(StringComparer.Ordinal)
    {
        "user_get_profile", "account_stats_secure", "activity_install_counts",
        "app_runtime_config", "beta_code_check", "referral_check_promo",
        "custom_guns_mine_secure", "custom_gun_list_pending_secure",
        "custom_gun_admin_list_secure", "hnt_my_codes",

        "authenticate_user",

        "referral_attach_signup_secure",
        "big_map_review_submit_secure", "big_map_review_delete_secure",
        "user_build_review_submit_secure", "user_build_review_delete_secure",
        "redux_review_delete_secure",
        "custom_gun_delete_secure", "custom_gun_admin_delete_secure",

    };

    private static readonly TimeSpan HostAttemptBudget = TimeSpan.FromSeconds(8);

    private static readonly TimeSpan BodyReadBudget = TimeSpan.FromSeconds(60);

    private static volatile string? _stickyHost;
    private static long _stickyUntilTicks;
    private static readonly TimeSpan StickyWinnerTtl = TimeSpan.FromSeconds(60);

    private static string[] OrderByStickyWinner(string[] hosts)
    {
        if (hosts.Length < 2) return hosts;
        var sticky = _stickyHost;
        if (sticky is null) return hosts;
        if (Environment.TickCount64 > Interlocked.Read(ref _stickyUntilTicks)) return hosts;

        var idx = Array.IndexOf(hosts, sticky);
        if (idx <= 0) return hosts;

        var result = new string[hosts.Length];
        result[0] = sticky;
        var w = 1;
        for (var i = 0; i < hosts.Length; i++)
            if (i != idx) result[w++] = hosts[i];
        return result;
    }

    private static void NoteHostWin(string winner, string regionPrimary)
    {
        if (string.Equals(winner, regionPrimary, StringComparison.OrdinalIgnoreCase))
        {
            if (_stickyHost is not null)
            {
                _stickyHost = null;
                Debug.WriteLine($"[Supabase] основной хост {regionPrimary} снова отвечает - липкость снята");
            }
            return;
        }
        if (!string.Equals(_stickyHost, winner, StringComparison.OrdinalIgnoreCase))
            Debug.WriteLine($"[Supabase] запасной {winner} выручил - ставим его первым на {StickyWinnerTtl.TotalSeconds:F0} с");
        _stickyHost = winner;
        Interlocked.Exchange(ref _stickyUntilTicks, Environment.TickCount64 + (long)StickyWinnerTtl.TotalMilliseconds);
    }

    private static readonly TimeSpan PingHostBudget = TimeSpan.FromSeconds(7);

    private static readonly TimeSpan PingFallbackHeadStart = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan ResponseWaitBudget = TimeSpan.FromSeconds(18);

    public static readonly string DirectUrl = System.Environment.GetEnvironmentVariable("MG_SUPABASE_URL") ?? "";

    public static string StoragePublicUrl(string bucket, string key)
        => $"{DirectUrl.TrimEnd('/')}/storage/v1/object/public/{bucket}/{key.TrimStart('/')}";

    private static string[] BaseUrls
    {
        get
        {
            EnsureRegionLoaded();
            return _cachedBaseUrls;
        }
    }

    public static void ReloadRegion()
    {
        lock (_regionLock)
        {
            _cachedRegion = ServerRegion.NotSelected;
        }
    }

    private static void EnsureRegionLoaded()
    {
        if (_cachedRegion != ServerRegion.NotSelected) return;

        lock (_regionLock)
        {
            if (_cachedRegion != ServerRegion.NotSelected) return;

            var region = ServerRegionStore.Load();
            _cachedRegion   = region;
            _cachedUrl      = ServerRegionConfig.PrimaryUrl(region);
            _cachedBaseUrls = ServerRegionConfig.AllUrls(region);
            Debug.WriteLine($"[Supabase] region loaded: {region} → {_cachedUrl}");
        }
    }

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy   = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;

    public SupabaseClient()
    {

        _http = new HttpClient(new FragmentingHttpHandler())
        {
            Timeout = TimeSpan.FromSeconds(60),
        };
        if (!string.IsNullOrWhiteSpace(AnonKey))
        {
            _http.DefaultRequestHeaders.Add("apikey", AnonKey);
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AnonKey);
        }
    }

    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        var hosts = BaseUrls;
        if (hosts.Length == 0) return false;

        using var race = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var failed = hosts.Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        var probes = hosts.Select((h, i) => ProbeAsync(h, i, race.Token)).ToList();
        try
        {
            while (probes.Count > 0)
            {
                var done = await Task.WhenAny(probes);
                probes.Remove(done);
                if (await done) return true;
            }
            return false;
        }
        finally
        {
            race.Cancel();
            try { await Task.WhenAll(probes); } catch { }
        }

        async Task<bool> ProbeAsync(string baseUrl, int index, CancellationToken token)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            try
            {
                if (index > 0)
                {
                    var wait = Task.Delay(PingFallbackHeadStart * index, cts.Token);
                    await Task.WhenAny(wait, failed[index - 1].Task);
                    cts.Token.ThrowIfCancellationRequested();
                }

                cts.CancelAfter(PingHostBudget);
                using var resp = await _http.GetAsync(AbsoluteUrl(baseUrl, "rest/v1/"), cts.Token);
                Debug.WriteLine($"[Supabase.ping] {baseUrl} {(int)resp.StatusCode} {resp.ReasonPhrase}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Supabase.ping] {baseUrl} FAIL {ex.GetType().Name}: {ex.Message}");
                failed[index].TrySetResult();
                return false;
            }
        }
    }

    public async Task<List<T>> SelectAsync<T>(string table, string? query = null, CancellationToken ct = default)
    {
        var url = $"rest/v1/{table}{(string.IsNullOrEmpty(query) ? "" : "?" + query)}";
        return await SendAsync(HttpMethod.Get, url, content: null, ct, async resp =>
        {
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            return await JsonSerializer.DeserializeAsync<List<T>>(stream, Json, ct) ?? new();
        });
    }

    public async Task<T?> SelectOneAsync<T>(string table, string query, CancellationToken ct = default) where T : class
    {
        var list = await SelectAsync<T>(table, query + "&limit=1", ct);
        return list.FirstOrDefault();
    }

    public async Task<List<T>> SelectAllPagedAsync<T>(
        string table, string query, string order = "id.asc",
        int pageSize = 1000, CancellationToken ct = default)
    {
        var all = new List<T>();
        for (var offset = 0; offset <= 200_000; offset += pageSize)
        {
            var page = await SelectAsync<T>(
                table, $"{query}&order={order}&limit={pageSize}&offset={offset}", ct);
            all.AddRange(page);
            if (page.Count < pageSize) break;
        }
        return all;
    }

    public async Task UpsertAsync<T>(string table, T row, CancellationToken ct = default)
    {
        await SendAsync<object?>(HttpMethod.Post, $"rest/v1/{table}",
            content: () =>
            {
                var c = JsonContent.Create(new[] { row }, options: Json);
                c.Headers.Add("Prefer", "resolution=merge-duplicates,return=minimal");
                return c;
            }, ct,
            handle: _ => Task.FromResult<object?>(null));
    }

    public async Task UpsertManyAsync<T>(string table, IEnumerable<T> rows, CancellationToken ct = default)
    {
        var list = rows as IReadOnlyCollection<T> ?? rows.ToList();
        if (list.Count == 0) return;

        await SendAsync<object?>(HttpMethod.Post, $"rest/v1/{table}",
            content: () =>
            {
                var c = JsonContent.Create(list, options: BulkJson);
                c.Headers.Add("Prefer", "resolution=merge-duplicates,return=minimal");
                return c;
            }, ct,
            handle: _ => Task.FromResult<object?>(null));
    }

    private static readonly JsonSerializerOptions BulkJson = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition      = JsonIgnoreCondition.Never,
    };

    public async Task DeleteAsync(string table, string query, CancellationToken ct = default)
    {
        await SendAsync<object?>(HttpMethod.Delete, $"rest/v1/{table}?{query}",
            content: null, ct,
            extraHeaders: req => req.Headers.Add("Prefer", "return=minimal"),
            handle: _ => Task.FromResult<object?>(null));
    }

    public async Task<int> DeleteWithServiceRoleAsync(string table, string query, string serviceRoleKey, CancellationToken ct = default)
    {
        var relative = $"rest/v1/{table}?{query}";
        var bases = BaseUrls;
        Exception? lastError = null;
        for (var i = 0; i < bases.Length; i++)
        {
            var url = AbsoluteUrl(bases[i], relative);
            using var req = new HttpRequestMessage(HttpMethod.Delete, url);
            ApplyAdminWriteAuth(req);
            req.Headers.Add("Prefer", "return=minimal,count=exact");

            HttpResponseMessage resp;
            try
            {
                resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct);
            }
            catch (Exception ex) when ((ex is HttpRequestException || ex is TaskCanceledException) && !ct.IsCancellationRequested)
            {
                Debug.WriteLine($"[Supabase.delete.service] region {i} unreachable {table}?{query}: {ex.Message}");
                lastError = ex;
                continue;
            }

            using (resp)
            {
                if (!resp.IsSuccessStatusCode)
                {
                    var body = await resp.Content.ReadAsStringAsync(ct);
                    Debug.WriteLine($"[Supabase.delete.service] FAIL {(int)resp.StatusCode} region {i} {table}?{query}: {Truncate(body, 256)}");
                    lastError = new InvalidOperationException(
                        $"Supabase service-role DELETE failed [{(int)resp.StatusCode}]: {Truncate(body, 200)}");
                    if ((int)resp.StatusCode >= 500) continue;
                    throw lastError;
                }

                int deleted = 0;
                if (resp.Content.Headers.TryGetValues("Content-Range", out var ranges))
                {
                    var first = ranges.FirstOrDefault();
                    if (first is not null)
                    {
                        var slash = first.IndexOf('/');
                        if (slash > 0 && int.TryParse(first.AsSpan(slash + 1), out var total)) deleted = total;
                    }
                }
                Debug.WriteLine($"[Supabase.delete.service] OK {(int)resp.StatusCode} region {i} {table}?{query} - deleted={deleted}");
                return deleted;
            }
        }

        throw lastError ?? new InvalidOperationException(
            $"Supabase service-role DELETE failed: no region reachable for {table}?{query}.");
    }

    public async Task<string> UploadAvatarViaApiAsync(
        string userId,
        string localPath,
        string ext,
        CancellationToken ct = default)
    {
        if (!File.Exists(localPath))
            throw new FileNotFoundException(Loc.T("error.fileNotFound"), localPath);

        var bytes = await File.ReadAllBytesAsync(localPath, ct);
        var payload = new
        {
            userId,
            ext,
            data = Convert.ToBase64String(bytes),
        };

        var baseUrl = BaseUrls[0].TrimEnd('/');
        var url = $"{baseUrl}/api/avatar/upload";
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(payload, options: Json),
        };
        req.Headers.Remove("apikey");
        req.Headers.Authorization = null;

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            Debug.WriteLine($"[avatar.api] FAIL {(int)resp.StatusCode}: {Truncate(raw, 200)}");
            throw new InvalidOperationException(
                $"vps-api avatar upload failed [{(int)resp.StatusCode}]: {Truncate(raw, 200)}");
        }
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var urlOut = doc.RootElement.GetProperty("url").GetString();
            if (string.IsNullOrWhiteSpace(urlOut))
                throw new InvalidOperationException("vps-api returned empty url");
            return urlOut!;
        }
        catch (JsonException jex)
        {
            throw new InvalidOperationException("vps-api returned non-JSON body for avatar upload", jex);
        }
    }

    private static readonly JsonSerializerOptions AiJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = global::System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<MiamiGraphics.Bridge.PcDiagAiResultDto> PcDiagAiAsync(object payload, CancellationToken ct = default)
    {
        var baseUrl = BaseUrls[0].TrimEnd('/');
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/pcdiag/ai")
        {
            Content = JsonContent.Create(payload, options: AiJson),
        };
        req.Headers.Remove("apikey");
        req.Headers.Authorization = null;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(90));

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, cts.Token);
        var raw = await resp.Content.ReadAsStringAsync(cts.Token);
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (resp.IsSuccessStatusCode && doc.RootElement.TryGetProperty("text", out var t))
                return new MiamiGraphics.Bridge.PcDiagAiResultDto(true, t.GetString() ?? "", "");
            var err = doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() ?? "ai_failed" : "ai_failed";
            return new MiamiGraphics.Bridge.PcDiagAiResultDto(false, "", err);
        }
        catch (JsonException)
        {
            return new MiamiGraphics.Bridge.PcDiagAiResultDto(false, "", "ai_failed");
        }
    }

    public async Task<string> UploadStorageObjectAsync(
        string bucket,
        string key,
        string localPath,
        string contentType,
        string serviceRoleKey,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(serviceRoleKey))
            throw new InvalidOperationException("service-role key not configured");
        if (!File.Exists(localPath))
            throw new FileNotFoundException(Loc.T("error.fileNotFound"), localPath);

        var url = $"{DirectUrl.TrimEnd('/')}/storage/v1/object/{bucket}/{key.TrimStart('/')}";
        await using var stream = File.OpenRead(localPath);
        using var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        req.Headers.Remove("apikey");
        req.Headers.Add("apikey", serviceRoleKey);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceRoleKey);
        req.Headers.Add("x-upsert", "true");

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            Debug.WriteLine($"[Supabase.storage.upload] FAIL {(int)resp.StatusCode} {bucket}/{key}: {Truncate(body, 256)}");
            throw new InvalidOperationException(
                $"Supabase storage upload failed [{(int)resp.StatusCode}]: {Truncate(body, 200)}");
        }
        Debug.WriteLine($"[Supabase.storage.upload] OK {bucket}/{key}");
        return StoragePublicUrl(bucket, key);
    }

    public async Task UpdateAsync<T>(string table, string query, T patch, CancellationToken ct = default)
    {
        await SendAsync<object?>(HttpMethod.Patch, $"rest/v1/{table}?{query}",
            content: () =>
            {
                var c = JsonContent.Create(patch, options: Json);
                c.Headers.Add("Prefer", "return=minimal");
                return c;
            }, ct,
            handle: _ => Task.FromResult<object?>(null));
    }

    public async Task InsertWithServiceRoleAsync<T>(
        string table, T row, string serviceRoleKey, CancellationToken ct = default)
    {
        var url = $"{Url.TrimEnd('/')}/rest/v1/{table}";
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(row, options: Json),
        };
        ApplyAdminWriteAuth(req);
        req.Headers.Add("Prefer", "return=minimal");

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            Debug.WriteLine($"[Supabase.insert.service] FAIL {(int)resp.StatusCode} {table}: {Truncate(body, 256)}");
            throw new InvalidOperationException(
                $"Supabase service-role INSERT failed [{(int)resp.StatusCode}]: {Truncate(body, 200)}");
        }
        Debug.WriteLine($"[Supabase.insert.service] OK {(int)resp.StatusCode} {table}");
    }

    public async Task UpsertWithServiceRoleAsync<T>(
        string table, T row, string serviceRoleKey, CancellationToken ct = default)
    {
        var url = $"{Url.TrimEnd('/')}/rest/v1/{table}";
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new[] { row }, options: Json),
        };
        ApplyAdminWriteAuth(req);
        req.Headers.Add("Prefer", "resolution=merge-duplicates,return=minimal");

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            Debug.WriteLine($"[Supabase.upsert.service] FAIL {(int)resp.StatusCode} {table}: {Truncate(body, 256)}");
            throw new InvalidOperationException(
                $"Supabase service-role UPSERT failed [{(int)resp.StatusCode}]: {Truncate(body, 200)}");
        }
        Debug.WriteLine($"[Supabase.upsert.service] OK {(int)resp.StatusCode} {table}");
    }

    public async Task UpsertManyWithServiceRoleAsync<T>(
        string table, IEnumerable<T> rows, string serviceRoleKey, CancellationToken ct = default)
    {
        var list = rows as IReadOnlyCollection<T> ?? rows.ToList();
        if (list.Count == 0) return;

        var url = $"{Url.TrimEnd('/')}/rest/v1/{table}";
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(list, options: BulkJson),
        };
        ApplyAdminWriteAuth(req);
        req.Headers.Add("Prefer", "resolution=merge-duplicates,return=minimal");

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            Debug.WriteLine($"[Supabase.upsertMany.service] FAIL {(int)resp.StatusCode} {table} ({list.Count}): {Truncate(body, 256)}");
            throw new InvalidOperationException(
                $"Supabase service-role bulk UPSERT failed [{(int)resp.StatusCode}]: {Truncate(body, 200)}");
        }
        Debug.WriteLine($"[Supabase.upsertMany.service] OK {(int)resp.StatusCode} {table} ({list.Count} rows)");
    }

    public async Task<int> UpdateWithServiceRoleAsync<T>(
        string table, string query, T patch, string serviceRoleKey, CancellationToken ct = default)
    {
        var url = $"{Url.TrimEnd('/')}/rest/v1/{table}?{query}";
        using var req = new HttpRequestMessage(HttpMethod.Patch, url)
        {
            Content = JsonContent.Create(patch, options: Json),
        };
        ApplyAdminWriteAuth(req);
        req.Headers.Add("Prefer", "return=minimal,count=exact");

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            Debug.WriteLine($"[Supabase.patch.service] FAIL {(int)resp.StatusCode} {table}?{query}: {Truncate(body, 256)}");
            throw new InvalidOperationException(
                $"Supabase service-role PATCH failed [{(int)resp.StatusCode}]: {Truncate(body, 200)}");
        }
        int updated = 0;
        if (resp.Content.Headers.TryGetValues("Content-Range", out var ranges))
        {
            var first = ranges.FirstOrDefault();
            if (first is not null)
            {
                var slash = first.IndexOf('/');
                if (slash > 0 && int.TryParse(first.AsSpan(slash + 1), out var total)) updated = total;
            }
        }
        Debug.WriteLine($"[Supabase.patch.service] OK {(int)resp.StatusCode} {table}?{query} - updated={updated}");
        return updated;
    }

    private static readonly JsonSerializerOptions RpcJson = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition      = JsonIgnoreCondition.Never,
    };

    public Task<T?> RpcSingleAsync<T>(string fn, object args, CancellationToken ct = default) where T : class
    {
        return SendAsync(HttpMethod.Post, $"rest/v1/rpc/{fn}",
            content: () => JsonContent.Create(args, options: RpcJson), ct,
            handle: async resp =>
            {
                await using var stream = await resp.Content.ReadAsStreamAsync(ct);
                var list = await JsonSerializer.DeserializeAsync<List<T>>(stream, Json, ct);
                return list?.FirstOrDefault();
            });
    }

    public Task<System.Text.Json.Nodes.JsonNode?> RpcJsonAsync(string fn, object args, CancellationToken ct = default)
    {
        return SendAsync(HttpMethod.Post, $"rest/v1/rpc/{fn}",
            content: () => JsonContent.Create(args, options: RpcJson), ct,
            handle: async resp =>
            {
                await using var stream = await resp.Content.ReadAsStreamAsync(ct);
                return await System.Text.Json.Nodes.JsonNode.ParseAsync(stream, cancellationToken: ct);
            });
    }

    public async Task RpcVoidAsync(string fn, object args, CancellationToken ct = default)
    {
        await SendAsync<object?>(HttpMethod.Post, $"rest/v1/rpc/{fn}",
            content: () => JsonContent.Create(args, options: RpcJson), ct,
            handle: _ => Task.FromResult<object?>(null));
    }

    public Task<List<T>> RpcManyAsync<T>(string fn, object args, CancellationToken ct = default)
    {
        return SendAsync(HttpMethod.Post, $"rest/v1/rpc/{fn}",
            content: () => JsonContent.Create(args, options: RpcJson), ct,
            handle: async resp =>
            {
                await using var stream = await resp.Content.ReadAsStreamAsync(ct);
                var list = await JsonSerializer.DeserializeAsync<List<T>>(stream, Json, ct);
                return list ?? new List<T>();
            });
    }

    public async Task<string?> UploadCustomGunFileAsync(byte[] bytes, string kind, CancellationToken ct = default)
    {
        if (bytes is not { Length: > 0 }) throw new ArgumentException("empty payload", nameof(bytes));

        var relative = $"api/customgun/upload?kind={Uri.EscapeDataString(kind)}";
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMinutes(5));

        string? lastError = null;
        for (var i = 0; i < BaseUrls.Length; i++)
        {
            var requestUrl = AbsoluteUrl(BaseUrls[i], relative);
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, requestUrl);
                var body = new ByteArrayContent(bytes);
                body.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                req.Content = body;
                AttachSessionToken(req);

                using var resp = await UploadHttp.SendAsync(req, HttpCompletionOption.ResponseContentRead, cts.Token);
                var raw = await resp.Content.ReadAsStringAsync(cts.Token);
                if (resp.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(raw);
                    return doc.RootElement.TryGetProperty("url", out var u) ? u.GetString() : null;
                }

                lastError = $"vps-api customgun upload [{(int)resp.StatusCode}]: {ProxyErrorText(raw)}";
                Debug.WriteLine($"[customgun.upload] {requestUrl} FAIL {lastError}");
                if ((int)resp.StatusCode < 500) break;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException ex)
            {
                lastError = Loc.T("error.skinUploadTimeout", ("mb", bytes.Length / 1024 / 1024));
                Debug.WriteLine($"[customgun.upload] {requestUrl} TIMEOUT: {ex.Message}");
            }
            catch (HttpRequestException ex)
            {
                lastError = Loc.T("error.uploadServerUnreachable", ("reason", ex.Message));
                Debug.WriteLine($"[customgun.upload] {requestUrl} NETWORK: {ex.Message}");
            }
        }

        throw new InvalidOperationException(lastError ?? "vps-api customgun upload failed.");
    }

    private static string ProxyErrorText(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String)
                return e.GetString() ?? Truncate(raw, 200);
        }
        catch { }
        return Truncate(raw, 200);
    }

    private static readonly HttpClient UploadHttp = new(new FragmentingHttpHandler())
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    private enum RetryScope
    {
        Repeatable,

        PreSendOnly,
    }

    private static RetryScope ClassifyRetry(HttpMethod method, string url)
    {
        if (method == HttpMethod.Get || method == HttpMethod.Head ||
            method == HttpMethod.Delete || method == HttpMethod.Patch)
            return RetryScope.Repeatable;

        var path = url.TrimStart('/');
        if (!path.StartsWith("rest/v1/", StringComparison.Ordinal)) return RetryScope.PreSendOnly;

        var rest = path.Substring("rest/v1/".Length);
        if (rest.StartsWith("rpc/", StringComparison.Ordinal))
            return IdempotentRpcs.Contains(rest.Substring(4).Split('?')[0])
                ? RetryScope.Repeatable
                : RetryScope.PreSendOnly;

        return RetryScope.Repeatable;
    }

    private readonly record struct HostAttempt(
        HttpResponseMessage? Response,
        Exception? Error,
        bool CapFired,
        bool RequestSent);

    private async Task<HostAttempt> SendOnHostAsync(
        string baseUrl,
        HttpMethod method,
        string url,
        Func<HttpContent>? content,
        CancellationToken ct,
        Action<HttpRequestMessage>? extraHeaders,
        RetryScope scope,
        TimeSpan? budget)
    {
        using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var sent = 0;

        using var req = new HttpRequestMessage(method, AbsoluteUrl(baseUrl, url));
        if (content is not null)
        {
            req.Content = new SendProbeContent(content(), () =>
            {
                Interlocked.Exchange(ref sent, 1);
                if (scope == RetryScope.PreSendOnly)
                {
                    attemptCts.CancelAfter(ResponseWaitBudget);
                }
            });
        }
        extraHeaders?.Invoke(req);
        AttachAdminToken(req, url, method);
        AttachSessionToken(req);

        if (budget is { } b) attemptCts.CancelAfter(b);

        HttpResponseMessage? resp = null;
        try
        {
            resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, attemptCts.Token);

            attemptCts.CancelAfter(BodyReadBudget);
            var body = await resp.Content.ReadAsByteArrayAsync(attemptCts.Token);
            var buffered = new ByteArrayContent(body);
            foreach (var h in resp.Content.Headers)
            {
                if (string.Equals(h.Key, "Content-Length", StringComparison.OrdinalIgnoreCase)) continue;
                buffered.Headers.TryAddWithoutValidation(h.Key, h.Value);
            }
            resp.Content.Dispose();
            resp.Content = buffered;
            var handedOver = resp;
            resp = null;
            return new HostAttempt(handedOver, null, false, true);
        }
        catch (Exception ex) when ((ex is HttpRequestException or OperationCanceledException)
                                   && !ct.IsCancellationRequested)
        {
            var requestSent = Volatile.Read(ref sent) == 1;
            if (ex is HttpRequestException hre && IsProvablyPreSendError(hre)) requestSent = false;
            return new HostAttempt(null, ex, attemptCts.IsCancellationRequested, requestSent);
        }
        finally
        {
            resp?.Dispose();
        }
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string url,
        Func<HttpContent>? content,
        CancellationToken ct,
        Func<HttpResponseMessage, Task<T>> handle,
        Action<HttpRequestMessage>? extraHeaders = null)
    {
        var scope = ClassifyRetry(method, url);
        var regionHosts = BaseUrls;
        var regionPrimary = regionHosts[0];
        var hosts = OrderByStickyWinner(regionHosts);

        TimeSpan? chainBudget = hosts.Length > 1 ? HostAttemptBudget : (TimeSpan?)null;

        var plan = new List<(string Host, TimeSpan? Budget)>();
        foreach (var h in hosts) plan.Add((h, chainBudget));
        var lastResortQueued = false;

        Exception? lastError = null;
        var capFired   = false;
        var allPreSend = true;

        for (var i = 0; i < plan.Count; i++)
        {
            var step = plan[i];
            var attempt = await SendOnHostAsync(step.Host, method, url, content, ct, extraHeaders, scope, step.Budget);

            var mayMoveOn = true;

            if (attempt.Error is not null)
            {
                lastError = attempt.Error;
                capFired |= attempt.CapFired;
                if (attempt.RequestSent) allPreSend = false;
                Debug.WriteLine($"[Supabase] {method} {url} @{step.Host} FAIL {attempt.Error.GetType().Name} " +
                                $"(наш потолок: {attempt.CapFired}, запрос ушёл: {attempt.RequestSent}): {attempt.Error.Message}");

                mayMoveOn = scope == RetryScope.Repeatable || !attempt.RequestSent;
                if (!mayMoveOn)
                    Debug.WriteLine($"[Supabase] {method} {url} - запасной хост запрещён: запрос уже ушёл, сервер мог его применить");
            }
            else
            {
                var resp = attempt.Response!;
                try
                {
                    if (resp.IsSuccessStatusCode)
                    {
                        if (i > 0) Debug.WriteLine($"[Supabase] {method} {url} OK на запасном {step.Host}");
                        NoteHostWin(step.Host, regionPrimary);
                        return await handle(resp);
                    }

                    var status = (int)resp.StatusCode;
                    var body = await resp.Content.ReadAsStringAsync(ct);
                    Debug.WriteLine($"[Supabase] {method} {url} @{step.Host} {status} {resp.ReasonPhrase} :: {body}");

                    var mapped = BuildResponseException(resp.StatusCode, status, resp.ReasonPhrase, body);

                    var frontDown = status == 502 || status == 503;
                    if (!frontDown || scope != RetryScope.Repeatable) throw mapped;

                    lastError = mapped;
                    allPreSend = false;
                }
                finally
                {
                    resp.Dispose();
                }
            }

            if (!mayMoveOn) break;

            if (i == plan.Count - 1 && !lastResortQueued && chainBudget is not null
                && lastError is not null
                && (scope == RetryScope.Repeatable || allPreSend)
                && (capFired || allPreSend))
            {
                lastResortQueued = true;
                plan.Add((hosts[0], scope == RetryScope.Repeatable ? (TimeSpan?)null : HostAttemptBudget));
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }
        }

        if (lastError is SupabaseException already) throw already;

        var timedOut = lastError is OperationCanceledException;
        throw new SupabaseException(SupabaseErrorKind.Network,
            timedOut
                ? Loc.T("error.noHostRespondedInTime", ("hosts", string.Join(", ", hosts)))
                : $"Supabase host unreachable: {lastError?.Message}",
            inner: lastError);
    }

    private static SupabaseException BuildResponseException(
        HttpStatusCode statusCode, int status, string? reason, string body)
    {
        var notProvisioned =
            statusCode == HttpStatusCode.NotFound ||
            body.Contains("PGRST202", StringComparison.Ordinal) ||
            body.Contains("PGRST116", StringComparison.Ordinal) ||
            body.Contains("42P01",    StringComparison.Ordinal) ||
            body.Contains("42883",    StringComparison.Ordinal);

        var kind = statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => SupabaseErrorKind.Unauthorized,
            HttpStatusCode.Conflict                                 => SupabaseErrorKind.Conflict,
            HttpStatusCode.BadRequest                               => SupabaseErrorKind.BadRequest,
            _ when notProvisioned                                  => SupabaseErrorKind.NotProvisioned,
            _                                                      => SupabaseErrorKind.Server,
        };

        return new SupabaseException(kind,
            kind switch
            {
                SupabaseErrorKind.Unauthorized   => "Supabase rejected the API key (401/403). Check anon key and RLS policies.",
                SupabaseErrorKind.NotProvisioned => "Supabase schema is not provisioned. Apply supabase/migrations/0001_initial.sql in the Supabase SQL Editor, then retry.",
                SupabaseErrorKind.Conflict       => $"Supabase 409 Conflict: {Truncate(body, 256)}",
                SupabaseErrorKind.BadRequest     => $"Supabase 400 Bad Request: {Truncate(body, 256)}",
                _                                => $"Supabase {status} {reason}: {Truncate(body, 256)}",
            },
            statusCode: status);
    }

    private static bool IsProvablyPreSendError(HttpRequestException ex)
        => ex.HttpRequestError is HttpRequestError.SecureConnectionError
            or HttpRequestError.NameResolutionError;

    private static void AttachSessionToken(HttpRequestMessage req)
    {
        var token = UserSessionToken;
        if (!string.IsNullOrEmpty(token))
            req.Headers.TryAddWithoutValidation("x-mg-session", token);
    }

    private static void AttachAdminToken(HttpRequestMessage req, string url, HttpMethod method)
    {
        if (string.IsNullOrEmpty(AdminApiToken)) return;
        if (method == HttpMethod.Get || method == HttpMethod.Head) return;
        var path = url.TrimStart('/');
        if (!path.StartsWith("rest/v1/", StringComparison.Ordinal)) return;
        var rest = path.Substring("rest/v1/".Length);
        bool needs = rest.StartsWith("rpc/", StringComparison.Ordinal)
            ? AdminRpcs.Contains(rest.Substring(4).Split('?')[0])
            : AdminWriteTables.Contains(rest.Split('?')[0]);
        if (needs)
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminApiToken);
    }

    private static void ApplyAdminWriteAuth(HttpRequestMessage req)
    {
        if (string.IsNullOrWhiteSpace(AdminApiToken))
            throw new InvalidOperationException(
                "Токен админ-доступа не задан. Откройте Admin → Настройки и впишите «Admin API token» " +
                "(он же ADMIN_API_TOKEN на vps-api). Как запасной вариант - переменная окружения HG_ADMIN_API_TOKEN.");
        req.Headers.Remove("apikey");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminApiToken);
    }

    private static string AbsoluteUrl(string baseUrl, string relativeUrl) =>
        $"{baseUrl.TrimEnd('/')}/{relativeUrl.TrimStart('/')}";

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max) + "…";
}

internal sealed class SendProbeContent : HttpContent
{
    private readonly HttpContent _inner;
    private readonly Action _onFirstWrite;
    private int _fired;

    public SendProbeContent(HttpContent inner, Action onFirstWrite)
    {
        _inner = inner;
        _onFirstWrite = onFirstWrite;
        foreach (var h in inner.Headers) Headers.TryAddWithoutValidation(h.Key, h.Value);
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        Fire();
        return _inner.CopyToAsync(stream);
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken ct)
    {
        Fire();
        return _inner.CopyToAsync(stream, ct);
    }

    protected override bool TryComputeLength(out long length)
    {
        var known = _inner.Headers.ContentLength;
        length = known ?? 0;
        return known.HasValue;
    }

    private void Fire()
    {
        if (Interlocked.Exchange(ref _fired, 1) == 0) _onFirstWrite();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _inner.Dispose();
        base.Dispose(disposing);
    }
}
