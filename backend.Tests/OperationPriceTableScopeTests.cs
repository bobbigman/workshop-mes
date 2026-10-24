using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;
using ahu.MicrosoftMes.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace WorkshopMes.Tests;

public class OperationPriceTableScopeTests
{
    private sealed class SqliteLockSyntax : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken=default)
        { command.CommandText=command.CommandText.Replace(" WITH (UPDLOCK, ROWLOCK)", ""); return ValueTask.FromResult(result); }
    }
    private sealed class Fixture : IAsyncDisposable
    {
        private SqliteConnection? connection;
        public AppDbContext Db { get; private set; } = null!;
        public async Task InitAsync()
        {
            var sql = Environment.GetEnvironmentVariable("MES_410_TEST_SQL") == "1";
            var options = new DbContextOptionsBuilder<AppDbContext>();
            if (sql) options.UseSqlServer($"Server=lpc:localhost;Database=WorkshopMes_410_Test_{Guid.NewGuid():N};Integrated Security=true;Encrypt=false;TrustServerCertificate=true");
            else { connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync(); options.UseSqlite(connection).AddInterceptors(new SqliteLockSyntax()); }
            Db = new AppDbContext(options.Options); await Db.Database.EnsureCreatedAsync();
            Db.Operations.AddRange(new BaseOperation { Id=42, FactoryId=5,Code="OP",Name="合片" },new BaseOperation { Id=43, FactoryId=5,Code="OP2",Name="小裁" });
            Db.Units.Add(new BaseUnit { Id=18,FactoryId=5,Name="把" });
            Db.Routings.Add(new BaseRouting { Id=20,FactoryId=5,Code="R",Name="路线" });
            Db.RoutingSteps.Add(new BaseRoutingStep { RoutingId=20,OperationId=42,Seq=1 });
            Db.Products.AddRange(new BaseProduct { Id=10,FactoryId=5,Code="P1",Name="产品",UnitId=18,RoutingId=20 },new BaseProduct { Id=11,FactoryId=5,Code="P2",Name="产品2",UnitId=18,RoutingId=20 });
            var fields=AttributePriceListTests.Fields();fields[3].Target="work_order_task";fields[3].OperationId=42;
            Db.CustomFields.AddRange(fields.Values);
            Db.CustomFields.Add(new SysCustomField { Id=4,FactoryId=5,Target="work_order_task",OperationId=43,FieldKey="print_panels",FieldName="小裁丝印",FieldType="number" });
            Db.CustomFieldValues.AddRange(new SysCustomFieldValue { FieldId=1,TargetId=10,Value="25" },new SysCustomFieldValue { FieldId=2,TargetId=10,Value="8" },new SysCustomFieldValue { FieldId=1,TargetId=11,Value="25" },new SysCustomFieldValue { FieldId=2,TargetId=11,Value="8" });
            if (!sql) await Db.SaveChangesAsync();
            else
            {
                await Db.Database.OpenConnectionAsync();
                var seeds=Db.ChangeTracker.Entries().Select(e=>e.Entity).ToList();Db.ChangeTracker.Clear();
                foreach(var group in seeds.GroupBy(e=>e.GetType()))
                {
                    Db.AddRange(group);var table=Db.Model.FindEntityType(group.Key)!.GetTableName();
                    var explicitId=group.Any(e=>(long?)group.Key.GetProperty("Id")?.GetValue(e)>0);
                    // 表名仅来自EF模型，无用户输入；固定种子ID仅用于隔离测试库。
#pragma warning disable EF1002
                    if(explicitId)await Db.Database.ExecuteSqlRawAsync($"SET IDENTITY_INSERT [{table}] ON");
                    await Db.SaveChangesAsync();
                    if(explicitId)await Db.Database.ExecuteSqlRawAsync($"SET IDENTITY_INSERT [{table}] OFF");
#pragma warning restore EF1002
                }
                await Db.Database.CloseConnectionAsync();
            }
        }
        public async ValueTask DisposeAsync()
        {
            if(Db!=null){if(Db.Database.IsSqlServer())await Db.Database.EnsureDeletedAsync();await Db.DisposeAsync();}
            if(connection!=null)await connection.DisposeAsync();
        }
    }
    private static PriceListInput Input(string code,long? productId=null) => new(){Code=code,Name=code,OperationId=42,ProductId=productId,EffectiveFrom=new(2026,1,1),PriceUnit="打",ReportUnitId=18,UnitsPerPriceUnit=12,Details=AttributePriceListTests.Detail()};
    private static PriceListInput From(OperationPriceList t)=>new(){Code=t.Code,Name=t.Name,OperationId=t.OperationId,ProductId=t.ProductId,Remark=t.Remark,Status=t.Status,EffectiveFrom=t.EffectiveFrom,EffectiveTo=t.EffectiveTo,PriceUnit=t.PriceUnit,ReportUnitId=t.ReportUnitId,UnitsPerPriceUnit=t.UnitsPerPriceUnit,ExpectedVersion=t.Version,Details=OperationPriceListService.Details(t)};

    [Fact] public void Selection_IsAtTableLevel_AndLegacyBlocks()
    {
        var common=AttributePriceListTests.Table();var specific=AttributePriceListTests.Table();specific.Id=2;specific.ProductId=10;
        Assert.Same(specific,OperationPriceListService.SelectTable(new[]{common,specific},10,42));
        Assert.Same(common,OperationPriceListService.SelectTable(new[]{common,specific},11,42));
        var old=AttributePriceListTests.Detail();old.SchemaVersion=1;common.Details=OperationPricingService.Write(old);
        Assert.Throws<BusinessException>(()=>OperationPriceListService.SelectTable(new[]{common,specific},10,42));
    }
    [Fact] public async Task Status_LocksOperation_PreservesStopped_AndRejectsStaleWrites()
    {
        await using var f=new Fixture();await f.InitAsync();var svc=new OperationPriceListService(f.Db);
        var input=Input("draft");input.Details.Rows[0].Confirmed=false;var id=await svc.SaveAsync(null,input,5);
        var t=await svc.GetAsync(id,5);Assert.Equal(0,t.Status);Assert.Null(t.ActivatedAt);
        input=From(t);input.Details.Rows[0].Confirmed=true;await svc.SaveAsync(id,input,5);
        t=await svc.GetAsync(id,5);Assert.Equal(1,t.Status);Assert.NotNull(t.ActivatedAt);
        await svc.SetStatusAsync(id,2,t.Version,5);t=await svc.GetAsync(id,5);
        input=From(t);input.Details.Rows[0].Confirmed=false;await svc.SaveAsync(id,input,5);t=await svc.GetAsync(id,5);Assert.Equal(2,t.Status);
        await Assert.ThrowsAsync<BusinessException>(()=>svc.SetStatusAsync(id,1,t.Version,5));f.Db.ChangeTracker.Clear();
        input=From(await svc.GetAsync(id,5));input.OperationId=43;await Assert.ThrowsAsync<BusinessException>(()=>svc.SaveAsync(id,input,5));f.Db.ChangeTracker.Clear();
        input=From(await svc.GetAsync(id,5));input.ExpectedVersion="stale";await Assert.ThrowsAsync<BusinessException>(()=>svc.SaveAsync(id,input,5));
    }
    [Fact] public async Task DedicatedAndCommon_Coexist_OverlapIsPerProduct_AndNoFallbackOnMissingSpec()
    {
        await using var f=new Fixture();await f.InitAsync();var svc=new OperationPriceListService(f.Db);
        await svc.SaveAsync(null,Input("common"),5);var special=Input("special",10);special.Details.Rows[0].Conditions[0].Value="34";
        await svc.SaveAsync(null,special,5);await svc.SaveAsync(null,Input("other",11),5);
        await Assert.ThrowsAsync<BusinessException>(()=>svc.SaveAsync(null,Input("clash",10),5));f.Db.ChangeTracker.Clear();
        var ex=await Assert.ThrowsAsync<BusinessException>(()=>svc.PreviewAsync(10,42,5,new(2026,10,24),new(){[3]="1"}));Assert.Contains("缺规格",ex.Message);
        Assert.Equal(5.25m,(await svc.PreviewAsync(11,42,5,new(2026,10,24),new(){[3]="1"})).UnitPrice);
    }
    [Fact] public async Task Preview_RejectsForeignOrOtherOperationFields_AndNullIsUnknown()
    {
        await using var f=new Fixture();await f.InitAsync();var svc=new OperationPriceListService(f.Db);await svc.SaveAsync(null,Input("common"),5);
        await Assert.ThrowsAsync<BusinessException>(()=>svc.PreviewAsync(10,42,5,new(2026,10,24),new(){[4]="1"}));
        await Assert.ThrowsAsync<BusinessException>(()=>svc.PreviewAsync(10,42,5,new(2026,10,24),new(){[1]="34"}));
        await Assert.ThrowsAsync<BusinessException>(()=>svc.PreviewAsync(10,42,5,new(2026,10,24),new(){[3]=null}));
        await Assert.ThrowsAsync<BusinessException>(()=>svc.PreviewAsync(10,42,6,new(2026,10,24),new(){[3]="1"}));
        Assert.Equal(5m,(await svc.PreviewAsync(10,42,5,new(2026,10,24),new(){[3]="0"})).UnitPrice);
    }
    [Fact] public async Task DraftDeletionAndReferences_AreGuarded_AndLegacyImportIsBlocked()
    {
        await using var f=new Fixture();await f.InitAsync();var svc=new OperationPriceListService(f.Db);var input=Input("draft",10);input.Details.Rows.Clear();var id=await svc.SaveAsync(null,input,5);
        Assert.NotNull(await DeleteGuard.CheckAsync(f.Db,"Product",10));await Assert.ThrowsAsync<BusinessException>(()=>svc.DeleteAsync(id,"stale",5));
        f.Db.ChangeTracker.Clear();await svc.DeleteAsync(id,(await svc.GetAsync(id,5)).Version,5);Assert.False(await f.Db.OperationPriceLists.AnyAsync());
        await Assert.ThrowsAsync<BusinessException>(()=>new AttributeImportService(f.Db).ImportAsync("operationPriceList",Array.Empty<byte>(),5,true));
        await Assert.ThrowsAsync<BusinessException>(()=>new AttributeImportService(f.Db).TemplateAsync("operationPriceList",5));
    }
    [Fact] public async Task NewOrders_LockPerOperation_CopyReprices_AndOldSnapshotRemains()
    {
        await using var f=new Fixture();await f.InitAsync();var svc=new OperationPriceListService(f.Db);var id=await svc.SaveAsync(null,Input("common"),5);await svc.SetModeAsync(10,42,5,true);
        var q=await svc.PreviewAsync(10,42,5,operationAttributes:new(){[3]="1"});
        var orders=new WorkOrderService(f.Db);await orders.CreateAsync(new(){OrderNo="T1",ProductId=10,Qty=120,OperationAttributes=new(){[42]=new(){[3]="1"}},ProcessingChoices=new(){[42]=new(){QuoteToken=q.QuoteToken}}},1,5);
        var order=await f.Db.WorkOrders.SingleAsync();var task=await f.Db.WorkOrderOperations.SingleAsync();var snapshot=task.ProcessingPrice;
        var t=await svc.GetAsync(id,5);var input=From(t);input.Details.Rows[0].Amount=6;await svc.SaveAsync(id,input,5);
        await orders.UpdateAsync(order.Id,new(){Qty=240},5);Assert.Equal(snapshot,(await f.Db.WorkOrderOperations.SingleAsync()).ProcessingPrice);
        await Assert.ThrowsAsync<BusinessException>(()=>orders.UpdateAsync(order.Id,new(){OperationAttributes=new(){[42]=new(){[3]="3"}}},5));f.Db.ChangeTracker.Clear();
        await orders.CopyAsync(order.Id,1,5);var copied=await f.Db.WorkOrders.SingleAsync(o=>o.Id!=order.Id);var copiedTask=await f.Db.WorkOrderOperations.SingleAsync(t=>t.WorkOrderId==copied.Id);
        Assert.Equal(6.25m,OperationPricingService.Read<ProcessingQuote>(copiedTask.ProcessingPrice!).UnitPrice);
        Assert.Equal("1",(await new OperationAttributeService(f.Db).GetAsync(copiedTask.Id,5,42))[3]);
        var stale=await Assert.ThrowsAsync<BusinessException>(()=>orders.CreateAsync(new(){OrderNo="STALE",ProductId=10,Qty=120,OperationAttributes=new(){[42]=new(){[3]="1"}},ProcessingChoices=new(){[42]=new(){QuoteToken=q.QuoteToken}}},1,5));
        f.Db.ChangeTracker.Clear();Assert.False(await f.Db.WorkOrders.AnyAsync(o=>o.OrderNo=="STALE"));
    }
    [Fact] public async Task FieldDefinition_ScopesCodeByOperation_AndReferencedOptionsCannotDisappear()
    {
        await using var f=new Fixture();await f.InitAsync();var fields=new CustomFieldService(f.Db);
        await Assert.ThrowsAsync<BusinessException>(()=>fields.CreateAsync(new(){Target="work_order_task",OperationId=43,FieldName="重复",FieldType="number",FieldKey="print_panels"},5));
        await Assert.ThrowsAsync<BusinessException>(()=>fields.CreateAsync(new(){Target="work_order_task",OperationId=999,FieldName="越权",FieldType="number"},5));
        var svc=new OperationPriceListService(f.Db);await svc.SaveAsync(null,Input("common"),5);await Assert.ThrowsAsync<BusinessException>(()=>fields.DeleteAsync(3,5));
    }
    [Fact] public void PendingNoteAndLineProductBinding_CannotBeEnabled()
    {
        var d=AttributePriceListTests.Detail();d.Rows[0].Note="待确认";Assert.Throws<BusinessException>(()=>OperationPriceListService.Validate(d,AttributePriceListTests.Fields(),true));
        d.Rows[0].Note="";d.Rows[0].ProductId=10;Assert.Throws<BusinessException>(()=>OperationPriceListService.Validate(d,AttributePriceListTests.Fields(),false));
    }

    [Fact] public async Task SqlServer_ConcurrentEnable_AndInclusiveDateBoundary()
    {
        if(Environment.GetEnvironmentVariable("MES_410_TEST_SQL")!="1")return;
        await using var f=new Fixture();await f.InitAsync();
        var connection=f.Db.Database.GetConnectionString();
        async Task<bool> Save(string code)
        {
            await using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection).Options);
            try { await new OperationPriceListService(db).SaveAsync(null,Input(code,10),5);return true; }
            catch(BusinessException){return false;}
            catch(Microsoft.Data.SqlClient.SqlException ex) when(ex.Number==1205){return false;}
            catch(DbUpdateException ex) when(ex.InnerException is Microsoft.Data.SqlClient.SqlException sql && sql.Number==1205){return false;}
        }
        var outcomes=await Task.WhenAll(Save("race1"),Save("race2"));Assert.Single(outcomes,v=>v);
        Assert.Single(await f.Db.OperationPriceLists.Where(t=>t.Status==1).ToListAsync());
        var existing=await f.Db.OperationPriceLists.SingleAsync();var svc=new OperationPriceListService(f.Db);var update=From(existing);update.EffectiveTo=new(2026,10,24);await svc.SaveAsync(existing.Id,update,5);
        var next=Input("boundary",10);next.EffectiveFrom=new(2026,10,24);await Assert.ThrowsAsync<BusinessException>(()=>svc.SaveAsync(null,next,5));f.Db.ChangeTracker.Clear();
        next.EffectiveFrom=new(2026,10,25);await svc.SaveAsync(null,next,5);
    }

    [Fact] public async Task SqlServer_IncrementalUpgrade_IsRepeatable_AndPreservesLegacyRows()
    {
        if(Environment.GetEnvironmentVariable("MES_410_TEST_SQL")!="1")return;
        var name=$"WorkshopMes_410_Test_{Guid.NewGuid():N}";
        await using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer($"Server=lpc:localhost;Database={name};Integrated Security=true;Encrypt=false;TrustServerCertificate=true").Options);
        await using(var master=new Microsoft.Data.SqlClient.SqlConnection("Server=lpc:localhost;Database=master;Integrated Security=true;Encrypt=false;TrustServerCertificate=true"))
        {
            await master.OpenAsync();await using var create=master.CreateCommand();create.CommandText=$"CREATE DATABASE [{name}]";await create.ExecuteNonQueryAsync();
        }
        try
        {
            await db.Database.ExecuteSqlRawAsync("CREATE TABLE sys_custom_field(id BIGINT IDENTITY PRIMARY KEY,factory_id BIGINT NOT NULL,target NVARCHAR(32) NOT NULL,field_key NVARCHAR(32) NULL)");
            await db.Database.ExecuteSqlRawAsync(OperationPriceListSchema.UpgradeSql);
            var legacyJson="{\"schemaVersion\":1,\"rows\":[{\"rowId\":\"r1\",\"productId\":10}]}";
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO base_operation_price_list(factory_id,code,name,operation_id,status,effective_from,price_unit,report_unit_id,units_per_price_unit,version,details) VALUES(5,N'OLD',N'旧表',42,1,'2026-01-01',N'打',18,12,N'old',{legacyJson})");
            for(var n=0;n<2;n++){await db.Database.ExecuteSqlRawAsync(OperationPriceTableScopeSchema.AddColumnsSql);await db.Database.ExecuteSqlRawAsync(OperationPriceTableScopeSchema.FinalizeSql);}
            var old=await db.OperationPriceLists.SingleAsync();Assert.Null(old.ProductId);Assert.NotNull(old.ActivatedAt);Assert.Equal(1,OperationPriceListService.Details(old).SchemaVersion);Assert.Equal(10,OperationPriceListService.Details(old).Rows[0].ProductId);
        }
        finally { await db.Database.EnsureDeletedAsync(); }
    }
}
