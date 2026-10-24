using System.Text.Json;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;
using ahu.MicrosoftMes.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace WorkshopMes.Tests;

public class AttributePriceListTests
{
    public static Dictionary<long,SysCustomField> Fields() => new()
    {
        [1]=new(){Id=1,FactoryId=5,Target="product",FieldKey="size_inch",FieldName="尺寸",FieldType="number"},
        [2]=new(){Id=2,FactoryId=5,Target="product",FieldKey="rib_count",FieldName="骨数",FieldType="number"},
        [3]=new(){Id=3,FactoryId=5,Target="product",FieldKey="print_panels",FieldName="丝印片数",FieldType="number"}
    };
    public static AttributePriceDetails Detail() => new()
    {
        SchemaVersion=2,RequiredFieldIds=new(){1,2,3},Groups=new(){new(){Key="print",Name="丝印",Required=true}},
        Rows=new(){
            new(){RowId="base25",Amount=5,Confirmed=true,Source="合片/6",Conditions=new(){new(){FieldId=1,Value="25"},new(){FieldId=2,Value="8"}}},
            new(){RowId="none",Kind="extra",GroupKey="print",Amount=0,Confirmed=true,Source="测试：明确不丝印",Conditions=new(){new(){FieldId=3,Value="0"}}},
            new(){RowId="one",Kind="extra",GroupKey="print",Amount=.25m,Confirmed=true,Source="合片/12",Conditions=new(){new(){FieldId=3,Value="1"}}},
            new(){RowId="two-four",Kind="extra",GroupKey="print",Amount=.5m,Confirmed=true,Source="合片/13",Conditions=new(){new(){FieldId=3,Operator="range",Min=2,Max=4}}},
            new(){RowId="five-eight",Kind="extra",GroupKey="print",Amount=1.65m,Confirmed=true,Source="合片/14",Conditions=new(){new(){FieldId=3,Operator="range",Min=5,Max=8}}}
        }
    };
    public static OperationPriceList Table(AttributePriceDetails? d=null)=>new(){Id=1,FactoryId=5,Code="SIM400",OperationId=42,Status=1,EffectiveFrom=new(2026,1,1),Version="v1",PriceUnit="打",ReportUnitId=18,UnitsPerPriceUnit=12,Details=OperationPricingService.Write(d??Detail())};
    public static ProcessingQuote Quote(int panels,AttributePriceDetails? d=null)=>OperationPriceListService.Calculate(Table(d),10,18,new(){[1]="25",[2]="8",[3]=panels.ToString()},Fields(),new(2026,10,24));

