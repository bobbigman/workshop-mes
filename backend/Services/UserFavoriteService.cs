using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;

namespace ahu.MicrosoftMes.Services;

public interface IUserFavoriteService
{
    Task<ApiResult<UserFavoriteListDto>> ListAsync(long userId, byte role);
    Task<ApiResult<UserFavoriteToggleDto>> ToggleAsync(long userId, byte role, string? menuKey);
}

public class UserFavoriteListDto
{
    public List<UserFavoriteItemDto> List { get; set; } = new();
}

public class UserFavoriteItemDto
{
    public string MenuKey { get; set; } = "";
    public string MenuTitle { get; set; } = "";
    public string? Icon { get; set; }
    public DateTime SortAt { get; set; }
}

public class UserFavoriteToggleDto
{
    public bool Favorited { get; set; }
    public UserFavoriteItemDto? Item { get; set; }
}

public class UserFavoriteToggleRequest
{
    public string? MenuKey { get; set; }
}

file enum FavoriteWho { All, Lead, Admin }

file sealed record FavoriteMenu(string Key, string Title, string Icon, FavoriteWho Who);

/// <summary>PC 侧栏菜单清单。标题以这里为准，不信前端传来的名字（docs/240）。</summary>
file static class UserFavoriteCatalog
{

    private static readonly FavoriteMenu[] Menus =
    [
        new("/home", "首页", "House", FavoriteWho.All),
        new("/order", "工单", "Tickets", FavoriteWho.All),
        new("/execution-monitor", "执行监控", "Monitor", FavoriteWho.All),
        new("/report", "报工", "EditPen", FavoriteWho.All),
        new("/review", "报工复核", "CircleCheck", FavoriteWho.Lead),
        new("/abnormal", "异常处理", "Warning", FavoriteWho.Lead),
        new("/product", "产品定义", "Box", FavoriteWho.Admin),
        new("/routing", "工艺路线", "Share", FavoriteWho.Admin),
        new("/operation", "工序", "Operation", FavoriteWho.Admin),
        new("/defect", "不良品项", "CircleClose", FavoriteWho.Admin),
        new("/unit", "单位", "Collection", FavoriteWho.Admin),
        new("/price-rule", "工价表", "Coin", FavoriteWho.Admin),
        new("/user", "用户", "User", FavoriteWho.Admin),
        new("/dept", "部门", "OfficeBuilding", FavoriteWho.Admin),
        new("/custom-field", "自定义字段", "Setting", FavoriteWho.Admin),
        new("/wechat-alert", "微信预警", "ChatDotRound", FavoriteWho.Admin),
        new("/worker-view", "工人手机视角", "Iphone", FavoriteWho.Admin),
        new("/report-upstream", "报工上级校验", "Connection", FavoriteWho.Admin),
        new("/report-qty", "报工数量设置", "Histogram", FavoriteWho.Admin),
        new("/ai-assistant", "AI 助手", "Cpu", FavoriteWho.Admin),
        new("/login-setting", "登录页图片", "Picture", FavoriteWho.Admin),
        new("/mcp-key", "MCP 钥匙", "Key", FavoriteWho.Admin),
        new("/salary", "工资报表", "Money", FavoriteWho.Admin),
        new("/salary-sku", "工资色码汇总", "Grid", FavoriteWho.Admin),
        new("/stat", "生产报表", "DataLine", FavoriteWho.All),
        new("/defect-stat", "不良品报表", "PieChart", FavoriteWho.All),
        new("/sku-stat", "色码汇总", "Grid", FavoriteWho.All),
        new("/quote-suggest", "建议报价", "TrendCharts", FavoriteWho.Admin),
        new("/schedule", "排产评分", "DataBoard", FavoriteWho.Admin),
        new("/board", "车间看板", "Odometer", FavoriteWho.All),
        new("/help", "操作手册", "Document", FavoriteWho.All),
        new("/onboarding", "新手入门", "Reading", FavoriteWho.All),
        new("/faq", "常见问题（系统能力50问）", "QuestionFilled", FavoriteWho.All),
        new("/advantages", "产品优势速览", "Trophy", FavoriteWho.All),
        new("/about", "关于", "InfoFilled", FavoriteWho.All),
    ];

    public static FavoriteMenu? Find(string key)
    {
        foreach (var menu in Menus)
        {
            if (string.Equals(menu.Key, key, StringComparison.OrdinalIgnoreCase))
                return menu;
        }
        return null;
    }

    public static bool CanSee(FavoriteMenu menu, byte role) => menu.Who switch
    {
        FavoriteWho.Admin => role == 1,
        FavoriteWho.Lead => role is 1 or 3,
        _ => role is 1 or 2 or 3
    };
}

