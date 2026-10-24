using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;

namespace ahu.MicrosoftMes.Services;

public interface IImportService
{
    Task<byte[]> DownloadTemplateAsync(string entityType);
    Task<ApiResult<ImportResultDto>> ImportAsync(string entityType, byte[] fileBytes, long factoryId);
}

public class ImportResultDto
{
    public int SuccessCount { get; set; }
    public int FailCount { get; set; }
    public List<string> Errors { get; set; } = new();
}

public class ImportService : IImportService
{
    private readonly AppDbContext _db;
    public ImportService(AppDbContext db) { _db = db; }

    // 母版列定义（顺序即列）
    private static readonly Dictionary<string, string[]> Templates = new()
    {
        ["user"] = new[] { "账号", "手机号", "姓名", "角色(1管理员/2生产/3班组长)", "初始密码(可空)", "部门编码(逗号分隔,可空)", "人员类型(内部/临时/外部,可空)" },
        ["defect"] = new[] { "名称" },
        ["operation"] = new[] { "编号", "名称", "报工部门编码(逗号分隔)", "不良品项名称(逗号分隔)" },
        ["routing"] = new[] { "编号", "名称", "工序编码(按顺序逗号分隔)" },
        ["product"] = new[] { "编号", "名称", "单位", "工艺路线编号", "供应商(可空)", "单价(可空)" },
    };

    public Task<byte[]> DownloadTemplateAsync(string entityType)
    {
        if (!Templates.TryGetValue(entityType, out var headers))
            throw ThrowHelper.Biz(nameof(DownloadTemplateAsync), $"未知导入类型: {entityType}");

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("母版");
        for (var i = 0; i < headers.Length; i++)
        {
            ws.Cell(1, i + 1).Value = headers[i];
            ws.Cell(1, i + 1).Style.Font.Bold = true;
        }

        // 第 2 行：格式占位示例（关联列可空；填了必须与本厂已有数据一致，不承诺照抄必成功）
        WriteSampleRow(ws, entityType);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return Task.FromResult(ms.ToArray());
    }

    private static void WriteSampleRow(IXLWorksheet ws, string entityType)
    {
        switch (entityType)
        {
            case "user":
                ws.Cell(2, 1).Value = "demo001";
                ws.Cell(2, 2).Value = "13800000000";
                ws.Cell(2, 3).Value = "示例用户";
                ws.Cell(2, 4).Value = "2";
                ws.Cell(2, 5).Value = "Admin123";
                ws.Cell(2, 6).GetComment().AddText("可选；填了必须是本厂已有部门编码，多个用逗号分隔");
                break;
            case "defect":
                ws.Cell(2, 1).Value = "示例不良品";
                break;
            case "operation":
                ws.Cell(2, 1).Value = "OP-DEMO-001";
                ws.Cell(2, 2).Value = "示例工序";
                // 关联列留空；批注说明须填本厂已有数据
                ws.Cell(2, 3).GetComment().AddText("可选；填了必须是本厂已有部门编码，多个用逗号分隔");
                ws.Cell(2, 4).GetComment().AddText("可选；填了必须是本厂已有不良品项名称，多个用逗号分隔");
                break;
            case "routing":
                ws.Cell(2, 1).Value = "R-DEMO-001";
                ws.Cell(2, 2).Value = "示例路线";
                ws.Cell(2, 3).GetComment().AddText("可选；填了必须是本厂已有工序编码，按顺序逗号分隔");
                break;
            case "product":
                ws.Cell(2, 1).Value = "P-DEMO-001";
                ws.Cell(2, 2).Value = "示例产品";
                ws.Cell(2, 3).GetComment().AddText("可选；填了必须是本厂已有单位名称");
                ws.Cell(2, 4).GetComment().AddText("可选；填了必须是本厂已有工艺路线编号");
                ws.Cell(2, 5).Value = "";
                ws.Cell(2, 6).Value = "1.5";
                break;
        }
    }

