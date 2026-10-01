using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
var storeOpt = builder.Configuration.GetSection("LicenseStore").Get<LicenseStoreOptions>() ?? new LicenseStoreOptions();
builder.Services.AddSingleton(storeOpt);
builder.Services.AddSingleton<LicenseRegistry>();

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new { service = "WorkshopMes LicenseServer", ok = true }));

app.MapPost("/api/license/check", (LicenseCheckRequest req, HttpRequest http, LicenseRegistry registry, LicenseStoreOptions opt) =>
{
    if (!string.IsNullOrWhiteSpace(opt.SharedSecret))
    {
        var key = http.Headers["X-License-Key"].FirstOrDefault();
        if (!string.Equals(key, opt.SharedSecret, StringComparison.Ordinal))
        {
            return Results.Json(new LicenseApiResult<LicenseCheckData>
            {
                Code = 401,
                Msg = "授权密钥无效"
            }, statusCode: 401);
        }
    }

    var factoryCode = (req.FactoryCode ?? "").Trim();
    if (string.IsNullOrEmpty(factoryCode))
    {
        return Results.Json(new LicenseApiResult<LicenseCheckData>
        {
            Code = 1,
            Msg = "factoryCode 不能为空"
        });
    }

    var serverUtc = DateTime.UtcNow;
    var entry = registry.Find(factoryCode);
    if (entry == null)
    {
        return Results.Json(new LicenseApiResult<LicenseCheckData>
        {
            Code = 0,
            Msg = "ok",
            Data = new LicenseCheckData
            {
                FactoryCode = factoryCode,
                LicenseTier = "trial",
                ExpiresAtUtc = null,
                ServerUtc = serverUtc,
                Status = "unknown"
            }
        });
    }

    var tier = NormalizeTier(entry.LicenseTier);
    var expired = entry.ExpiresAtUtc.HasValue && serverUtc > entry.ExpiresAtUtc.Value.ToUniversalTime();
    return Results.Json(new LicenseApiResult<LicenseCheckData>
    {
        Code = 0,
        Msg = "ok",
        Data = new LicenseCheckData
        {
            FactoryCode = entry.FactoryCode,
            LicenseTier = tier,
            ExpiresAtUtc = entry.ExpiresAtUtc?.ToUniversalTime(),
            ServerUtc = serverUtc,
            Status = expired ? "expired" : "valid"
        }
    });
});

app.Run();

static string NormalizeTier(string? raw) => (raw ?? "").Trim().ToLowerInvariant() switch
{
    "enterprise" => "enterprise",
    "flagship" => "flagship",
    _ => "trial"
};

file sealed class LicenseStoreOptions
{
    public string FilePath { get; set; } = "licenses.json";
    public string SharedSecret { get; set; } = "";
}

file sealed class LicenseEntry
{
    [JsonPropertyName("factoryCode")]
    public string FactoryCode { get; set; } = "";

    [JsonPropertyName("licenseTier")]
    public string LicenseTier { get; set; } = "trial";

    [JsonPropertyName("expiresAtUtc")]
    public DateTime? ExpiresAtUtc { get; set; }
}

file sealed class LicenseRegistry
{
    private readonly string _path;
    private readonly object _lock = new();
    private List<LicenseEntry> _entries = new();
    private DateTime _loadedAt = DateTime.MinValue;

    public LicenseRegistry(LicenseStoreOptions opt, IWebHostEnvironment env)
    {
        _path = Path.IsPathRooted(opt.FilePath)
            ? opt.FilePath
            : Path.Combine(env.ContentRootPath, opt.FilePath);
        Reload();
    }

    public LicenseEntry? Find(string factoryCode)
    {
        ReloadIfStale();
        lock (_lock)
        {
            return _entries.FirstOrDefault(e =>
                string.Equals(e.FactoryCode, factoryCode, StringComparison.OrdinalIgnoreCase));
        }
    }

    private void ReloadIfStale()
    {
        // 热读：文件变更或超过 30 秒再读
        try
        {
            var fi = new FileInfo(_path);
            if (!fi.Exists) return;
            if (fi.LastWriteTimeUtc <= _loadedAt && (DateTime.UtcNow - _loadedAt).TotalSeconds < 30)
                return;
            Reload();
        }
        catch
        {
            // 读失败保留旧缓存；下次再试
        }
    }

    private void Reload()
    {
        if (!File.Exists(_path))
        {
            lock (_lock) { _entries = new List<LicenseEntry>(); _loadedAt = DateTime.UtcNow; }
            return;
        }

        var json = File.ReadAllText(_path);
        var list = JsonSerializer.Deserialize<List<LicenseEntry>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new List<LicenseEntry>();

        lock (_lock)
        {
            _entries = list;
            _loadedAt = DateTime.UtcNow;
        }
    }
}

file sealed class LicenseCheckRequest
{
    public string? FactoryCode { get; set; }
    public DateTime? RequestedAtUtc { get; set; }
}

file sealed class LicenseCheckData
{
    public string FactoryCode { get; set; } = "";
    public string LicenseTier { get; set; } = "trial";
    public DateTime? ExpiresAtUtc { get; set; }
    public DateTime ServerUtc { get; set; }
    public string Status { get; set; } = "unknown";
}

file sealed class LicenseApiResult<T>
{
    public int Code { get; set; }
    public string Msg { get; set; } = "ok";
    public T? Data { get; set; }
}
