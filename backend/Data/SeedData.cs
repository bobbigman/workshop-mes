using Microsoft.EntityFrameworkCore;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Models;

namespace ahu.MicrosoftMes.Data;

/// <summary>
/// 演示种子：黑湖川菜馆「最小可跑通」基础资料 + 1 张演示工单。
/// 幂等：按 factory + 业务键判断，已存在则跳过。
/// 仅当 Instance:SeedDemoData=true 时由 Program 调用；客户试用禁止开启。
/// </summary>
public static class SeedData
{
    private const string DemoPwd = "Admin123";

    public static async Task EnsureAsync(AppDbContext db)
    {
        if (!db.Factories.Any())
        {
            db.Factories.Add(new SysFactory
            {
                FactoryCode = "F001",
                FactoryName = "黑湖川菜馆",
                LicenseTier = "trial",
                CreatedAt = DateTime.Now
            });
            await db.SaveChangesAsync();
        }

        var factory = db.Factories.First(f => f.FactoryCode == "F001");
        // 已有库可能仍是「演示工厂」，对齐文稿场景名
        if (factory.FactoryName != "黑湖川菜馆")
        {
            factory.FactoryName = "黑湖川菜馆";
            await db.SaveChangesAsync();
        }

        await EnsureUser(db, factory.Id, "admin", "总管理员", "13800000000", role: 1);
        var wangban = await EnsureUser(db, factory.Id, "wangban", "王班长", "13800000001", role: 3);
        var zhangchu = await EnsureUser(db, factory.Id, "zhangchu", "张厨", "13800000002", role: 2);
        var lichu = await EnsureUser(db, factory.Id, "lichu", "李厨", "13800000003", role: 2);

        var hot = await EnsureDept(db, factory.Id, "HOT", "热菜组", zhangchu.Id, lichu.Id, wangban.Id);
        await EnsureDept(db, factory.Id, "COLD", "凉菜组");

        var unitZhi = await EnsureUnit(db, factory.Id, "只");
        await EnsureUnit(db, factory.Id, "份");
        await EnsureUnit(db, factory.Id, "盘");

        var dSticky = await EnsureDefect(db, factory.Id, "粘锅");
        var dSalty = await EnsureDefect(db, factory.Id, "过咸");
        var dShort = await EnsureDefect(db, factory.Id, "分量不足");

        var opCut = await EnsureOperation(db, factory.Id, "CUT", "切菜",
            deptIds: new[] { hot.Id }, defectIds: new[] { dShort.Id });
        var opCook = await EnsureOperation(db, factory.Id, "COOK", "炒制",
            deptIds: new[] { hot.Id }, defectIds: new[] { dSticky.Id, dSalty.Id });
        var opPlate = await EnsureOperation(db, factory.Id, "PLATE", "装盘",
            deptIds: new[] { hot.Id }, defectIds: Array.Empty<long>());

        var routing = await EnsureRouting(db, factory.Id, "RT-HOT", "热菜标准线",
            opCut.Id, opCook.Id, opPlate.Id);

        var productHg = await EnsureProduct(db, factory.Id, "HG-001", "回锅肉", unitZhi.Id, routing.Id);
        await EnsureProduct(db, factory.Id, "YX-001", "鱼香肉丝", unitZhi.Id, routing.Id);

        var fieldTable = await EnsureCustomField(db, factory.Id, "work_order", "桌号", "single",
            "一号桌", "二号桌", "三号桌");
        var fieldSpice = await EnsureCustomField(db, factory.Id, "work_order", "辣度", "single",
            "不辣", "微辣", "中辣", "特辣");

        await EnsureDemoWorkOrder(db, factory.Id, wangban.Id, productHg.Id, "DEMO001",
            new[] { (fieldTable.Id, "一号桌"), (fieldSpice.Id, "中辣") });
    }

