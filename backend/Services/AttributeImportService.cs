using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;

namespace ahu.MicrosoftMes.Services;

/// <summary>产品属性与工序价目表的预检及导入；价格文件始终先落草稿。</summary>
public class AttributeImportService(AppDbContext db)
{
    public async Task<byte[]> TemplateAsync(string type, long factoryId)
    {
        if (type != "product") throw ThrowHelper.BizUser("价目表格式已升级，按408更新后使用，旧行级导入已停用");
        using var wb = new XLWorkbook();
        if (type == "product")
        {
            var fields = await db.CustomFields.AsNoTracking().Where(f => f.FactoryId == factoryId && f.Target == "product" && f.FieldKey != null).OrderBy(f => f.Id).ToListAsync();
            Sheet(wb, "产品", new[] { "编号", "名称", "单位", "工艺路线编号", "供应商(可空)", "单价(可空)" }.Concat(fields.Select(f => "属性:"+f.FieldKey)).ToArray());
        }
        else
        {
            Sheet(wb, "表头", new[] { "编号", "名称", "工序编号", "生效日期", "失效日期", "计价单位", "报工单位", "换算数量" });
            Sheet(wb, "价格明细", new[] { "表编号", "行编号", "性质(base/extra/final)", "产品编号(空不限)", "金额", "不良扣价", "收费组编码", "包含行编号(逗号分隔)", "已确认(是/否)", "来源", "备注" });
            Sheet(wb, "属性条件", new[] { "表编号", "行编号", "字段编码", "运算(eq/range)", "等于值", "下界", "上界", "含下界(是/否)", "含上界(是/否)" });
            Sheet(wb, "收费组", new[] { "表编号", "组编码", "名称", "必须命中(是/否)" });
            wb.Worksheet("属性条件").Cell(1,2).GetComment().AddText("收费组适用条件在行编号填写 @组编码；空条件表示不限。");
        }
        using var ms = new MemoryStream(); wb.SaveAs(ms); return ms.ToArray();
    }
    private static IXLWorksheet Sheet(XLWorkbook wb, string name, string[] headers)
    {
        var ws = wb.AddWorksheet(name);
        for (var i=0;i<headers.Length;i++) { ws.Cell(1,i+1).Value=headers[i]; ws.Cell(1,i+1).Style.Font.Bold=true; }
        ws.SheetView.FreezeRows(1); ws.Columns().Width=20; return ws;
    }
    private static string Cell(IXLWorksheet ws, int row, int col) => ws.Cell(row,col).GetString().Trim();
    private static decimal Decimal(string text) => decimal.TryParse(text, System.Globalization.NumberStyles.AllowLeadingSign | System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : throw ThrowHelper.BizUser($"无效金额/数字：{text}");
    private static DateTime Date(string text) => DateTime.TryParse(text, out var date) ? date.Date : throw ThrowHelper.BizUser($"无效日期：{text}");
    private static bool Yes(string text) => text switch { "是" => true, "否" => false, _ => throw ThrowHelper.BizUser($"请明确填写是或否：{text}") };

    public async Task<ApiResult<ImportResultDto>> ImportAsync(string type, byte[] bytes, long factoryId, bool preview)
    {
        if (type != "product") throw ThrowHelper.BizUser("价目表格式已升级，按408更新后使用，旧行级导入已停用");
        using var ms = new MemoryStream(bytes);
        using var wb = new XLWorkbook(ms);
        return type == "product" ? await ProductsAsync(wb, factoryId, preview) : await PricesAsync(wb, factoryId, preview);
    }

    private async Task<ApiResult<ImportResultDto>> ProductsAsync(XLWorkbook wb, long fid, bool preview)
    {
        var ws = wb.Worksheet(1);
        var fields = await db.CustomFields.AsNoTracking().Where(f => f.FactoryId == fid && f.Target == "product").ToListAsync();
        var columns = new Dictionary<int, long>();
        for (var col=7;col<=(ws.LastColumnUsed()?.ColumnNumber()??6);col++)
        {
            var header=Cell(ws,1,col);
            var f=fields.SingleOrDefault(f => f.FieldKey != null && header == "属性:"+f.FieldKey);
            if (f==null) throw ThrowHelper.BizUser($"第{col}列未知属性编码：{header}");
            if (columns.Values.Contains(f.Id)) throw ThrowHelper.BizUser("模板含重复属性列");
            columns[col]=f.Id;
        }
        var options=await db.CustomFieldOptions.Where(o=>columns.Values.Contains(o.FieldId)).ToListAsync();
        var units=await db.Units.Where(u=>u.FactoryId==fid).ToListAsync();
        var routes=await db.Routings.Where(r=>r.FactoryId==fid).ToListAsync();
        var result=new ImportResultDto(); var plans=new List<(int Row,ProductCreateDto Dto)>(); var codes=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for(var row=2;row<=(ws.LastRowUsed()?.RowNumber()??1);row++)
        {
            if (ws.Row(row).CellsUsed().All(c=>c.IsEmpty())) continue;
            try
            {
                var code=Cell(ws,row,1); var name=Cell(ws,row,2);
                OperationPriceListService.Require(code.Length>0&&name.Length>0,"产品编号/名称必填");
                OperationPriceListService.Require(codes.Add(code),"文件内产品编号重复");
                OperationPriceListService.Require(!await db.Products.AnyAsync(p=>p.FactoryId==fid&&p.Code==code),"产品编号已存在，禁止覆盖或重复插入");
                var unit=Cell(ws,row,3); var route=Cell(ws,row,4);
                OperationPriceListService.Require(unit.Length==0||units.Count(u=>u.Name==unit)==1,"单位不存在或不唯一");
                OperationPriceListService.Require(route.Length==0||routes.Any(r=>r.Code==route),"路线编号不存在");
                var values=new Dictionary<long,string?>();
                foreach(var (col,id) in columns)
                {
                    var value=Cell(ws,row,col); var field=fields.Single(f=>f.Id==id);
                    if(value.Length==0){values[id]=null;continue;}
                    OperationPriceListService.Require(value.Length<=256,"属性值超过256字");
                    if(field.FieldType=="number") { var n=Decimal(value); OperationPriceListService.Require(n>=0 && (field.FieldKey is not ("rib_count" or "print_panels") || decimal.Truncate(n)==n),"属性须为非负数，骨数/丝印片数须整数"); }
                    if(field.FieldType=="single") OperationPriceListService.Require(options.Any(o=>o.FieldId==id&&o.Label==value),$"{field.FieldName}的选项无效");
                    values[id]=value;
                }
                plans.Add((row,new ProductCreateDto{Code=code,Name=name,UnitId=units.SingleOrDefault(u=>u.Name==unit)?.Id,RoutingId=routes.SingleOrDefault(r=>r.Code==route)?.Id,Supplier=Cell(ws,row,5),Price=Cell(ws,row,6).Length==0?null:Decimal(Cell(ws,row,6)),CustomValues=values}));
            }
            catch(BusinessException ex){result.FailCount++;result.Errors.Add($"第{row}行：{ex.Message}");}
        }
        // 整批预检有误则不写，避免使用旧版逐行导入造成资料不完整。
        if(result.FailCount>0)return ApiResult<ImportResultDto>.Ok(result);
        if(preview){result.SuccessCount=plans.Count;return ApiResult<ImportResultDto>.Ok(result);}
        foreach(var plan in plans)
        {
            await new ProductService(db).CreateAsync(plan.Dto,fid);
            result.SuccessCount++;
        }
        return ApiResult<ImportResultDto>.Ok(result);
    }

    private async Task<ApiResult<ImportResultDto>> PricesAsync(XLWorkbook wb,long fid,bool preview)
    {
        foreach(var name in new[]{"表头","价格明细","属性条件","收费组"}) OperationPriceListService.Require(wb.Worksheets.Any(s=>s.Name==name),$"缺少工作表：{name}");
        var headers=wb.Worksheet("表头");var lines=wb.Worksheet("价格明细");var conditions=wb.Worksheet("属性条件");var groups=wb.Worksheet("收费组");
        var fields=await db.CustomFields.Where(f=>f.FactoryId==fid&&f.Target=="product").ToDictionaryAsync(f=>f.Id);
        var operations=await db.Operations.Where(o=>o.FactoryId==fid).ToListAsync();var units=await db.Units.Where(u=>u.FactoryId==fid).ToListAsync();var products=await db.Products.Where(p=>p.FactoryId==fid).ToListAsync();
        var plans=new Dictionary<string,PriceListInput>();
        var result=new ImportResultDto();
        try
        {
            for(var row=2;row<=(headers.LastRowUsed()?.RowNumber()??1);row++)
            {
                var code=Cell(headers,row,1);if(code.Length==0)continue;
                OperationPriceListService.Require(!plans.ContainsKey(code)&&!await db.OperationPriceLists.AnyAsync(t=>t.FactoryId==fid&&t.Code==code),$"表编号{code}重复/已存在，禁止覆盖");
                var op=operations.SingleOrDefault(o=>o.Code==Cell(headers,row,3))??throw ThrowHelper.BizUser("工序编号不存在");
                var unit=units.SingleOrDefault(u=>u.Name==Cell(headers,row,7))??throw ThrowHelper.BizUser("报工单位不存在或不唯一");
                plans[code]=new PriceListInput{Code=code,Name=Cell(headers,row,2),OperationId=op.Id,Status=0,EffectiveFrom=Date(Cell(headers,row,4)),EffectiveTo=Cell(headers,row,5).Length==0?null:Date(Cell(headers,row,5)),PriceUnit=Cell(headers,row,6),ReportUnitId=unit.Id,UnitsPerPriceUnit=Decimal(Cell(headers,row,8))};
            }
            for(var row=2;row<=(groups.LastRowUsed()?.RowNumber()??1);row++)
            {
                var code=Cell(groups,row,1);if(code.Length==0)continue;
                OperationPriceListService.Require(plans.ContainsKey(code),"收费组的表编号无效");
                plans[code].Details.Groups.Add(new PriceChargeGroup{Key=Cell(groups,row,2),Name=Cell(groups,row,3),Required=Yes(Cell(groups,row,4))});
            }
            for(var row=2;row<=(lines.LastRowUsed()?.RowNumber()??1);row++)
            {
                var code=Cell(lines,row,1);if(code.Length==0)continue;
                OperationPriceListService.Require(plans.ContainsKey(code),"明细的表编号无效");
                var product=Cell(lines,row,4);OperationPriceListService.Require(product.Length==0||products.Any(p=>p.Code==product),"明细产品编号不存在");
                plans[code].Details.Rows.Add(new AttributePriceRow{RowId=Cell(lines,row,2),Kind=Cell(lines,row,3),ProductId=product.Length==0?null:products.Single(p=>p.Code==product).Id,Amount=Cell(lines,row,5).Length==0?null:Decimal(Cell(lines,row,5)),DeductPrice=Cell(lines,row,6).Length==0?0:Decimal(Cell(lines,row,6)),GroupKey=Cell(lines,row,7),IncludedRowIds=Cell(lines,row,8).Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).ToList(),Confirmed=Yes(Cell(lines,row,9)),Source=Cell(lines,row,10),Note=Cell(lines,row,11)});
            }
            for(var row=2;row<=(conditions.LastRowUsed()?.RowNumber()??1);row++)
            {
                var code=Cell(conditions,row,1);if(code.Length==0)continue;
                OperationPriceListService.Require(plans.ContainsKey(code),"条件的表编号无效");
                var key=Cell(conditions,row,3);var field=fields.Values.SingleOrDefault(f=>f.FieldKey==key)??throw ThrowHelper.BizUser($"条件字段编码不存在：{key}");
                var c=new AttributeCondition{FieldId=field.Id,Operator=Cell(conditions,row,4),Value=Cell(conditions,row,5),Min=Cell(conditions,row,6).Length==0?null:Decimal(Cell(conditions,row,6)),Max=Cell(conditions,row,7).Length==0?null:Decimal(Cell(conditions,row,7)),IncludeMin=Yes(Cell(conditions,row,8)),IncludeMax=Yes(Cell(conditions,row,9))};
                var target=Cell(conditions,row,2);
                if(target.StartsWith('@')) (plans[code].Details.Groups.SingleOrDefault(g=>g.Key==target[1..])??throw ThrowHelper.BizUser("条件收费组不存在")).Conditions.Add(c);
                else (plans[code].Details.Rows.SingleOrDefault(r=>r.RowId==target)??throw ThrowHelper.BizUser("条件价格行不存在")).Conditions.Add(c);
            }
            foreach(var plan in plans.Values)
            {
                OperationPriceListService.Require(plan.Name.Length>0&&plan.Code.Length<=64&&plan.Name.Length<=128&&plan.PriceUnit.Length is >0 and <=32&&plan.UnitsPerPriceUnit is >0 and <=1000000&&(!plan.EffectiveTo.HasValue||plan.EffectiveTo>=plan.EffectiveFrom),$"表{plan.Code}表头不完整");
                OperationPriceListService.Validate(plan.Details,fields,false);
            }
            OperationPriceListService.Require(plans.Count>0,"无价目表可导入");
        }
        catch(BusinessException ex){result.FailCount=1;result.Errors.Add($"价目表预检：{ex.Message}");return ApiResult<ImportResultDto>.Ok(result);}
        if(!preview)foreach(var plan in plans.Values)await new OperationPriceListService(db).SaveAsync(null,plan,fid);
        result.SuccessCount=plans.Count;
        return ApiResult<ImportResultDto>.Ok(result);
    }
}
