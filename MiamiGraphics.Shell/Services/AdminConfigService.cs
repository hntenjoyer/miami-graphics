using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace MiamiGraphics.Shell.Services;

public sealed record AdminConfig(
    string R2Endpoint,
    string R2Bucket,
    string R2PublicUrl,
    string R2AccessKey,
    string R2SecretKey,
    string CleanUpdateRpfPath,
    string? GtaPathOverride,
    string? WorkDirOverride = null,
    string SupabaseServiceKey = "",
    string AdminApiToken = ""
)
{
    public static AdminConfig Default { get; } = new(
        R2Endpoint: System.Environment.GetEnvironmentVariable("MG_R2_ENDPOINT") ?? "",
        R2Bucket:   "huntergraphics",
        R2PublicUrl:"https://miamigraphicsstorage.uk",
        R2AccessKey: "",
        R2SecretKey: "",

        CleanUpdateRpfPath: string.Empty,
        GtaPathOverride: null,

        WorkDirOverride: null,
        SupabaseServiceKey: "",
        AdminApiToken: ""
    );
}

public sealed record TestConnectionResult(bool Success, string Message, int? ObjectCount);

public interface IAdminConfigService
{
    Task<AdminConfig> GetAsync();
    Task SaveAsync(AdminConfig config);
    Task<TestConnectionResult> TestR2ConnectionAsync(AdminConfig config, CancellationToken ct);
}

public sealed class AdminConfigService : IAdminConfigService
{
    private const string FileName = "admin-config.bin";
    private readonly string _filePath;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public AdminConfigService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(appData, "MiamiGraphics");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, FileName);
    }

    public Task<AdminConfig> GetAsync()
    {
        var cfg = LoadConfig();
        SupabaseClient.AdminApiTokenOverride = cfg.AdminApiToken;
        return Task.FromResult(cfg);
    }

    private AdminConfig LoadConfig()
    {
        if (!File.Exists(_filePath)) return AdminConfig.Default;
        try
        {
            var enc = File.ReadAllBytes(_filePath);
            var raw = ProtectedData.Unprotect(enc, null, DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(raw);
            return JsonSerializer.Deserialize<AdminConfig>(json, Json) ?? AdminConfig.Default;
        }
        catch
        {
            return AdminConfig.Default;
        }
    }

    public Task SaveAsync(AdminConfig config)
    {
        var json = JsonSerializer.Serialize(config, Json);
        var raw = Encoding.UTF8.GetBytes(json);
        var enc = ProtectedData.Protect(raw, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_filePath, enc);
        SupabaseClient.AdminApiTokenOverride = config.AdminApiToken;
        return Task.CompletedTask;
    }

    public async Task<TestConnectionResult> TestR2ConnectionAsync(AdminConfig config, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(config.R2AccessKey) || string.IsNullOrWhiteSpace(config.R2SecretKey))
            return new TestConnectionResult(false, "Access Key and Secret Key are required.", null);

        try
        {
            var creds = new BasicAWSCredentials(config.R2AccessKey, config.R2SecretKey);
            var s3Cfg = new AmazonS3Config
            {
                ServiceURL = config.R2Endpoint,
                ForcePathStyle = true,
                AuthenticationRegion = "auto",
            };
            using var client = new AmazonS3Client(creds, s3Cfg);
            var resp = await client.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = config.R2Bucket,
                MaxKeys = 1,
            }, ct);

            string corsNote = string.Empty;
            try
            {
                var corsReq = new PutCORSConfigurationRequest
                {
                    BucketName = config.R2Bucket,
                    Configuration = new CORSConfiguration
                    {
                        Rules = new List<CORSRule>
                        {
                            new CORSRule
                            {
                                Id             = "hntgraph-public-read",
                                AllowedOrigins = new List<string> { "*" },
                                AllowedMethods = new List<string> { "GET", "HEAD" },
                                AllowedHeaders = new List<string> { "*" },
                                ExposeHeaders  = new List<string> { "Content-Length", "Content-Range", "ETag" },
                                MaxAgeSeconds  = 3600,
                            },
                        },
                    },
                };
                await client.PutCORSConfigurationAsync(corsReq, ct);
                corsNote = " · CORS applied";
                Debug.WriteLine("[r2.test] CORS rule applied to bucket");
            }
            catch (Exception cex)
            {
                var msg = cex.Message ?? string.Empty;
                var isAccessDenied = msg.IndexOf("Access Denied", StringComparison.OrdinalIgnoreCase) >= 0
                                  || msg.IndexOf("AccessDenied",  StringComparison.OrdinalIgnoreCase) >= 0;
                if (isAccessDenied)
                {

                    Debug.WriteLine("[r2.test] CORS apply skipped (token lacks s3:PutBucketCors - assuming dashboard config).");
                    corsNote = " · CORS не управляется этим токеном (используется dashboard-конфиг)";
                }
                else
                {
                    Debug.WriteLine($"[r2.test] CORS apply FAILED: {cex.Message}");
                    corsNote = " · CORS NOT applied (set manually in Cloudflare → R2 → Settings → CORS)";
                }
            }

            return new TestConnectionResult(true, $"Connected.{corsNote}", resp.KeyCount);
        }
        catch (AmazonS3Exception aex)
        {
            return new TestConnectionResult(false, $"S3: {aex.ErrorCode} - {aex.Message}", null);
        }
        catch (Exception ex)
        {
            return new TestConnectionResult(false, ex.Message, null);
        }
    }
}