    /// <summary>
    /// 新建账套（工厂）+ 灌一套「五金厂」演示数据（docs/52）。
    /// 幂等：按 factory + 业务键判断，已存在则跳过；与启动种子（川菜馆 F001）分离。
    /// </summary>
    public static async Task<SysFactory> SeedFactoryAsync(AppDbContext db, string factoryCode, string factoryName)
    {
        var factory = await db.Factories.FirstOrDefaultAsync(f => f.FactoryCode == factoryCode);
        if (factory == null)
        {
            factory = new SysFactory
            {
                FactoryCode = factoryCode.Trim(),
                FactoryName = factoryName.Trim(),
                LicenseTier = "trial",
                CreatedAt = DateTime.Now
            };
            db.Factories.Add(factory);
            await db.SaveChangesAsync();
        }

        await EnsureUser(db, factory.Id, "admin", "管理员", "", role: 1);
        var banzhang = await EnsureUser(db, factory.Id, "banzhang", "张班长", "", role: 3);
        var gongren = await EnsureUser(db, factory.Id, "gongren", "李师傅", "", role: 2);

        var chong = await EnsureDept(db, factory.Id, "CHONG", "冲压组", gongren.Id, banzhang.Id);
        var da = await EnsureDept(db, factory.Id, "DA", "打磨组");
        var jian = await EnsureDept(db, factory.Id, "JIAN", "质检组");

        var unitJian = await EnsureUnit(db, factory.Id, "件");
        await EnsureUnit(db, factory.Id, "套");

        var dScratch = await EnsureDefect(db, factory.Id, "划伤");
        var dSize = await EnsureDefect(db, factory.Id, "尺寸超差");
        var dBurr = await EnsureDefect(db, factory.Id, "毛刺");

        var opCut = await EnsureOperation(db, factory.Id, "XC", "下料",
            deptIds: new[] { chong.Id }, defectIds: Array.Empty<long>());
        var opPunch = await EnsureOperation(db, factory.Id, "CY", "冲压",
            deptIds: new[] { chong.Id }, defectIds: new[] { dBurr.Id, dSize.Id });
        var opPolish = await EnsureOperation(db, factory.Id, "DM", "打磨",
            deptIds: new[] { da.Id }, defectIds: new[] { dScratch.Id });
        var opQc = await EnsureOperation(db, factory.Id, "ZJ", "质检",
            deptIds: new[] { jian.Id }, defectIds: Array.Empty<long>());

        var routing = await EnsureRouting(db, factory.Id, "RT-WJ", "五金件标准线",
            opCut.Id, opPunch.Id, opPolish.Id, opQc.Id);

        var prodA = await EnsureProduct(db, factory.Id, "WJ-001", "五金冲压件A", unitJian.Id, routing.Id);
        await EnsureProduct(db, factory.Id, "WJ-002", "五金冲压件B", unitJian.Id, routing.Id);

        var fieldBatch = await EnsureCustomField(db, factory.Id, "work_order", "批次", "single",
            "第一批", "第二批");
        var fieldMat = await EnsureCustomField(db, factory.Id, "work_order", "材质", "single",
            "304不锈钢", "201不锈钢");

        await EnsureDemoWorkOrder(db, factory.Id, banzhang.Id, prodA.Id, "DEMO001",
            new[] { (fieldBatch.Id, "第一批"), (fieldMat.Id, "304不锈钢") });

        return factory;
    }

    private static async Task<SysUser> EnsureUser(AppDbContext db, long factoryId,
        string account, string name, string phone, byte role)
    {
        var u = db.Users.FirstOrDefault(x => x.FactoryId == factoryId && x.Account == account);
        if (u != null)
        {
            // docs/22：演示账号角色对齐（王班长 → 班组长）
            if (u.Role != role)
            {
                u.Role = role;
                await db.SaveChangesAsync();
            }
            return u;
        }

        u = new SysUser
        {
            FactoryId = factoryId,
            Account = account,
            Name = name,
            Phone = phone,
            Role = role,
            Password = PasswordHelper.Hash(DemoPwd),
            Status = 1,
            CreatedAt = DateTime.Now
        };
        db.Users.Add(u);
        await db.SaveChangesAsync();
        return u;
    }

    private static async Task<SysDepartment> EnsureDept(AppDbContext db, long factoryId,
        string code, string name, params long[] memberIds)
    {
        var d = db.Departments.FirstOrDefault(x => x.FactoryId == factoryId && x.Code == code);
        if (d == null)
        {
            d = new SysDepartment
            {
                FactoryId = factoryId,
                Code = code,
                Name = name,
                CreatedAt = DateTime.Now
            };
            db.Departments.Add(d);
            await db.SaveChangesAsync();
        }

        foreach (var uid in memberIds.Distinct())
        {
            if (!db.DepartmentUsers.Any(x => x.DepartmentId == d.Id && x.UserId == uid))
            {
                db.DepartmentUsers.Add(new SysDepartmentUser { DepartmentId = d.Id, UserId = uid });
            }
        }
        await db.SaveChangesAsync();
        return d;
    }

    private static async Task<BaseUnit> EnsureUnit(AppDbContext db, long factoryId, string name)
    {
        var u = db.Units.FirstOrDefault(x => x.FactoryId == factoryId && x.Name == name);
        if (u != null) return u;
        u = new BaseUnit { FactoryId = factoryId, Name = name, CreatedAt = DateTime.Now };
        db.Units.Add(u);
        await db.SaveChangesAsync();
        return u;
    }

    private static async Task<BaseDefectItem> EnsureDefect(AppDbContext db, long factoryId, string name)
    {
        var d = db.DefectItems.FirstOrDefault(x => x.FactoryId == factoryId && x.Name == name);
        if (d != null) return d;
        d = new BaseDefectItem { FactoryId = factoryId, Name = name, CreatedAt = DateTime.Now };
        db.DefectItems.Add(d);
        await db.SaveChangesAsync();
        return d;
    }

