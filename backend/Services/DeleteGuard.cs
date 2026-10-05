using Microsoft.EntityFrameworkCore;
using ahu.MicrosoftMes.Data;

namespace ahu.MicrosoftMes.Services;

/// <summary>删除顺序校验（三条硬规则）。返回 null 可删，非空为阻断提示。</summary>
public static class DeleteGuard
{
    public static async Task<string?> CheckAsync(AppDbContext db, string entityType, long id)
    {
        switch (entityType)
        {
            case "WorkOrder":
                return null;

            case "Product":
                if (await db.WorkOrders.AnyAsync(o => o.ProductId == id))
                    return "该产品已被工单引用，请先删除相关工单";
                if (await db.PriceRules.AnyAsync(r => r.ProductId == id))
                    return "该产品已被工价规则引用，请先删除相关工价规则";
                if (await db.KnowledgeFiles.AnyAsync(f => f.RefType == "product" && f.RefId == id))
                    return "该产品已被知识库文件引用，请先删除相关文件";
                break;

            case "Routing":
                if (await db.Products.AnyAsync(p => p.RoutingId == id))
                    return "该工艺路线已被产品引用，请先删除相关产品";
                break;

            case "Operation":
                if (await db.RoutingSteps.AnyAsync(s => s.OperationId == id))
                    return "该工序已被工艺路线引用，请先删除相关工艺路线";
                if (await db.Reports.AnyAsync(r => r.OperationId == id))
                    return "该工序已有报工记录，不可删除";
                if (await db.PriceRules.AnyAsync(r => r.OperationId == id))
                    return "该工序已被工价规则引用，请先删除相关工价规则";
                if (await db.KnowledgeFiles.AnyAsync(f => f.RefType == "operation" && f.RefId == id))
                    return "该工序已被知识库文件引用，请先删除相关文件";
                break;

            case "DefectItem":
                if (await db.OperationDefects.AnyAsync(x => x.DefectId == id))
                    return "该不良品项已被工序引用，请先删除相关工序";
                break;

            case "Unit":
                if (await db.Products.AnyAsync(p => p.UnitId == id))
                    return "该单位已被产品引用，请先删除相关产品";
                break;

            case "Department":
                if (await db.OperationDepartments.AnyAsync(x => x.DepartmentId == id))
                    return "该部门被工序设为报工权限，请先调整工序";
                if (await db.DepartmentUsers.AnyAsync(x => x.DepartmentId == id))
                    return "该部门下还有用户，请先移除";
                if (await db.PriceRules.AnyAsync(r => r.DepartmentId == id))
                    return "该部门已被工价规则引用，请先删除相关工价规则";
                break;

            case "User":
                if (await db.DepartmentUsers.AnyAsync(x => x.UserId == id))
                    return "该用户属于部门，请先从部门移除";
                if (await db.Reports.AnyAsync(r => r.UserId == id))
                    return "该用户已有报工记录，不可删除";
                if (await db.SalaryStatements.AnyAsync(s => s.UserId == id))
                    return "该用户已有工资单，不可删除";
                if (await db.WorkOrderOperations.AnyAsync(t => t.AssigneeUserId == id)
                    || await db.WorkOrderOperationAssignees.AnyAsync(a => a.UserId == id))
                    return "该用户已被派工，请先在工单详情取消派工";
                break;

            default:
                return $"未知实体类型: {entityType}";
        }
        return null;
    }
}