    [Fact] public void PrintTiers_AreFixedAmounts_NotPerPanel()
    {
        Assert.Equal(5m,Quote(0).UnitPrice);Assert.Equal(5.25m,Quote(1).UnitPrice);
        foreach(var n in new[]{2,3,4})Assert.Equal(5.5m,Quote(n).UnitPrice);
        foreach(var n in new[]{5,8})Assert.Equal(6.65m,Quote(n).UnitPrice);
        Assert.Throws<BusinessException>(()=>Quote(9));
    }
    [Fact] public void MissingAttribute_IsNotZero_AndMismatchedUnitFails()
    {
        Assert.Throws<BusinessException>(()=>OperationPriceListService.Calculate(Table(),10,18,new(){[1]="25",[2]="8"},Fields(),new(2026,10,24)));
        Assert.Throws<BusinessException>(()=>OperationPriceListService.Calculate(Table(),10,19,new(){[1]="25",[2]="8",[3]="1"},Fields(),new(2026,10,24)));
    }
    [Fact] public void OverlapAndDraftPrice_CannotBeEnabled()
    {
        var d=Detail(); d.Rows[3].Conditions[0].Min=1;
        Assert.Throws<BusinessException>(()=>OperationPriceListService.Validate(d,Fields(),true));
        d=Detail();d.Rows[1].Amount=null;Assert.Throws<BusinessException>(()=>OperationPriceListService.Validate(d,Fields(),true));
        d=Detail();d.Rows[0].Confirmed=false;Assert.Throws<BusinessException>(()=>OperationPriceListService.Validate(d,Fields(),true));
    }
    [Fact] public void FinalPrice_IncludesOnlyExplicitRows_AndKeepsActualAttributes()
    {
        var d=Detail();d.Rows[0].Kind="final";d.Rows[0].Amount=7.7m;d.Rows[0].IncludedRowIds.Add("one");
        var q=Quote(1,d);Assert.Equal(7.7m,q.UnitPrice);Assert.True(q.Lines[0].Included);Assert.Equal("1",q.AttributeEvidence!.Attributes[3]);
        Assert.Equal(9.35m,Quote(8,d).UnitPrice);
    }
    [Fact] public void IntervalOpenEnds_AndNonApplicableMissingFields()
    {
        var fs=Fields();var v=new Dictionary<long,string?>{[1]="25"};
        Assert.True(OperationPriceListService.Matches(new[]{new AttributeCondition{FieldId=1,Operator="range",Min=25,Max=27,IncludeMax=false}},v,fs));
        Assert.False(OperationPriceListService.Matches(new[]{new AttributeCondition{FieldId=1,Operator="range",Min=25,IncludeMin=false}},v,fs));
        Assert.False(OperationPriceListService.Matches(new[]{new AttributeCondition{FieldId=2,Value="8"},new AttributeCondition{FieldId=1,Value="32"}},v,fs));
    }
    [Fact] public void WageUsesOriginalRateAndSnapshot_AfterPriceMutation()
    {
        var q=Quote(1);var snapshot=new ReportPriceSnapshot(-8,1,q.UnitPrice,0){Processing=q};
        var r=new ProdReport{GoodQty=1200,PriceSnapshot=JsonSerializer.Serialize(snapshot)};
        Assert.Equal(525m,ReportPriceSnapshot.Calculate(r,1).Amount);Assert.Equal(420m,ReportPriceSnapshot.Calculate(r,.8m).Amount);
        var copy=OperationPricingService.Read<ProcessingQuote>(OperationPricingService.Write(q));
        Assert.Equal(q.QuoteToken,copy.QuoteToken);Assert.Equal("合片/6",copy.AttributeEvidence!.Rows[0].Source);
    }
    [Fact] public async Task ProductAttributes_AreAtomic_Copyable_AndFactoryScoped()
    {
        await using var conn=new SqliteConnection("Data Source=:memory:");await conn.OpenAsync();
        await using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options);await db.Database.EnsureCreatedAsync();
        db.CustomFields.AddRange(Fields().Values);await db.SaveChangesAsync();
        var service=new ProductService(db);
        await service.CreateAsync(new(){Code="P1",Name="模拟",CustomValues=new(){[1]="25",[2]="8",[3]="3"}},5);
        var id=(await db.Products.SingleAsync()).Id;
        await service.CreateAsync(new(){Code="P2",Name="复制",CopyFromId=id},5);
        var p2=await db.Products.SingleAsync(p=>p.Code=="P2");Assert.Equal("3",(await service.GetAsync(p2.Id,5)).Data!.CustomValues![3]);
        await Assert.ThrowsAsync<BusinessException>(()=>service.CreateAsync(new(){Code="P3",Name="非法",CustomValues=new(){[3]="2.5"}},5));
        db.ChangeTracker.Clear();Assert.False(await db.Products.AnyAsync(p=>p.Code=="P3"));
        await Assert.ThrowsAsync<BusinessException>(()=>service.CreateAsync(new(){Code="OTHER",Name="跨厂",CustomValues=new(){[3]="1"}},6));
    }
    [Fact] public async Task PriceLists_EnforceFactoryVersionAndOverlappingDates()
    {
        await using var conn=new SqliteConnection("Data Source=:memory:");await conn.OpenAsync();
        await using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options);await db.Database.EnsureCreatedAsync();
        db.CustomFields.AddRange(Fields().Values);db.Operations.Add(new(){Id=42,FactoryId=5,Code="05",Name="合片"});db.Units.Add(new(){Id=18,FactoryId=5,Name="把"});await db.SaveChangesAsync();
        var svc=new OperationPriceListService(db);var input=new PriceListInput{Code="PL1",Name="测试",OperationId=42,Status=1,EffectiveFrom=new(2026,1,1),PriceUnit="打",ReportUnitId=18,UnitsPerPriceUnit=12,Details=Detail()};
        var id=await svc.SaveAsync(null,input,5);await Assert.ThrowsAsync<BusinessException>(()=>svc.GetAsync(id,6));
        input.Code="PL2";await Assert.ThrowsAsync<BusinessException>(()=>svc.SaveAsync(null,input,5));
        input.Code="PL1";await Assert.ThrowsAsync<BusinessException>(()=>svc.SaveAsync(id,input,5));
    }
}
