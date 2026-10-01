using Microsoft.EntityFrameworkCore;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;

namespace ahu.MicrosoftMes.Services;

public interface IUserService
{
    Task<ApiResult<PageResult<UserListDto>>> QueryAsync(UserQueryDto query, long factoryId);
    Task<ApiResult<object?>> CreateAsync(UserCreateDto dto, long factoryId);
    Task<ApiResult<object?>> UpdateAsync(long id, UserCreateDto dto, long factoryId);
    Task<ApiResult<object?>> DeleteAsync(long id);
}

public class UserQueryDto
{
    public string? Keyword { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class UserCreateDto
{
    public string Account { get; set; } = "";
    public string? Phone { get; set; }
    public string? WechatId { get; set; }
    public string Name { get; set; } = "";
    public byte Role { get; set; }
    public string Password { get; set; } = "";
}

public class UserListDto
{
    public long Id { get; set; }
    public string Account { get; set; } = "";
    public string? Phone { get; set; }
    public string? WechatId { get; set; }
    public string Name { get; set; } = "";
    public byte Role { get; set; }
    public byte Status { get; set; }
}

public class UserService : IUserService
{
    private readonly AppDbContext _db;
    public UserService(AppDbContext db) { _db = db; }

    public async Task<ApiResult<PageResult<UserListDto>>> QueryAsync(UserQueryDto query, long factoryId)
    {
        var q = _db.Users.AsNoTracking().Where(u => u.FactoryId == factoryId);
        if (!string.IsNullOrWhiteSpace(query.Keyword))
            q = q.Where(u => u.Account.Contains(query.Keyword)
                || u.Name.Contains(query.Keyword)
                || (u.WechatId != null && u.WechatId.Contains(query.Keyword)));

        var total = await q.CountAsync();
        var list = await q.OrderByDescending(u => u.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(u => new UserListDto
            {
                Id = u.Id, Account = u.Account, Phone = u.Phone, WechatId = u.WechatId,
                Name = u.Name, Role = u.Role, Status = u.Status
            }).ToListAsync();

        return ApiResult<PageResult<UserListDto>>.Ok(new PageResult<UserListDto> { List = list, Total = total });
    }

    public async Task<ApiResult<object?>> CreateAsync(UserCreateDto dto, long factoryId)
    {
        if (string.IsNullOrWhiteSpace(dto.Account) || string.IsNullOrWhiteSpace(dto.Name))
            throw ThrowHelper.Biz(nameof(CreateAsync), "账号和姓名不能为空");
        if (dto.Role is not (1 or 2 or 3))
            throw ThrowHelper.Biz(nameof(CreateAsync), "角色无效：须为 1管理员 / 2生产人员 / 3班组长");
        PasswordHelper.EnsureComplexity(dto.Password, nameof(CreateAsync));

        if (await _db.Users.AnyAsync(u => u.FactoryId == factoryId && u.Account == dto.Account))
            throw ThrowHelper.Biz(nameof(CreateAsync), "账号已存在");

        _db.Users.Add(new SysUser
        {
            FactoryId = factoryId,
            Account = dto.Account.Trim(),
            Phone = dto.Phone,
            WechatId = NormalizeWechatId(dto.WechatId),
            Name = dto.Name.Trim(),
            Role = dto.Role,
            Password = PasswordHelper.Hash(dto.Password),
            Status = 1,
            CreatedAt = DateTime.Now
        });
        await _db.SaveChangesAsync();
        return ApiResult<object?>.OkMsg();
    }

    public async Task<ApiResult<object?>> UpdateAsync(long id, UserCreateDto dto, long factoryId)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(UpdateAsync), "用户不存在");

        if (await _db.Users.AnyAsync(u => u.FactoryId == factoryId && u.Account == dto.Account && u.Id != id))
            throw ThrowHelper.Biz(nameof(UpdateAsync), "账号已存在");
        if (dto.Role is not (1 or 2 or 3))
            throw ThrowHelper.Biz(nameof(UpdateAsync), "角色无效：须为 1管理员 / 2生产人员 / 3班组长");

        user.Account = dto.Account.Trim();
        user.Phone = dto.Phone;
        user.WechatId = NormalizeWechatId(dto.WechatId);
        user.Name = dto.Name.Trim();
        user.Role = dto.Role;
        if (!string.IsNullOrWhiteSpace(dto.Password))
        {
            PasswordHelper.EnsureComplexity(dto.Password, nameof(UpdateAsync));
            user.Password = PasswordHelper.Hash(dto.Password);
        }
        await _db.SaveChangesAsync();
        return ApiResult<object?>.OkMsg();
    }

    public async Task<ApiResult<object?>> DeleteAsync(long id)
    {
        var msg = await DeleteGuard.CheckAsync(_db, "User", id);
        if (msg != null) throw ThrowHelper.Biz(nameof(DeleteAsync), msg);

        var user = await _db.Users.FindAsync(id)
            ?? throw ThrowHelper.Biz(nameof(DeleteAsync), "用户不存在");
        _db.Users.Remove(user);
        await _db.SaveChangesAsync();
        return ApiResult<object?>.OkMsg();
    }

    /// <summary>空/空白存 null，有值则 Trim；不做格式硬校验。</summary>
    private static string? NormalizeWechatId(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