    private static async Task<BaseOperation> EnsureOperation(AppDbContext db, long factoryId,
        string code, string name, long[] deptIds, long[] defectIds)
    {
        var op = db.Operations.FirstOrDefault(x => x.FactoryId == factoryId && x.Code == code);
        if (op == null)
        {
            op = new BaseOperation
            {
                FactoryId = factoryId,
                Code = code,
                Name = name,
                CreatedAt = DateTime.Now
            };
            db.Operations.Add(op);
            await db.SaveChangesAsync();
        }

        foreach (var deptId in deptIds)
        {
            if (!db.OperationDepartments.Any(x => x.OperationId == op.Id && x.DepartmentId == deptId))
                db.OperationDepartments.Add(new BaseOperationDepartment { OperationId = op.Id, DepartmentId = deptId });
        }
        foreach (var defectId in defectIds)
        {
            if (!db.OperationDefects.Any(x => x.OperationId == op.Id && x.DefectId == defectId))
                db.OperationDefects.Add(new BaseOperationDefect { OperationId = op.Id, DefectId = defectId });
        }
        await db.SaveChangesAsync();
        return op;
    }

    private static async Task<BaseRouting> EnsureRouting(AppDbContext db, long factoryId,
        string code, string name, params long[] operationIds)
    {
        var r = db.Routings.FirstOrDefault(x => x.FactoryId == factoryId && x.Code == code);
        if (r == null)
        {
            r = new BaseRouting
            {
                FactoryId = factoryId,
                Code = code,
                Name = name,
                CreatedAt = DateTime.Now
            };
            db.Routings.Add(r);
            await db.SaveChangesAsync();
        }

        for (var i = 0; i < operationIds.Length; i++)
        {
            var seq = i + 1;
            var opId = operationIds[i];
            if (!db.RoutingSteps.Any(x => x.RoutingId == r.Id && x.Seq == seq))
            {
                db.RoutingSteps.Add(new BaseRoutingStep
                {
                    RoutingId = r.Id,
                    OperationId = opId,
                    Seq = seq
                });
            }
        }
        await db.SaveChangesAsync();
        return r;
    }

    private static async Task<BaseProduct> EnsureProduct(AppDbContext db, long factoryId,
        string code, string name, long unitId, long routingId)
    {
        var p = db.Products.FirstOrDefault(x => x.FactoryId == factoryId && x.Code == code);
        if (p != null) return p;
        p = new BaseProduct
        {
            FactoryId = factoryId,
            Code = code,
            Name = name,
            UnitId = unitId,
            RoutingId = routingId,
            CreatedAt = DateTime.Now
        };
        db.Products.Add(p);
        await db.SaveChangesAsync();
        return p;
    }

    private static async Task<SysCustomField> EnsureCustomField(AppDbContext db, long factoryId,
        string target, string fieldName, string fieldType, params string[] options)
    {
        var f = db.CustomFields.FirstOrDefault(x =>
            x.FactoryId == factoryId && x.Target == target && x.FieldName == fieldName);
        if (f == null)
        {
            f = new SysCustomField
            {
                FactoryId = factoryId,
                Target = target,
                FieldName = fieldName,
                FieldType = fieldType,
                CreatedAt = DateTime.Now
            };
            db.CustomFields.Add(f);
            await db.SaveChangesAsync();
        }

        for (var i = 0; i < options.Length; i++)
        {
            var label = options[i];
            if (!db.CustomFieldOptions.Any(x => x.FieldId == f.Id && x.Label == label))
            {
                db.CustomFieldOptions.Add(new SysCustomFieldOption
                {
                    FieldId = f.Id,
                    Label = label,
                    Seq = i + 1
                });
            }
        }
        await db.SaveChangesAsync();
        return f;
    }

    private static async Task EnsureDemoWorkOrder(AppDbContext db, long factoryId,
        long createdBy, long productId, string orderNo, (long fieldId, string value)[] fieldValues)
    {
        var order = db.WorkOrders.FirstOrDefault(o => o.FactoryId == factoryId && o.OrderNo == orderNo);
        if (order == null)
        {
            order = new ProdWorkOrder
            {
                FactoryId = factoryId,
                OrderNo = orderNo,
                ProductId = productId,
                Qty = 2,
                Status = 1,
                CreatedBy = createdBy,
                CreatedAt = DateTime.Now
            };
            db.WorkOrders.Add(order);
            await db.SaveChangesAsync();
        }

        // 幂等补工序任务（工序级计划数）：已有工单无任务行时补上
        if (!db.WorkOrderOperations.Any(t => t.WorkOrderId == order.Id))
        {
            var product = db.Products.First(p => p.Id == order.ProductId);
            if (product.RoutingId.HasValue)
            {
                var steps = db.RoutingSteps.AsNoTracking()
                    .Where(s => s.RoutingId == product.RoutingId)
                    .OrderBy(s => s.Seq).ToList();
                foreach (var s in steps)
                {
                    db.WorkOrderOperations.Add(new ProdWorkOrderOperation
                    {
                        WorkOrderId = order.Id,
                        OperationId = s.OperationId,
                        Seq = s.Seq,
                        PlanQty = order.Qty
                    });
                }
                await db.SaveChangesAsync();
            }
        }

        // 幂等补自定义字段值
        if (!db.CustomFieldValues.Any(v => v.TargetId == order.Id))
        {
            foreach (var (fieldId, value) in fieldValues)
            {
                db.CustomFieldValues.Add(new SysCustomFieldValue
                {
                    FieldId = fieldId,
                    TargetId = order.Id,
                    Value = value
                });
            }
            await db.SaveChangesAsync();
        }
    }
}
