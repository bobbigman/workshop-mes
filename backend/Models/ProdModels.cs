namespace ahu.MicrosoftMes.Models;

// ============ 生产业务（prod_）实体 ============
// 【业务背景】字段一一对应 docs/02-数据字典.md 的 prod_* 表。

/// <summary>工单</summary>
public class ProdWorkOrder
{
    public long Id { get; set; }
    public long FactoryId { get; set; }
    public string OrderNo { get; set; } = "";       // 工单编号(唯一)
    public long ProductId { get; set; }             // 产品
    public int Qty { get; set; }                    // 下单数量
    public byte Status { get; set; } = 0;           // 0待生产 1生产中 2完成
    public DateTime? DueDate { get; set; }          // 计划交期；空=未设交期
    public long? CreatedBy { get; set; }            // 创建人
    public DateTime CreatedAt { get; set; }
}

/// <summary>工单工序任务（工序级计划数，对标黑湖「生产任务」tab）</summary>
public class ProdWorkOrderOperation
{
    public long Id { get; set; }
    public long WorkOrderId { get; set; }             // 工单
    public long OperationId { get; set; }             // 工序
    public int Seq { get; set; }                      // 顺序
    public int PlanQty { get; set; }                  // 该工序计划数
    public long? AssigneeUserId { get; set; }         // 派工执行人（docs/29）；空=未派工
}

/// <summary>报工记录</summary>
public class ProdReport
{
    public long Id { get; set; }
    public long FactoryId { get; set; }
    public long OrderId { get; set; }               // 工单
    public long OperationId { get; set; }           // 工序
    public long UserId { get; set; }                // 报工人
    public int GoodQty { get; set; }                // 良品数
    public int DefectQty { get; set; }              // 不良品数
    public long? DefectId { get; set; }             // 不良品原因(不良品项)
    public int DurationMinutes { get; set; }        // 报工时长（分钟），0=未填
    public string? BatchNo { get; set; }            // 批量补报批次号；单条报工为空
    public long? ProductId { get; set; }            // 产品快照（docs/21）
    public long? DepartmentId { get; set; }         // 报工部门快照
    public decimal? UnitPrice { get; set; }         // 命中工价单价快照
    public decimal? WageAmount { get; set; }        // 预计算工资额
    public bool SettledFlag { get; set; }           // 已结算冻结
    public byte ReviewStatus { get; set; }          // 0待复核 1已通过 2已退回（docs/22）
    public long? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? RejectReason { get; set; }
    public DateTime ReportTime { get; set; }        // 报工时间
    public string? ClientRequestId { get; set; }    // 客户端提交去重标识（docs/94）
    public string? ShareBatchNo { get; set; }       // 多人分摊/代报同批号；与 BatchNo 独立
    public long? OperatorUserId { get; set; }       // 代报/分摊操作人；自报为空
}

/// <summary>工人异常上报（docs/28；官方三类）</summary>
public class ProdAbnormal
{
    public long Id { get; set; }
    public long FactoryId { get; set; }
    public byte AbnormalType { get; set; }          // 1设备故障 2物料短缺 3质量异常
    public string Description { get; set; } = "";
    public string? ImagePath { get; set; }          // 相对路径
    public long? WorkOrderId { get; set; }
    public long ReportedBy { get; set; }
    public DateTime ReportedAt { get; set; }
    public byte Status { get; set; }                // 0待处理 1已恢复
    public long? HandledBy { get; set; }
    public DateTime? RecoveredAt { get; set; }
    public string? HandleNote { get; set; }
}
