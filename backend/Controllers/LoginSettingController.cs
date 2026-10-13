using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;

namespace ahu.MicrosoftMes.Controllers;

/// <summary>登录页设置（docs/69）：左侧大图按工厂自配。读取免鉴权，上传仅管理员。</summary>
[ApiController]
[Route("api/[controller]")]
public class LoginSettingController : ControllerBase
{
    private const long MaxBytes = 5 * 1024 * 1024;
    private static readonly string[] AllowedExt = { ".png", ".jpg", ".jpeg", ".webp" };

    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;

    public LoginSettingController(AppDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }

    /// <summary>登录页读取：按工厂返回 bannerUrl；未配置返回 null。带 ?v= 时间戳防浏览器缓存。</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ApiResult<object?>> Get([FromQuery] string? factoryCode)
    {
        string? bannerUrl = null;
        if (!string.IsNullOrWhiteSpace(factoryCode))
        {
            var factoryId = await _db.Factories.AsNoTracking()
                .Where(f => f.FactoryCode == factoryCode.Trim())
                .Select(f => (long?)f.Id)
                .FirstOrDefaultAsync();
            if (factoryId != null)
            {
                var s = await _db.LoginSettings.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.FactoryId == factoryId.Value);
                // DB 有路径但物理文件被清掉（如重建 wwwroot）时返回 null，前端回退默认图，避免裂图
                if (s != null && !string.IsNullOrWhiteSpace(s.BannerUrl) && BannerFileExists(s.BannerUrl))
                    bannerUrl = $"{s.BannerUrl}?v={s.UpdatedAt.Ticks}";
            }
        }
        return ApiResult<object?>.Ok(new { bannerUrl });
    }

    /// <summary>管理员上传/替换本账套登录图。覆盖旧图（清理任意扩展名的旧文件）。</summary>
    [HttpPost("banner")]
    [Authorize]
    public async Task<ApiResult<object?>> Upload(IFormFile? file)
    {
        if (file == null || file.Length == 0)
            throw ThrowHelper.Biz(nameof(Upload), "上传文件为空");
        if (file.Length > MaxBytes)
            throw ThrowHelper.Biz(nameof(Upload), "图片不能超过 5MB");

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExt.Contains(ext))
            throw ThrowHelper.Biz(nameof(Upload), "仅支持 png / jpg / jpeg / webp 图片");
        if (!(file.ContentType ?? "").StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            throw ThrowHelper.Biz(nameof(Upload), "仅支持图片文件");

        var factoryId = JwtHelper.GetFactoryId(User);

        var root = _env.WebRootPath;
        if (string.IsNullOrWhiteSpace(root))
            root = Path.Combine(_env.ContentRootPath, "wwwroot");
        var uploadDir = Path.Combine(root, "uploads", "login");
        Directory.CreateDirectory(uploadDir);

        // 覆盖旧图：删该工厂任意扩展名的旧 banner，再写新文件
        foreach (var old in Directory.GetFiles(uploadDir, $"{factoryId}.*"))
            System.IO.File.Delete(old);

        var newFileName = $"{factoryId}{ext}";
        var fullPath = Path.Combine(uploadDir, newFileName);
        await using (var fs = new FileStream(fullPath, FileMode.Create))
            await file.CopyToAsync(fs);

        var now = DateTime.Now;
        var relative = $"/uploads/login/{newFileName}";
        var setting = await _db.LoginSettings.FirstOrDefaultAsync(x => x.FactoryId == factoryId);
        if (setting == null)
        {
            setting = new LoginSetting { FactoryId = factoryId, BannerUrl = relative, UpdatedAt = now };
            _db.LoginSettings.Add(setting);
        }
        else
        {
            setting.BannerUrl = relative;
            setting.UpdatedAt = now;
        }
        await _db.SaveChangesAsync();

        return ApiResult<object?>.Ok(new { bannerUrl = $"{relative}?v={now.Ticks}" });
    }

    private string ResolveWebRoot()
    {
        var root = _env.WebRootPath;
        if (string.IsNullOrWhiteSpace(root))
            root = Path.Combine(_env.ContentRootPath, "wwwroot");
        return root;
    }

    private bool BannerFileExists(string relativeUrl)
    {
        var rel = relativeUrl.Trim().TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        if (rel.Contains("..", StringComparison.Ordinal))
            return false;
        var full = Path.GetFullPath(Path.Combine(ResolveWebRoot(), rel));
        var rootFull = Path.GetFullPath(ResolveWebRoot());
        if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            return false;
        return System.IO.File.Exists(full);
    }
}
