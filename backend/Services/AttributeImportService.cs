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
        if (type != "product") throw ThrowHelper.BizUser("价目表导入格式已升级，旧的行级导入方式已停用。请按新模板格式（指令408）重新准备导入文件。");
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
    private static decimal Decimal(string text) => decimal.TryParse(text, System.Globalization.NumberStyles.AllowLeadingSign | System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : throw ThrowHelper.BizUser($"导入文件里有金额或数字格式不对：{text}。请检查所有数值列，去掉多余符号、空格或非数字字符后重新上传。");
    private static DateTime Date(string text) => DateTime.TryParse(text, out var date) ? date.Date : throw ThrowHelper.BizUser($"导入文件里有日期格式不对：{text}。请统一用标准日期格式（如 2026-01-15）填写所有日期列后重新上传。");
    private static bool Yes(string text) => text switch { "是" => true, "否" => false, _ => throw ThrowHelper.BizUser($"导入文件里有是/否字段填得不规范：{text}。请把该列明确填成「是」或「否」，不要留空或用其他写法后重新上传。") };

    public async Task<ApiResult<ImportResultDto>> ImportAsync(string type, byte[] bytes, long factoryId, bool preview)
    {
        if (type != "product") throw ThrowHelper.BizUser("价目表导入格式已升级，旧的行级导入方式已停用。请按新模板格式（指令408）重新准备导入文件。");
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
            if (f==null) throw ThrowHelper.BizUser($"导入模板第{col}列有系统不认识的属性代号：{header}。请下载最新导入模板，对照模板列名检查并修正文件中的列标题。");
            if (columns.Values.Contains(f.Id)) throw ThrowHelper.BizUser("导入文件里有重复的属性列。请删掉多余的重复列，确保每个属性只出现一次后重新上传。");
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
                OperationPriceListService.Require(code.Length>0&&name.Length>0,"产品编号和产品名称都必须填写。请在导入文件中补全缺失的编号或名称后重新上传。");
                OperationPriceListService.Require(codes.Add(code),"导入文件内有重复的产品编号。请检查并修改重复编号，确保文件内每个产品编号唯一后重新上传。");
                OperationPriceListService.Require(!await db.Products.AnyAsync(p=>p.FactoryId==fid&&p.Code==code),"该产品编号在系统中已存在，不允许通过导入覆盖。要更新已有产品请到「产品定义」直接编辑；要新增请换一个不重复的编号。");
                var unit=Cell(ws,row,3); var route=Cell(ws,row,4);
                OperationPriceListService.Require(unit.Length==0||units.Count(u=>u.Name==unit)==1,"导入文件里填的单位名称在系统中找不到，或有多个同名单位。请到「单位」确认单位名称准确且唯一，再在导入文件中修正后重新上传。");
                OperationPriceListService.Require(route.Length==0||routes.Any(r=>r.Code==route),"导入文件里填的工艺路线编号在系统中不存在。请到「工艺路线」核对路线编号，或在导入文件中修正后重新上传。");
                var values=new Dictionary<long,string?>();
                foreach(var (col,id) in columns)
                {
                    var value=Cell(ws,row,col); var field=fields.Single(f=>f.Id==id);
                    if(value.Length==0){values[id]=null;continue;}
                    OperationPriceListService.Require(value.Length<=256,"有属性值超过了256个字。请把该属性内容缩短后重新上传。");
                    if(field.FieldType=="number") { var n=Decimal(value); OperationPriceListService.Require(n>=0 && (field.FieldKey is not ("rib_count" or "print_panels") || decimal.Truncate(n)==n),"属性值必须是非负数，其中骨数和丝印片数还必须是整数。请检查导入文件对应列，去掉负数和小数后重新上传。"); }
                    if(field.FieldType=="single") OperationPriceListService.Require(options.Any(o=>o.FieldId==id&&o.Label==value),$"{field.FieldName}填的选项无效。请对照该字段的下拉选项列表，选择有效选项后重新上传。");
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
        foreach(var name in new[]{"表头","价格明细","属性条件","收费组"}) OperationPriceListService.Require(wb.Worksheets.Any(s=>s.Name==name),$"导入的 Excel 缺少必需的工作表：{name}。请按模板要求补全对应名称的工作表后重新上传。");
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
                OperationPriceListService.Require(!plans.ContainsKey(code)&&!await db.OperationPriceLists.AnyAsync(t=>t.FactoryId==fid&&t.Code==code),$"表编号{code}已存在，不能重复导入或覆盖。请改成不重复的新编号后重新上传。");
                var op=operations.SingleOrDefault(o=>o.Code==Cell(headers,row,3))??throw ThrowHelper.BizUser("导入文件里引用的工序编号在系统中不存在。请到「工序」核对工序编号，或在导入文件中修正后重新上传。");
                var unit=units.SingleOrDefault(u=>u.Name==Cell(headers,row,7))??throw ThrowHelper.BizUser("导入前请确认报工单位存在且唯一。");
                plans[code]=new PriceListInput{Code=code,Name=Cell(headers,row,2),OperationId=op.Id,Status=0,EffectiveFrom=Date(Cell(headers,row,4)),EffectiveTo=Cell(headers,row,5).Length==0?null:Date(Cell(headers,row,5)),PriceUnit=Cell(headers,row,6),ReportUnitId=unit.Id,UnitsPerPriceUnit=Decimal(Cell(headers,row,8))};
            }
            for(var row=2;row<=(groups.LastRowUsed()?.RowNumber()??1);row++)
            {
                var code=Cell(groups,row,1);if(code.Length==0)continue;
                OperationPriceListService.Require(plans.ContainsKey(code),"收费组关联的价目表编号无效。请检查导入文件中收费组对应的表编号是否正确、是否已存在。");
                plans[code].Details.Groups.Add(new PriceChargeGroup{Key=Cell(groups,row,2),Name=Cell(groups,row,3),Required=Yes(Cell(groups,row,4))});
            }
            for(var row=2;row<=(lines.LastRowUsed()?.RowNumber()??1);row++)
            {
                var code=Cell(lines,row,1);if(code.Length==0)continue;
                OperationPriceListService.Require(plans.ContainsKey(code),"明细行关联的价目表编号无效。请检查导入文件中明细行的表编号是否正确、是否已存在。");
                var product=Cell(lines,row,4);OperationPriceListService.Require(product.Length==0||products.Any(p=>p.Code==product),"明细行引用的产品编号在系统中不存在。请到「产品定义」核对产品编号，或在导入文件中修正后重新上传。");
                plans[code].Details.Rows.Add(new AttributePriceRow{RowId=Cell(lines,row,2),Kind=Cell(lines,row,3),ProductId=product.Length==0?null:products.Single(p=>p.Code==product).Id,Amount=Cell(lines,row,5).Length==0?null:Decimal(Cell(lines,row,5)),DeductPrice=Cell(lines,row,6).Length==0?0:Decimal(Cell(lines,row,6)),GroupKey=Cell(lines,row,7),IncludedRowIds=Cell(lines,row,8).Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).ToList(),Confirmed=Yes(Cell(lines,row,9)),Source=Cell(lines,row,10),Note=Cell(lines,row,11)});
            }
            for(var row=2;row<=(conditions.LastRowUsed()?.RowNumber()??1);row++)
            {
                var code=Cell(conditions,row,1);if(code.Length==0)continue;
                OperationPriceListService.Require(plans.ContainsKey(code),"取价条件关联的价目表编号无效。请检查导入文件中条件行的表编号是否正确、是否已存在。");
                var key=Cell(conditions,row,3);var field=fields.Values.SingleOrDefault(f=>f.FieldKey==key)??throw ThrowHelper.BizUser($"取价条件里引用的字段代号在系统中不存在：{key}。请到「自定义字段」核对字段代号，或在导入文件中修正。");
                var c=new AttributeCondition{FieldId=field.Id,Operator=Cell(conditions,row,4),Value=Cell(conditions,row,5),Min=Cell(conditions,row,6).Length==0?null:Decimal(Cell(conditions,row,6)),Max=Cell(conditions,row,7).Length==0?null:Decimal(Cell(conditions,row,7)),IncludeMin=Yes(Cell(conditions,row,8)),IncludeMax=Yes(Cell(conditions,row,9))};
                var target=Cell(conditions,row,2);
                if(target.StartsWith('@')) (plans[code].Details.Groups.SingleOrDefault(g=>g.Key==target[1..])??throw ThrowHelper.BizUser("取价条件引用的收费组不存在。请检查导入文件中条件行的收费组代号是否正确、是否已在价目表中定义。")).Conditions.Add(c);
                else (plans[code].Details.Rows.SingleOrDefault(r=>r.RowId==target)??throw ThrowHelper.BizUser("取价条件引用的价格行不存在。请检查导入文件中条件行的价格行编号是否正确、是否已在价目表中定义。")).Conditions.Add(c);
            }
            foreach(var plan in plans.Values)
            {
                OperationPriceListService.Require(plan.Name.Length>0&&plan.Code.Length<=64&&plan.Name.Length<=128&&plan.PriceUnit.Length is >0 and <=32&&plan.UnitsPerPriceUnit is >0 and <=1000000&&(!plan.EffectiveTo.HasValue||plan.EffectiveTo>=plan.EffectiveFrom),$"表{plan.Code}的表头信息不完整。请检查该价目表所有必填列是否都已填写，补全后重新上传。");
                OperationPriceListService.Validate(plan.Details,fields,false);
            }
            OperationPriceListService.Require(plans.Count>0,"导入文件里没有可识别的价目表数据。请确认文件格式和内容符合导入模板要求后重新上传。");
        }
        catch(BusinessException ex){result.FailCount=1;result.Errors.Add($"价目表预检：{ex.Message}");return ApiResult<ImportResultDto>.Ok(result);}
        if(!preview)foreach(var plan in plans.Values)await new OperationPriceListService(db).SaveAsync(null,plan,fid);
        result.SuccessCount=plans.Count;
        return ApiResult<ImportResultDto>.Ok(result);
    }
}
