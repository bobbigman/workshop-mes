using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Services;
using Microsoft.AspNetCore.Mvc;

namespace ahu.MicrosoftMes.Controllers;

/// <summary>
/// MCP 授权 H5 页（M3，docs/25）。
/// 不挂 /api 前缀，不被 JWT 拦；由一次性配对码（code）约束访问。
/// GET 显示金蝶账号密码表单；POST 验证金蝶 → 换 auth_token → 页面显示授权码。
/// </summary>
[Route("mcp-auth")]
public class McpAuthController : ControllerBase
{
    private readonly IMcpAuthService _authService;
    private readonly IKingdeeAuthService _kingdeeService;
    private readonly IConfiguration _config;

    public McpAuthController(IMcpAuthService authService, IKingdeeAuthService kingdeeService, IConfiguration config)
    {
        _authService = authService;
        _kingdeeService = kingdeeService;
        _config = config;
    }

    [HttpGet]
    public async Task<ContentResult> Index([FromQuery] string code)
    {
        code = (code ?? "").Trim();
        if (string.IsNullOrEmpty(code))
            return Content(ErrorPage("链接缺少配对码。请回到豆包对话，再说一次「查工单」，打开新链接。"), "text/html; charset=utf-8");

        var factoryId = await _authService.ResolveFactoryIdByCodeAsync(code);
        // 打开页时先验配对码，避免填完账号才发现已过期
        try
        {
            await _authService.PeekCodeAsync(code, factoryId);
        }
        catch (BusinessException ex)
        {
            return Content(ErrorPage(ex.Message), "text/html; charset=utf-8");
        }

        var html = $$"""
<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>车间系统授权</title>
<style>
  body { font-family: -apple-system, "PingFang SC", "Microsoft YaHei", sans-serif; background:#f5f6f8; display:flex; justify-content:center; align-items:center; min-height:100vh; margin:0; }
  .card { background:#fff; border-radius:12px; padding:28px 24px; width:92%; max-width:360px; box-shadow:0 4px 16px rgba(0,0,0,.08); }
  h2 { margin:0 0 4px; font-size:20px; color:#1f2329; }
  .sub { color:#8a919f; font-size:13px; margin-bottom:20px; }
  label { display:block; font-size:14px; color:#1f2329; margin-bottom:6px; }
  input { width:100%; box-sizing:border-box; padding:10px 12px; border:1px solid #dcdfe6; border-radius:8px; font-size:15px; margin-bottom:16px; }
  input:focus { outline:none; border-color:#3370ff; }
  button { width:100%; background:#3370ff; color:#fff; border:none; border-radius:8px; padding:12px; font-size:16px; cursor:pointer; }
  button:active { background:#245bdb; }
</style>
</head>
<body>
<div class="card">
  <h2>车间系统授权</h2>
  <div class="sub">演示期：账号密码非空即可（建议账号 boss）。通过后把授权码发回豆包。</div>
  <form method="post" action="/mcp-auth">
    <input type="hidden" name="code" value="{{code}}">
    <label>账号</label>
    <input type="text" name="account" autocomplete="username" required>
    <label>密码</label>
    <input type="password" name="password" autocomplete="current-password" required>
    <button type="submit">验证并授权</button>
  </form>
</div>
</body>
</html>
""";
        return Content(html, "text/html; charset=utf-8");
    }

    [HttpPost]
    public async Task<ContentResult> Submit([FromForm] string code, [FromForm] string account, [FromForm] string password)
    {
        try
        {
            var user = await _kingdeeService.VerifyAsync(account, password);
            var factoryId = await _authService.ResolveFactoryIdByCodeAsync(code);
            var auth = await _authService.ExchangeAsync(code, user, factoryId);

            var html = $$"""
<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>授权成功</title>
<style>
  body { font-family: -apple-system, "PingFang SC", "Microsoft YaHei", sans-serif; background:#f5f6f8; display:flex; justify-content:center; align-items:center; min-height:100vh; margin:0; }
  .card { background:#fff; border-radius:12px; padding:28px 24px; width:92%; max-width:360px; box-shadow:0 4px 16px rgba(0,0,0,.08); text-align:center; }
  h2 { margin:0 0 8px; font-size:20px; color:#1f2329; }
  .sub { color:#8a919f; font-size:13px; margin-bottom:18px; }
  .token { font-size:28px; letter-spacing:4px; font-weight:700; color:#3370ff; background:#f0f5ff; border-radius:8px; padding:14px 8px; margin:12px 0; user-select:all; }
  .hint { color:#8a919f; font-size:13px; line-height:1.6; }
</style>
</head>
<body>
<div class="card">
  <h2>授权成功</h2>
  <div class="sub">金蝶用户：{{auth?.KingdeeUserName}}</div>
  <div class="token">{{auth?.AuthToken}}</div>
  <div class="hint">这是你的授权码。<br>复制后回到豆包对话，把授权码发给我，即可查询工单、报工、工资。</div>
</div>
</body>
</html>
""";
            return Content(html, "text/html; charset=utf-8");
        }
        catch (BusinessException ex)
        {
            return Content(ErrorPage(ex.Message), "text/html; charset=utf-8");
        }
    }

    private static string ErrorPage(string msg)
    {
        var html = $$"""
<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>授权失败</title>
<style>
  body { font-family: -apple-system, "PingFang SC", "Microsoft YaHei", sans-serif; background:#f5f6f8; display:flex; justify-content:center; align-items:center; min-height:100vh; margin:0; }
  .card { background:#fff; border-radius:12px; padding:28px 24px; width:92%; max-width:360px; box-shadow:0 4px 16px rgba(0,0,0,.08); text-align:center; }
  h2 { margin:0 0 12px; font-size:20px; color:#f54a45; }
  .msg { color:#1f2329; font-size:15px; line-height:1.6; }
</style>
</head>
<body>
<div class="card">
  <h2>授权失败</h2>
  <div class="msg">{{msg}}</div>
</div>
</body>
</html>
""";
        return html;
    }
}
