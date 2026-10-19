using System.Reflection;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Services;

var count = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); count++; }
var period = typeof(SalaryService).GetMethod("MyStatementPeriodEnd", BindingFlags.NonPublic | BindingFlags.Static)!;
DateTime End(byte type, string value) => (DateTime)period.Invoke(null, new object[] { type, value })!;
Check(End(1, "2024-02") == new DateTime(2024, 3, 1), "闰月边界");
Check(End(2, "2020-W53") == new DateTime(2021, 1, 4), "ISO跨年周");
Check(End(1, "2026-10") > End(2, "2026-W42"), "月周按结束日期排序");
foreach (var (type, value) in new (byte, string)[] { (0,"2026-10"), (1,"2026-13"), (2,"2026-W00"), (2,"2025-W53"), (2,"2026-W54"), (2,"2026-W1"), (2,"0000-W01") })
{
    try { End(type,value); throw new Exception("非法周期未拒绝: " + value); }
    catch (TargetInvocationException ex) when (ex.InnerException is BusinessException b && b.Code == 400) { count++; }
}
foreach (var roleMethod in new[] { "IsAllowedForWorker", "IsAllowedForLeader" })
{
    var method = typeof(RoleGuardMiddleware).GetMethod(roleMethod, BindingFlags.NonPublic | BindingFlags.Static)!;
    bool Allowed(string path, string verb) => (bool)method.Invoke(null, new object[] { path, verb })!;
    Check(Allowed("/api/Salary/my-statements", "GET"), "本人工资列表放行");
    Check(Allowed("/api/salary/my-statements/123/items", "GET"), "本人明细放行");
    Check(!Allowed("/api/Salary/my-statements/123/items", "POST"), "写操作拒绝");
    Check(!Allowed("/api/Salary/123", "GET"), "管理明细仍拒绝");
    Check(!Allowed("/api/Salary/my-statements/123/456/items", "GET"), "多段路径拒绝");
}
Console.WriteLine($"PASS {count} checks");
