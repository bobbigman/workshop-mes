namespace ahu.MicrosoftMes.Models;

// ============ 扩展能力（sys_custom_field*）实体 ============
// 【业务背景】自定义字段采用「定义表 + 值表」方案（见 docs/02 与 03 技术方案 5.2）。
//             新增字段=插一行定义，不改表结构、不发版。

/// <summary>自定义字段定义</summary>
public class SysCustomField
{
    public long Id { get; set; }
    public long FactoryId { get; set; }
    public string Target { get; set; } = "";        // 归属对象: work_order/product/operation
    public string FieldName { get; set; } = "";     // 字段名称（显示名）
    public string FieldType { get; set; } = "";     // 类型: single(单选)/text(文本)/number(数字)
    /// <summary>业务编码标识；空=普通字段。色码约定 color/spec（docs/102）。</summary>
    public string? FieldKey { get; set; }
    /// <summary>是否在 PC 工单列表显示该列；仅 target=work_order 可开启。</summary>
    public bool ShowInOrderList { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>自定义字段选项（单选用）</summary>
public class SysCustomFieldOption
{
    public long Id { get; set; }
    public long FieldId { get; set; }
    public string Label { get; set; } = "";         // 选项值, 如"一号桌"
    public int Seq { get; set; }
}

/// <summary>自定义字段值（绑定到某条记录）</summary>
public class SysCustomFieldValue
{
    public long Id { get; set; }
    public long FieldId { get; set; }
    public long TargetId { get; set; }              // 归属对象记录ID
    public string? Value { get; set; }              // 字段值
}
