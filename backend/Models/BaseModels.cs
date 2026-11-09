namespace ahu.MicrosoftMes.Models;

// ============ 基础数据（base_）实体 ============
// 【业务背景】字段一一对应 docs/02-数据字典.md 的 base_* 表。

/// <summary>单位（如"只"）</summary>
public class BaseUnit
{
    public long Id { get; set; }
    public long FactoryId { get; set; }
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

/// <summary>不良品项（如"粘锅"）</summary>
public class BaseDefectItem
{
    public long Id { get; set; }
    public long FactoryId { get; set; }
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

/// <summary>工序</summary>
public class BaseOperation
{
    public long Id { get; set; }
    public long FactoryId { get; set; }
    public string Code { get; set; } = "";          // 工序编号
    public string Name { get; set; } = "";          // 工序名称
    /// <summary>1启用 0禁用</summary>
    public byte Status { get; set; } = 1;
    /// <summary>0未审核 1已审核</summary>
    public byte AuditStatus { get; set; }
    public long? AuditedBy { get; set; }
    public DateTime? AuditedAt { get; set; }
    public long? ForbidBy { get; set; }
    public DateTime? ForbidAt { get; set; }
    public long? UnforbidBy { get; set; }
    public DateTime? UnforbidAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>工序-报工权限部门（仅该部门人员可报工）</summary>
public class BaseOperationDepartment
{
    public long Id { get; set; }
    public long OperationId { get; set; }
    public long DepartmentId { get; set; }
}

/// <summary>工序-不良品项关联</summary>
public class BaseOperationDefect
{
    public long Id { get; set; }
    public long OperationId { get; set; }
    public long DefectId { get; set; }
}

/// <summary>工艺路线</summary>
public class BaseRouting
{
    public long Id { get; set; }
    public long FactoryId { get; set; }
    public string Code { get; set; } = "";          // 路线编号
    public string Name { get; set; } = "";          // 路线名称
    /// <summary>1启用 0禁用</summary>
    public byte Status { get; set; } = 1;
    /// <summary>0未审核 1已审核</summary>
    public byte AuditStatus { get; set; }
    public long? AuditedBy { get; set; }
    public DateTime? AuditedAt { get; set; }
    public long? ForbidBy { get; set; }
    public DateTime? ForbidAt { get; set; }
    public long? UnforbidBy { get; set; }
    public DateTime? UnforbidAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>工艺路线-工序明细（有序）</summary>
public class BaseRoutingStep
{
    public long Id { get; set; }
    public long RoutingId { get; set; }
    public long OperationId { get; set; }
    public int Seq { get; set; }                    // 工序顺序(1开始)
}

/// <summary>产品定义</summary>
public class BaseProduct
{
    public long Id { get; set; }
    public long FactoryId { get; set; }
    public string Code { get; set; } = "";          // 产品编号(唯一，相当于身份证)
    public string Name { get; set; } = "";          // 产品名称(可重复)
    public long? UnitId { get; set; }               // 单位
    public long? RoutingId { get; set; }            // 工艺路线
    public string? Supplier { get; set; }           // 供应商(扩展属性)
    public decimal? Price { get; set; }             // 单价(扩展属性，不是工价)
    public bool IsRiskFocus { get; set; }           // 综合风险简报优先关注产品（docs/251、252）
    /// <summary>1启用 0禁用（对标金蝶禁用/反禁用）。</summary>
    public byte Status { get; set; } = 1;
    /// <summary>0未审核 1已审核；新建默认 0；建单须已审且启用。</summary>
    public byte AuditStatus { get; set; }
    public long? AuditedBy { get; set; }
    public DateTime? AuditedAt { get; set; }
    public long? ForbidBy { get; set; }
    public DateTime? ForbidAt { get; set; }
    public long? UnforbidBy { get; set; }
    public DateTime? UnforbidAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// 制造知识库文件（docs/48）。挂在产品/工序上，只存相对路径，不存文件二进制/全文。
/// 【业务背景】文件实体放硬盘，数据库只存「相对 KnowledgeBase:RootPath 的路径 + 归属」。
///             一个文件可挂多处 = 插多条记录（ref_type + ref_id 不同）。
/// </summary>
public class BaseKnowledgeFile
{
    public long Id { get; set; }
    public long FactoryId { get; set; }
    public string FileName { get; set; } = "";       // 显示名（给人看，可与磁盘文件名不同）
    public string RelativePath { get; set; } = "";   // 相对 KnowledgeBase:RootPath 的路径，不存绝对路径
    public string FileType { get; set; } = "";       // pdf / docx / xlsx / jpg / png / webp（决定翻译或预览方式）
    public string RefType { get; set; } = "";        // product / operation（口径同 sys_custom_field.target）
    public long RefId { get; set; }                  // 挂的产品 ID 或工序 ID
    public string? Version { get; set; }             // 版本号（如 V1.2），够用即可，不做历史管理
    public long? CreatedBy { get; set; }             // 登记人
    public DateTime CreatedAt { get; set; }
}
