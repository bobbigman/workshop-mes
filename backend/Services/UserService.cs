using System.Security.Cryptography;
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
    /// <summary>停用/启用账号（docs/219）；不踢已发 JWT</summary>
    Task<ApiResult<object?>> SetStatusAsync(long id, byte status, long factoryId, long operatorUserId);
    /// <summary>获取或首次生成个人免密二维码（docs/150）</summary>
    Task<ApiResult<UserLoginQrDto>> GetOrCreateLoginQrAsync(long id, long factoryId);
    /// <summary>重新生成个人免密码，旧码立刻失效</summary>
    Task<ApiResult<UserLoginQrDto>> RegenerateLoginQrAsync(long id, long factoryId);
}

public class UserQueryDto
{
    public string? Keyword { get; set; }
    /// <summary>人员类型筛选（docs/161）；不传=全部</summary>
    public byte? UserType { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class UserCreateDto
{
    public string Account { get; set; } = "";
    public string? Phone { get; set; }
    public string? WechatId { get; set; }
    /// <summary>企微通讯录账号；空=不 @（docs/226）</summary>
    public string? WecomUserId { get; set; }
    public string Name { get; set; } = "";
    public byte Role { get; set; }
    /// <summary>0内部员工 1临时用工 2外部供应商；默认 0</summary>
    public byte UserType { get; set; }
    /// <summary>工价系数；不传=新建当 1，编辑则保持原值（docs/225）</summary>
    public decimal? WageCoeff { get; set; }
    public string Password { get; set; } = "";
    /// <summary>所属部门 Id 列表（写 sys_department_user，与部门弹窗同源）</summary>
    public List<long> DeptIds { get; set; } = new();
}

public class UserListDto
{
    public long Id { get; set; }
    public string Account { get; set; } = "";
    public string? Phone { get; set; }
    public string? WechatId { get; set; }
    public string? WecomUserId { get; set; }
    public string Name { get; set; } = "";
    public byte Role { get; set; }
    public byte UserType { get; set; }
    public decimal WageCoeff { get; set; }
    public byte Status { get; set; }
    public List<long> DeptIds { get; set; } = new();
    /// <summary>部门名顿号分隔，供列表展示</summary>
    public string DeptNames { get; set; } = "";
}

public class UserStatusDto
{
    /// <summary>1启用 0停用</summary>
    public byte Status { get; set; }
}

public class UserLoginQrDto
{
    public long UserId { get; set; }
    public string Name { get; set; } = "";
    public string Account { get; set; } = "";
    public byte Status { get; set; }
    /// <summary>密钥本体，对应登录 URL 查询参数 pq</summary>
    public string Code { get; set; } = "";
    /// <summary>站内路径，前端拼 origin 成完整扫码 URL</summary>
    public string LoginPath { get; set; } = "";
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
        if (query.UserType.HasValue)
            q = q.Where(u => u.UserType == query.UserType.Value);

        var total = await q.CountAsync();
        var list = await q.OrderByDescending(u => u.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(u => new UserListDto
            {
                Id = u.Id, Account = u.Account, Phone = u.Phone, WechatId = u.WechatId, WecomUserId = u.WecomUserId,
                Name = u.Name, Role = u.Role, UserType = u.UserType, WageCoeff = u.WageCoeff, Status = u.Status
            }).ToListAsync();

        await FillDeptFieldsAsync(list);
        return ApiResult<PageResult<UserListDto>>.Ok(new PageResult<UserListDto> { List = list, Total = total });
    }

    public async Task<ApiResult<object?>> CreateAsync(UserCreateDto dto, long factoryId)
    {
        if (string.IsNullOrWhiteSpace(dto.Account) || string.IsNullOrWhiteSpace(dto.Name))
            throw ThrowHelper.Biz(nameof(CreateAsync), "账号和姓名不能为空");
        if (dto.Role is not (1 or 2 or 3))
            throw ThrowHelper.Biz(nameof(CreateAsync), "角色无效：须为 1管理员 / 2生产人员 / 3班组长");
        UserTypeHelper.EnsureValid(dto.UserType, nameof(CreateAsync));
        PasswordHelper.EnsureComplexity(dto.Password, nameof(CreateAsync));

        if (await _db.Users.AnyAsync(u => u.FactoryId == factoryId && u.Account == dto.Account))
            throw ThrowHelper.Biz(nameof(CreateAsync), "账号已存在");

        var user = new SysUser
        {
            FactoryId = factoryId,
            Account = dto.Account.Trim(),
            Phone = dto.Phone,
            WechatId = NormalizeWechatId(dto.WechatId),
            WecomUserId = NormalizeWecomUserId(dto.WecomUserId, nameof(CreateAsync)),
            Name = dto.Name.Trim(),
            Role = dto.Role,
            UserType = dto.UserType,
            WageCoeff = WageCoeffHelper.Normalize(dto.WageCoeff, nameof(CreateAsync)),
            Password = PasswordHelper.Hash(dto.Password),
            Status = 1,
            CreatedAt = DateTime.Now
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        await ReplaceUserDeptsAsync(user.Id, dto.DeptIds, factoryId, nameof(CreateAsync));
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
        UserTypeHelper.EnsureValid(dto.UserType, nameof(UpdateAsync));

        user.Account = dto.Account.Trim();
        user.Phone = dto.Phone;
        user.WechatId = NormalizeWechatId(dto.WechatId);
        user.WecomUserId = NormalizeWecomUserId(dto.WecomUserId, nameof(UpdateAsync));
        user.Name = dto.Name.Trim();
        user.Role = dto.Role;
        user.UserType = dto.UserType;
        if (dto.WageCoeff.HasValue)
            user.WageCoeff = WageCoeffHelper.Normalize(dto.WageCoeff, nameof(UpdateAsync));
        if (!string.IsNullOrWhiteSpace(dto.Password))
        {
            PasswordHelper.EnsureComplexity(dto.Password, nameof(UpdateAsync));
            user.Password = PasswordHelper.Hash(dto.Password);
        }
        await ReplaceUserDeptsAsync(id, dto.DeptIds, factoryId, nameof(UpdateAsync));
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

    public async Task<ApiResult<object?>> SetStatusAsync(long id, byte status, long factoryId, long operatorUserId)
    {
        if (status is not (0 or 1))
            throw ThrowHelper.Biz(nameof(SetStatusAsync), "状态无效：须为 0停用 / 1启用");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(SetStatusAsync), "用户不存在");

        if (user.Status == status)
            return ApiResult<object?>.OkMsg();

        if (status == 0)
        {
            if (user.Id == operatorUserId)
                throw ThrowHelper.Biz(nameof(SetStatusAsync), "不能停用自己");

            if (user.Role == 1)
            {
                var enabledAdminCount = await _db.Users.CountAsync(u =>
                    u.FactoryId == factoryId && u.Role == 1 && u.Status == 1);
                if (enabledAdminCount <= 1)
                    throw ThrowHelper.Biz(nameof(SetStatusAsync), "最后一个管理员不可停用");
            }
        }

        user.Status = status;
        await _db.SaveChangesAsync();
        return ApiResult<object?>.OkMsg();
    }

    public async Task<ApiResult<UserLoginQrDto>> GetOrCreateLoginQrAsync(long id, long factoryId)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(GetOrCreateLoginQrAsync), "用户不存在");

        if (string.IsNullOrEmpty(user.LoginQrSecret))
        {
            await AssignNewLoginQrSecretAsync(user);
            await _db.SaveChangesAsync();
        }

        return ApiResult<UserLoginQrDto>.Ok(ToLoginQrDto(user));
    }

    public async Task<ApiResult<UserLoginQrDto>> RegenerateLoginQrAsync(long id, long factoryId)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(RegenerateLoginQrAsync), "用户不存在");

