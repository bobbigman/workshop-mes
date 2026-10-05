namespace ahu.MicrosoftMes.Models;

// ============ 系统（sys_）实体 ============
// 【业务背景】字段一一对应 docs/02-数据字典.md 的 sys_* 表。
//             字段名用 PascalCase，EF 默认映射到同名列；如需自定义列名在 OnModelCreating 配置。

/// <summary>工厂（多租户）</summary>
public class SysFactory
{
    public long Id { get; set; }
    public string FactoryCode { get; set; } = "";   // 工厂代码
    public string FactoryName { get; set; } = "";   // 工厂名称
    /// <summary>合同授权档：trial / enterprise / flagship（docs/88）；有效档见 LicenseCloudService</summary>
    public string LicenseTier { get; set; } = "trial";
    /// <summary>云端授权到期（UTC）；空=未激活（docs/89）</summary>
    public DateTime? LicenseExpiresAtUtc { get; set; }
    /// <summary>最近一次成功云校验的服务端 UTC</summary>
    public DateTime? LastLicenseOkUtc { get; set; }
    /// <summary>最近一次发起校验的本地 UTC（防拨回）</summary>
    public DateTime? LastLicenseSeenUtc { get; set; }
    /// <summary>体验账套到期（UTC）；空=不进清理队列（docs/90）</summary>
    public DateTime? TrialExpiresAtUtc { get; set; }
    /// <summary>体验账套已清场时间（防重复清理）</summary>
    public DateTime? TrialCleanedAtUtc { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>用户</summary>
public class SysUser
{
    public long Id { get; set; }
    public long FactoryId { get; set; }             // 所属工厂
    public string Account { get; set; } = "";       // 账号(可填手机号)
    public string? Phone { get; set; }              // 手机号
    public string? WechatId { get; set; }           // 微信号（可空，仅记录；不做登录/推送）
    public string Name { get; set; } = "";          // 姓名
    public byte Role { get; set; }                  // 角色: 1管理员 2生产人员 3班组长
    public string Password { get; set; } = "";      // 加密密码(BCrypt)
    public byte Status { get; set; } = 1;           // 1启用 0停用
    public DateTime CreatedAt { get; set; }
}

/// <summary>部门</summary>
public class SysDepartment
{
    public long Id { get; set; }
    public long FactoryId { get; set; }
    public string Code { get; set; } = "";          // 部门编码
    public string Name { get; set; } = "";          // 部门名称
    public DateTime CreatedAt { get; set; }

    // 【TODO·填空】导航属性：成员列表（关联 SysDepartmentUser）
}

/// <summary>部门-用户关联</summary>
public class SysDepartmentUser
{
    public long Id { get; set; }
    public long DepartmentId { get; set; }
    public long UserId { get; set; }
}

/// <summary>工单打印标签设置（每工厂一条）</summary>
public class SysPrintSetting
{
    public long Id { get; set; }
    public long FactoryId { get; set; }
    public string LabelSize { get; set; } = "Label80x60"; // Label30x40 | Label80x60
    public string QrMode { get; set; } = "OrderNo";       // OrderNo | ReportUrl | ReportUrlOp
    public string? BaseUrl { get; set; }
    public bool ShowProductCode { get; set; } = true;
    public bool ShowProductName { get; set; } = true;
    public bool ShowQty { get; set; } = true;
    public bool ShowOps { get; set; } = true;
    /// <summary>要打印的工单自定义字段 ID 列表，JSON 如 [1,2]；空=不打</summary>
    public string? ShowCustomFieldIds { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>登录页设置（每工厂一条；左侧大图自配，docs/69）</summary>
public class LoginSetting
{
    public long Id { get; set; }
    public long FactoryId { get; set; }
    public string? BannerUrl { get; set; }          // 登录页左侧图相对路径
    public DateTime UpdatedAt { get; set; }
}

/// <summary>工人手机视角（每工厂一条；默认全车间可见，docs/200）</summary>
public class SysWorkerViewSetting
{
    public long Id { get; set; }
    public long FactoryId { get; set; }
    /// <summary>1=全车间可见(默认) 2=只看我的任务</summary>
    public byte WorkerViewMode { get; set; } = 1;
    public DateTime UpdatedAt { get; set; }
}

/// <summary>企业微信预警设置（每工厂一条；默认关闭，docs/1B）</summary>
public class SysWechatAlertSetting
{
    public long Id { get; set; }
    public long FactoryId { get; set; }
    public bool Enabled { get; set; }                 // 默认 false
    public string? CorpId { get; set; }
    public string? Secret { get; set; }
    public string? AgentId { get; set; }
    public string? ToUser { get; set; }               // 企业微信 UserId，| 分隔
    public string? WebhookKey { get; set; }           // 默认群机器人 webhook key（docs/131）
    public int DailyLimit { get; set; } = 20;
    public bool NoticeSeen { get; set; }              // 首次说明已看过
    public DateTime UpdatedAt { get; set; }
}

/// <summary>企业微信推送去重日志</summary>
public class SysWechatAlertLog
{
    public long Id { get; set; }
    public long FactoryId { get; set; }
    public string AlertKey { get; set; } = "";
    public DateTime SendDate { get; set; }            // 仅日期部分
    public string? Content { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>MCP 授权（豆包方舟演示；配对码/授权token，docs/25）</summary>
public class SysMcpAuth
{
    public long Id { get; set; }
    public long FactoryId { get; set; }
    public string AuthCode { get; set; } = "";         // 一次性配对码
    public string AuthToken { get; set; } = "";        // 授权 token
    public string? KingdeeUserId { get; set; }         // 金蝶用户ID
    public string? KingdeeUserName { get; set; }       // 金蝶用户名
    public bool IsFinance { get; set; }                // 是否财务角色
    public bool CodeUsed { get; set; }                 // code 是否已用
    public DateTime CodeExpire { get; set; }           // code 过期
    public DateTime TokenExpire { get; set; }          // token 过期
    public DateTime CreatedAt { get; set; }
}

/// <summary>MCP 每厂 API Key（只存哈希，docs/96）</summary>
public class SysMcpKey
{
    public long KeyId { get; set; }
    public long FactoryId { get; set; }
    public string KeyAlias { get; set; } = "";
    public string ApiKeyHash { get; set; } = "";
    public byte Status { get; set; } = 1;
    public DateTime? ExpireAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? Remark { get; set; }
}

/// <summary>MCP 会话身份绑定用户（手机号直绑，docs/97）</summary>
public class SysWxBind
{
    public long BindId { get; set; }
    public long FactoryId { get; set; }
    public string ChanType { get; set; } = "";
    public string WxOpenid { get; set; } = "";
    public long UserId { get; set; }
    public byte Status { get; set; } = 1;
    public DateTime BoundAt { get; set; }
    public DateTime? UnboundAt { get; set; }
    public string? Remark { get; set; }
}
