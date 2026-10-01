using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;
using ahu.MicrosoftMes.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace WorkshopMes.Tests;

/// <summary>
/// 工单自定义字段列表展示与筛选（docs/18 补充）：真实 SQL Server 测试库。
/// 环境：本机 SQL Server；连接串可用环境变量 MES_TEST_DB 覆盖。
/// </summary>
[Collection("cf_list_db")]
public class WorkOrderCustomFieldListTests
{
    private readonly CustomFieldListDbFixture _fx;
    public WorkOrderCustomFieldListTests(CustomFieldListDbFixture fx) { _fx = fx; }

    private static (AppDbContext db, WorkOrderService orders, CustomFieldService fields) New()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(CustomFieldListDbFixture.ConnString)
            .Options;
        var db = new AppDbContext(opts);
        return (db, new WorkOrderService(db), new CustomFieldService(db));
    }

    [Fact]
    public async Task Exact_Match_SalesOrder_Returns_A1_A2_Only()
    {
        var (_, orders, _) = New();
        var r = await orders.QueryAsync(new WorkOrderQueryDto
        {
            CustomFieldId = _fx.SoFieldId,
            CustomFieldValue = "SO202609001",
            CustomFieldMatch = "exact",
            Page = 1,
            PageSize = 50,
            ExcludeCancelled = true
        }, _fx.FactoryId);

        Assert.Equal(0, r.Code);
        var nos = r.Data!.List.Select(x => x.OrderNo).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "CF-A1", "CF-A2" }, nos);
        Assert.Equal(2, r.Data.Total);
    }

    [Fact]
    public async Task Contains_Match_Includes_Suffix_Variant()
    {
        var (_, orders, _) = New();
        var r = await orders.QueryAsync(new WorkOrderQueryDto
        {
            CustomFieldId = _fx.SoFieldId,
            CustomFieldValue = "SO202609001",
            CustomFieldMatch = "contains",
            Page = 1,
            PageSize = 50,
            ExcludeCancelled = true
        }, _fx.FactoryId);

        Assert.Equal(0, r.Code);
        var nos = r.Data!.List.Select(x => x.OrderNo).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "CF-A1", "CF-A2", "CF-A3" }, nos);
    }

    [Fact]
    public async Task List_Ext_Only_ShowInOrderList_Fields()
    {
        var (_, orders, _) = New();
        var r = await orders.QueryAsync(new WorkOrderQueryDto
        {
            Keyword = "CF-A1",
            Page = 1,
            PageSize = 10,
            ExcludeCancelled = true
        }, _fx.FactoryId);

        var row = Assert.Single(r.Data!.List);
        Assert.True(row.Ext.ContainsKey(_fx.SoFieldId));
        Assert.Equal("SO202609001", row.Ext[_fx.SoFieldId]);
        Assert.False(row.Ext.ContainsKey(_fx.HiddenFieldId));
    }

    [Fact]
    public async Task Filter_Works_When_Field_Not_Shown_In_List()
    {
        var (_, orders, _) = New();
        var r = await orders.QueryAsync(new WorkOrderQueryDto
        {
            CustomFieldId = _fx.HiddenFieldId,
            CustomFieldValue = "HIDDEN-X",
            CustomFieldMatch = "exact",
            Page = 1,
            PageSize = 10,
            ExcludeCancelled = true
        }, _fx.FactoryId);

        Assert.Equal(0, r.Code);
        Assert.Equal("CF-A4", Assert.Single(r.Data!.List).OrderNo);
    }

    [Fact]
    public async Task Reject_NonWorkOrder_Field_And_CrossFactory()
    {
        var (_, orders, _) = New();
        await Assert.ThrowsAsync<BusinessException>(() => orders.QueryAsync(new WorkOrderQueryDto
        {
            CustomFieldId = _fx.ProductFieldId,
            CustomFieldValue = "x",
            Page = 1,
            PageSize = 10
        }, _fx.FactoryId));

        await Assert.ThrowsAsync<BusinessException>(() => orders.QueryAsync(new WorkOrderQueryDto
        {
            CustomFieldId = _fx.Factory2FieldId,
            CustomFieldValue = "x",
            Page = 1,
            PageSize = 10
        }, _fx.FactoryId));
    }

    [Fact]
    public async Task Update_Omit_ShowInOrderList_Keeps_Original()
    {
        var (_, _, fields) = New();
        var before = (await fields.QueryByTargetAsync("work_order", _fx.FactoryId)).Data!
            .First(f => f.Id == _fx.SoFieldId);
        Assert.True(before.ShowInOrderList);

        await fields.UpdateAsync(_fx.SoFieldId, new CustomFieldCreateDto
        {
            Target = "work_order",
            FieldName = before.FieldName,
            FieldType = before.FieldType,
            Options = before.Options,
            ShowInOrderList = null
        }, _fx.FactoryId);

        var after = (await fields.QueryByTargetAsync("work_order", _fx.FactoryId)).Data!
            .First(f => f.Id == _fx.SoFieldId);
        Assert.True(after.ShowInOrderList);
    }

    [Fact]
    public async Task Reject_ShowInOrderList_On_Product_Field()
    {
        var (_, _, fields) = New();
        await Assert.ThrowsAsync<BusinessException>(() => fields.CreateAsync(new CustomFieldCreateDto
        {
            Target = "product",
            FieldName = "不应开启",
            FieldType = "text",
            ShowInOrderList = true
        }, _fx.FactoryId));
    }

    [Fact]
    public async Task Like_Special_Chars_Are_Literal()
    {
        var (_, orders, _) = New();
        var r = await orders.QueryAsync(new WorkOrderQueryDto
        {
            CustomFieldId = _fx.SoFieldId,
            CustomFieldValue = "SO%",
            CustomFieldMatch = "contains",
            Page = 1,
            PageSize = 10,
            ExcludeCancelled = true
        }, _fx.FactoryId);
        Assert.Equal(0, r.Code);
        Assert.Empty(r.Data!.List); // 字面 SO%，不是通配
    }
}