    public async Task<ApiResult<ImportResultDto>> ImportAsync(string entityType, byte[] fileBytes, long factoryId)
    {
        if (!Templates.TryGetValue(entityType, out _))
            throw ThrowHelper.Biz(nameof(ImportAsync), $"未知导入类型: {entityType}");

        List<(int ExcelRow, string[] Cells)> rows;
        try
        {
            using var ms = new MemoryStream(fileBytes);
            using var wb = new XLWorkbook(ms);
            var ws = wb.Worksheet(1);
            rows = new List<(int, string[])>();
            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
            var colCount = Templates[entityType].Length;
            for (var r = 2; r <= lastRow; r++)
            {
                var cells = new List<string>();
                for (var c = 1; c <= colCount; c++)
                    cells.Add(ws.Cell(r, c).GetString().Trim());
                if (cells.All(string.IsNullOrEmpty)) continue;
                rows.Add((r, cells.ToArray()));
            }
        }
        catch (Exception ex)
        {
            throw ThrowHelper.General(nameof(ImportAsync), "解析 Excel 失败", ex);
        }

        if (rows.Count == 0)
            throw ThrowHelper.Biz(nameof(ImportAsync), "没有可导入的数据行");

        var result = new ImportResultDto();
        // 本文件内已出现过的账号：后续同行部门取并集；库中已有账号首次出现则覆盖部门
        var userAccountsSeenInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (excelRow, cells) in rows)
        {
            try
            {
                if (entityType == "user")
                    await ImportUserAsync(cells, factoryId, userAccountsSeenInFile);
                else
                {
                    await using var tx = await _db.Database.BeginTransactionAsync();
                    await ImportRowAsync(entityType, cells, factoryId);
                    await tx.CommitAsync();
                }
                result.SuccessCount++;
            }
            catch (BusinessException ex)
            {
                _db.ChangeTracker.Clear();
                result.FailCount++;
                result.Errors.Add($"第{excelRow}行：{ex.Message}");
            }
        }
        return ApiResult<ImportResultDto>.Ok(result);
    }

    private async Task ImportRowAsync(string entityType, string[] row, long factoryId)
    {
        switch (entityType)
        {
            case "defect": await ImportDefectAsync(row, factoryId); break;
            case "operation": await ImportOperationAsync(row, factoryId); break;
            case "routing": await ImportRoutingAsync(row, factoryId); break;
            case "product": await ImportProductAsync(row, factoryId); break;
        }
    }

    private async Task ImportUserAsync(string[] r, long factoryId, HashSet<string> seenInFile)
    {
        var account = r[0];
        if (string.IsNullOrWhiteSpace(account)) throw ThrowHelper.Biz(nameof(ImportUserAsync), "账号为空");

        var deptCodes = Split(r.Length > 5 ? r[5] : null).ToList();
        var deptIds = new List<long>();
        foreach (var deptCode in deptCodes)
        {
            var dept = await _db.Departments.AsNoTracking()
                .FirstOrDefaultAsync(d => d.FactoryId == factoryId && d.Code == deptCode);
            if (dept == null)
                throw ThrowHelper.Biz(nameof(ImportUserAsync), $"部门编码{deptCode}不存在");
            deptIds.Add(dept.Id);
        }

        var userType = UserTypeHelper.ParseImport(r.Length > 6 ? r[6] : null);

        var existing = await _db.Users.FirstOrDefaultAsync(u => u.FactoryId == factoryId && u.Account == account);
        long userId;
        if (existing == null)
        {
            var role = r[3] switch
            {
                "2" => (byte)2,
                "3" => (byte)3,
                _ => (byte)1
            };
            var pwd = string.IsNullOrWhiteSpace(r[4]) ? "Admin123" : r[4];
            PasswordHelper.EnsureComplexity(pwd, nameof(ImportUserAsync));
            var user = new SysUser
            {
                FactoryId = factoryId,
                Account = account,
                Phone = r[1],
                Name = string.IsNullOrWhiteSpace(r[2]) ? account : r[2],
                Role = role,
                UserType = userType,
                Password = PasswordHelper.Hash(pwd),
                Status = 1,
                CreatedAt = DateTime.Now
            };
            _db.Users.Add(user);
            await _db.SaveChangesAsync();
            userId = user.Id;
            await ReplaceUserDeptLinksAsync(userId, deptIds, union: false);
        }
        else
        {
            // 库中已有：首次出现覆盖部门；本文件内再次出现则并集；人员类型按本行覆盖
            var union = seenInFile.Contains(account);
            existing.UserType = userType;
            userId = existing.Id;
            await ReplaceUserDeptLinksAsync(userId, deptIds, union);
        }

        seenInFile.Add(account);
        await _db.SaveChangesAsync();
    }

    private async Task ReplaceUserDeptLinksAsync(long userId, List<long> deptIds, bool union)
    {
        var want = deptIds.Distinct().ToList();
        if (union)
        {
            var have = await _db.DepartmentUsers
                .Where(x => x.UserId == userId).Select(x => x.DepartmentId).ToListAsync();
            foreach (var did in want.Where(id => !have.Contains(id)))
                _db.DepartmentUsers.Add(new SysDepartmentUser { DepartmentId = did, UserId = userId });
        }
        else
        {
            var old = _db.DepartmentUsers.Where(x => x.UserId == userId);
            _db.DepartmentUsers.RemoveRange(old);
            foreach (var did in want)
                _db.DepartmentUsers.Add(new SysDepartmentUser { DepartmentId = did, UserId = userId });
        }
    }

    private async Task ImportDefectAsync(string[] r, long factoryId)
    {
        var name = r[0];
        if (string.IsNullOrWhiteSpace(name)) throw ThrowHelper.Biz(nameof(ImportDefectAsync), "名称为空");
        if (await _db.DefectItems.AnyAsync(d => d.FactoryId == factoryId && d.Name == name))
            throw ThrowHelper.Biz(nameof(ImportDefectAsync), $"不良品项 {name} 已存在");
        _db.DefectItems.Add(new BaseDefectItem { FactoryId = factoryId, Name = name, CreatedAt = DateTime.Now });
        await _db.SaveChangesAsync();
    }

    private async Task ImportOperationAsync(string[] r, long factoryId)
    {
        var code = r[0];
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(r[1]))
            throw ThrowHelper.Biz(nameof(ImportOperationAsync), "编号或名称为空");
        if (await _db.Operations.AnyAsync(o => o.FactoryId == factoryId && o.Code == code))
            throw ThrowHelper.Biz(nameof(ImportOperationAsync), $"工序 {code} 已存在");

        // 先解析校验全部关联，通过后再写父表，避免失败行留下半截工序
        var depts = new List<SysDepartment>();
        foreach (var deptCode in Split(r[2]))
        {
            var dept = await _db.Departments.FirstOrDefaultAsync(d => d.FactoryId == factoryId && d.Code == deptCode);
            if (dept == null)
                throw ThrowHelper.Biz(nameof(ImportOperationAsync), $"报工部门编码不存在：{deptCode}");
            depts.Add(dept);
        }
        var defects = new List<BaseDefectItem>();
        foreach (var defectName in Split(r[3]))
        {
            var d = await _db.DefectItems.FirstOrDefaultAsync(x => x.FactoryId == factoryId && x.Name == defectName);
            if (d == null)
                throw ThrowHelper.Biz(nameof(ImportOperationAsync), $"不良品项名称不存在：{defectName}");
            defects.Add(d);
        }

        var op = new BaseOperation { FactoryId = factoryId, Code = code, Name = r[1], CreatedAt = DateTime.Now };
        _db.Operations.Add(op);
        await _db.SaveChangesAsync();

        foreach (var dept in depts)
            _db.OperationDepartments.Add(new BaseOperationDepartment { OperationId = op.Id, DepartmentId = dept.Id });
        foreach (var d in defects)
            _db.OperationDefects.Add(new BaseOperationDefect { OperationId = op.Id, DefectId = d.Id });
        if (depts.Count > 0 || defects.Count > 0)
            await _db.SaveChangesAsync();
    }

    private async Task ImportRoutingAsync(string[] r, long factoryId)
    {
        var code = r[0];
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(r[1]))
            throw ThrowHelper.Biz(nameof(ImportRoutingAsync), "编号或名称为空");
        if (await _db.Routings.AnyAsync(x => x.FactoryId == factoryId && x.Code == code))
            throw ThrowHelper.Biz(nameof(ImportRoutingAsync), $"路线 {code} 已存在");

        var ops = new List<BaseOperation>();
        foreach (var opCode in Split(r[2]))
        {
            var op = await _db.Operations.FirstOrDefaultAsync(o => o.FactoryId == factoryId && o.Code == opCode);
            if (op == null)
                throw ThrowHelper.Biz(nameof(ImportRoutingAsync), $"工序编码不存在：{opCode}");
            ops.Add(op);
        }

        var routing = new BaseRouting { FactoryId = factoryId, Code = code, Name = r[1], CreatedAt = DateTime.Now };
        _db.Routings.Add(routing);
        await _db.SaveChangesAsync();

        var seq = 1;
        foreach (var op in ops)
            _db.RoutingSteps.Add(new BaseRoutingStep { RoutingId = routing.Id, OperationId = op.Id, Seq = seq++ });
        if (ops.Count > 0)
            await _db.SaveChangesAsync();
    }

    private async Task ImportProductAsync(string[] r, long factoryId)
    {
        var code = r[0];
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(r[1]))
            throw ThrowHelper.Biz(nameof(ImportProductAsync), "编号或名称为空");
        if (await _db.Products.AnyAsync(p => p.FactoryId == factoryId && p.Code == code))
            throw ThrowHelper.Biz(nameof(ImportProductAsync), $"产品 {code} 已存在（编号唯一）");

        long? unitId = null;
        if (!string.IsNullOrWhiteSpace(r[2]))
        {
            var unit = await _db.Units.FirstOrDefaultAsync(u => u.FactoryId == factoryId && u.Name == r[2]);
            if (unit == null)
                throw ThrowHelper.Biz(nameof(ImportProductAsync), $"单位名称不存在：{r[2]}");
            unitId = unit.Id;
        }
        long? routingId = null;
        if (!string.IsNullOrWhiteSpace(r[3]))
        {
            var routing = await _db.Routings.FirstOrDefaultAsync(x => x.FactoryId == factoryId && x.Code == r[3]);
            if (routing == null)
                throw ThrowHelper.Biz(nameof(ImportProductAsync), $"工艺路线编号不存在：{r[3]}");
            routingId = routing.Id;
        }

        _db.Products.Add(new BaseProduct
        {
            FactoryId = factoryId,
            Code = code,
            Name = r[1],
            UnitId = unitId,
            RoutingId = routingId,
            Supplier = string.IsNullOrWhiteSpace(r[4]) ? null : r[4],
            Price = decimal.TryParse(r[5], out var p) ? p : null,
            CreatedAt = DateTime.Now
        });
        await _db.SaveChangesAsync();
    }

    private static IEnumerable<string> Split(string? s) =>
        (s ?? "").Split(new[] { ',', '，', '、' }, StringSplitOptions.RemoveEmptyEntries)
                 .Select(x => x.Trim()).Where(x => x.Length > 0);
}