        await AssignNewLoginQrSecretAsync(user);
        await _db.SaveChangesAsync();
        return ApiResult<UserLoginQrDto>.Ok(ToLoginQrDto(user));
    }

    private async Task AssignNewLoginQrSecretAsync(SysUser user)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var secret = NewLoginQrSecret();
            var clash = await _db.Users.AnyAsync(u => u.LoginQrSecret == secret && u.Id != user.Id);
            if (clash) continue;
            user.LoginQrSecret = secret;
            return;
        }
        throw ThrowHelper.General(nameof(AssignNewLoginQrSecretAsync), "生成个人免密码失败，请重试");
    }

    private static UserLoginQrDto ToLoginQrDto(SysUser user)
    {
        var code = user.LoginQrSecret ?? "";
        return new UserLoginQrDto
        {
            UserId = user.Id,
            Name = user.Name,
            Account = user.Account,
            Status = user.Status,
            Code = code,
            LoginPath = string.IsNullOrEmpty(code) ? "" : $"/#/login?pq={Uri.EscapeDataString(code)}"
        };
    }

    private static string NewLoginQrSecret()
    {
        var bytes = new byte[24];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>空/空白存 null，有值则 Trim；不做格式硬校验。</summary>
    private static string? NormalizeWechatId(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>空存 null。有值须是单个企微通讯录账号，不带 @ 和分隔符。</summary>
    private static string? NormalizeWecomUserId(string? value, string where)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();
        if (v.Length > 64)
            throw ThrowHelper.Biz(where, "企微UserId最长 64 字符");
        if (v.Contains('@') || v.Any(char.IsWhiteSpace) || v.IndexOfAny([',', '，', '|', ';', '；']) >= 0)
            throw ThrowHelper.Biz(where, "企微UserId请填通讯录账号，不要带 @ 或分隔符");
        return v;
    }

    private async Task FillDeptFieldsAsync(List<UserListDto> list)
    {
        if (list.Count == 0) return;
        var userIds = list.Select(u => u.Id).ToList();
        var links = await _db.DepartmentUsers.AsNoTracking()
            .Where(x => userIds.Contains(x.UserId)).ToListAsync();
        var deptIds = links.Select(x => x.DepartmentId).Distinct().ToList();
        var names = await _db.Departments.AsNoTracking()
            .Where(d => deptIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => d.Name);

        foreach (var u in list)
        {
            var mine = links.Where(l => l.UserId == u.Id).ToList();
            u.DeptIds = mine.Select(l => l.DepartmentId).ToList();
            u.DeptNames = string.Join("、", mine.Select(l => names.GetValueOrDefault(l.DepartmentId, "")));
        }
    }

    /// <summary>覆盖式重写该用户的部门关联（本厂校验）。</summary>
    private async Task ReplaceUserDeptsAsync(long userId, List<long>? deptIds, long factoryId, string where)
    {
        var ids = (deptIds ?? new List<long>()).Distinct().ToList();
        if (ids.Count > 0)
        {
            var validCount = await _db.Departments.AsNoTracking()
                .CountAsync(d => d.FactoryId == factoryId && ids.Contains(d.Id));
            if (validCount != ids.Count)
                throw ThrowHelper.Biz(where, "所属部门包含无效或不属于本厂的部门");
        }

        var old = _db.DepartmentUsers.Where(x => x.UserId == userId);
        _db.DepartmentUsers.RemoveRange(old);
        foreach (var did in ids)
            _db.DepartmentUsers.Add(new SysDepartmentUser { DepartmentId = did, UserId = userId });
    }
}