public class UserFavoriteService : IUserFavoriteService
{
    private readonly AppDbContext _db;
    public UserFavoriteService(AppDbContext db) { _db = db; }

    public async Task<ApiResult<UserFavoriteListDto>> ListAsync(long userId, byte role)
    {
        var rows = await _db.UserFavorites.AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.SortAt)
            .ThenByDescending(x => x.Id)
            .ToListAsync();

        var list = new List<UserFavoriteItemDto>();
        foreach (var row in rows)
        {
            var menu = UserFavoriteCatalog.Find(row.MenuKey);
            if (menu == null || !UserFavoriteCatalog.CanSee(menu, role)) continue;
            list.Add(ToDto(row, menu.Key, menu.Title, menu.Icon));
            if (list.Count == 12) break;
        }
        return ApiResult<UserFavoriteListDto>.Ok(new UserFavoriteListDto { List = list });
    }

    public async Task<ApiResult<UserFavoriteToggleDto>> ToggleAsync(long userId, byte role, string? menuKey)
    {
        var key = (menuKey ?? "").Trim();
        if (key.Length == 0)
            throw ThrowHelper.BizUser("请选择要收藏的菜单");

        var menu = UserFavoriteCatalog.Find(key);
        if (menu == null || !UserFavoriteCatalog.CanSee(menu, role))
            throw ThrowHelper.BizUser("当前账号不能收藏这个菜单");

        var existing = await _db.UserFavorites
            .FirstOrDefaultAsync(x => x.UserId == userId && x.MenuKey == menu.Key);
        var sql = existing == null
            ? "INSERT INTO sys_user_favorite (user_id, menu_key, menu_title, icon, sort_at, created_at) VALUES (@user_id, @menu_key, @menu_title, @icon, SYSDATETIME(), SYSDATETIME())"
            : "DELETE FROM sys_user_favorite WHERE user_id=@user_id AND menu_key=@menu_key";
        var snap = $"user_id={userId}; menu_key={menu.Key}";

        try
        {
            if (existing != null)
            {
                _db.UserFavorites.Remove(existing);
                await _db.SaveChangesAsync();
                return ApiResult<UserFavoriteToggleDto>.Ok(new UserFavoriteToggleDto { Favorited = false });
            }

            var row = new SysUserFavorite
            {
                UserId = userId,
                MenuKey = menu.Key,
                MenuTitle = menu.Title,
                Icon = menu.Icon,
                SortAt = DateTime.Now,
                CreatedAt = DateTime.Now
            };
            _db.UserFavorites.Add(row);
            await _db.SaveChangesAsync();
            return ApiResult<UserFavoriteToggleDto>.Ok(new UserFavoriteToggleDto
            {
                Favorited = true,
                Item = ToDto(row, menu.Key, menu.Title, menu.Icon)
            });
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _db.ChangeTracker.Clear();
            var row = await _db.UserFavorites.AsNoTracking()
                .FirstOrDefaultAsync(x => x.UserId == userId && x.MenuKey == menu.Key);
            return ApiResult<UserFavoriteToggleDto>.Ok(new UserFavoriteToggleDto
            {
                Favorited = row != null,
                Item = row == null ? null : ToDto(row, menu.Key, menu.Title, menu.Icon)
            });
        }
        catch (DbUpdateException ex)
        {
            throw ThrowHelper.Sql(sql, ex, snap);
        }
    }

    private static UserFavoriteItemDto ToDto(SysUserFavorite row, string key, string title, string icon) =>
        new()
        {
            MenuKey = key,
            MenuTitle = title,
            Icon = icon,
            SortAt = row.SortAt
        };

    private static bool IsUniqueViolation(Exception ex)
    {
        for (var cur = ex; cur != null; cur = cur.InnerException)
        {
            if (cur is SqlException sql && sql.Number is 2627 or 2601)
                return true;
        }
        return false;
    }
}