[CollectionDefinition("cf_list_db", DisableParallelization = true)]
public class CustomFieldListDbCollection : ICollectionFixture<CustomFieldListDbFixture> { }

public class CustomFieldListDbFixture : IDisposable
{
    public static string ConnString =>
        Environment.GetEnvironmentVariable("MES_TEST_DB")
        ?? "Server=localhost;Database=WorkshopMes_Test;Trusted_Connection=True;TrustServerCertificate=True";

    public long FactoryId;
    public long Factory2Id;
    public long SoFieldId;
    public long HiddenFieldId;
    public long ProductFieldId;
    public long Factory2FieldId;

    public CustomFieldListDbFixture()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnString).Options;
        using var db = new AppDbContext(opts);
        db.Database.EnsureCreated();
        DbCompat.EnsureAsync(db).GetAwaiter().GetResult();
        Reset(db);
        Seed(db);
    }

    public void Dispose() { }

    private static void Reset(AppDbContext db)
    {
        var codes = new[] { "TEST-CFLIST", "TEST-CFLIST2" };
        var fids = db.Factories.Where(f => codes.Contains(f.FactoryCode)).Select(f => f.Id).ToList();
        if (fids.Count == 0) return;

        var fieldIds = db.CustomFields.Where(f => fids.Contains(f.FactoryId)).Select(f => f.Id).ToList();
        var orderIds = db.WorkOrders.Where(o => fids.Contains(o.FactoryId)).Select(o => o.Id).ToList();
        var productIds = db.Products.Where(p => fids.Contains(p.FactoryId)).Select(p => p.Id).ToList();

        if (fieldIds.Count > 0)
        {
            db.CustomFieldValues.RemoveRange(db.CustomFieldValues.Where(v => fieldIds.Contains(v.FieldId)));
            db.CustomFieldOptions.RemoveRange(db.CustomFieldOptions.Where(o => fieldIds.Contains(o.FieldId)));
            db.CustomFields.RemoveRange(db.CustomFields.Where(f => fieldIds.Contains(f.Id)));
        }
        if (orderIds.Count > 0)
        {
            db.WorkOrderOperations.RemoveRange(db.WorkOrderOperations.Where(t => orderIds.Contains(t.WorkOrderId)));
            db.WorkOrders.RemoveRange(db.WorkOrders.Where(o => orderIds.Contains(o.Id)));
        }
        db.Products.RemoveRange(db.Products.Where(p => productIds.Contains(p.Id)));
        db.Units.RemoveRange(db.Units.Where(u => fids.Contains(u.FactoryId)));
        db.Users.RemoveRange(db.Users.Where(u => fids.Contains(u.FactoryId)));
        db.Factories.RemoveRange(db.Factories.Where(f => fids.Contains(f.Id)));
        db.SaveChanges();
    }

    private void Seed(AppDbContext db)
    {
        var now = DateTime.Now;
        var f = new SysFactory { FactoryCode = "TEST-CFLIST", FactoryName = "自定义列表厂", CreatedAt = now };
        var f2 = new SysFactory { FactoryCode = "TEST-CFLIST2", FactoryName = "隔离厂", CreatedAt = now };
        db.Factories.AddRange(f, f2);
        db.SaveChanges();
        FactoryId = f.Id;
        Factory2Id = f2.Id;

        var unit = new BaseUnit { FactoryId = f.Id, Name = "件", CreatedAt = now };
        db.Units.Add(unit);
        db.SaveChanges();

        var user = new SysUser
        {
            FactoryId = f.Id, Account = "cf_admin", Password = "x",
            Name = "测", Role = 1, Status = 1, CreatedAt = now
        };
        db.Users.Add(user);
        db.SaveChanges();

        var prod = new BaseProduct
        {
            FactoryId = f.Id, Code = "CF-P1", Name = "测试产品", UnitId = unit.Id, CreatedAt = now
        };
        db.Products.Add(prod);
        db.SaveChanges();

        var so = new SysCustomField
        {
            FactoryId = f.Id, Target = "work_order", FieldName = "销售订单号", FieldType = "text",
            ShowInOrderList = true, CreatedAt = now
        };
        var hidden = new SysCustomField
        {
            FactoryId = f.Id, Target = "work_order", FieldName = "隐藏筛选用", FieldType = "text",
            ShowInOrderList = false, CreatedAt = now
        };
        var productField = new SysCustomField
        {
            FactoryId = f.Id, Target = "product", FieldName = "产品备注", FieldType = "text",
            ShowInOrderList = false, CreatedAt = now
        };
        var f2Field = new SysCustomField
        {
            FactoryId = f2.Id, Target = "work_order", FieldName = "他厂字段", FieldType = "text",
            ShowInOrderList = true, CreatedAt = now
        };
        db.CustomFields.AddRange(so, hidden, productField, f2Field);
        db.SaveChanges();
        SoFieldId = so.Id;
        HiddenFieldId = hidden.Id;
        ProductFieldId = productField.Id;
        Factory2FieldId = f2Field.Id;

        void AddOrder(string no, string? soVal, string? hiddenVal = null)
        {
            var o = new ProdWorkOrder
            {
                FactoryId = f.Id, OrderNo = no, ProductId = prod.Id, Qty = 10, Status = 0,
                CreatedBy = user.Id, CreatedAt = now
            };
            db.WorkOrders.Add(o);
            db.SaveChanges();
            if (soVal != null)
                db.CustomFieldValues.Add(new SysCustomFieldValue { FieldId = so.Id, TargetId = o.Id, Value = soVal });
            if (hiddenVal != null)
                db.CustomFieldValues.Add(new SysCustomFieldValue { FieldId = hidden.Id, TargetId = o.Id, Value = hiddenVal });
            db.SaveChanges();
        }

        AddOrder("CF-A1", "SO202609001");
        AddOrder("CF-A2", "SO202609001");
        AddOrder("CF-A3", "SO202609001-01");
        AddOrder("CF-A4", "SO202609002", "HIDDEN-X");
        AddOrder("CF-A5", null);
    }
}
