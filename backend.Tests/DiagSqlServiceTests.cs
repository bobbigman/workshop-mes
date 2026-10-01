using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Services;
using Xunit;

namespace WorkshopMes.Tests;

/// <summary>docs/59：只读诊断 SQL 语句门禁。</summary>
public class DiagSqlServiceTests
{
    [Theory]
    [InlineData("SELECT 1")]
    [InlineData("with x as (select 1 as n) select * from x")]
    [InlineData("SELECT Id FROM prod_work_order WHERE OrderNo = 'a;b'")]
    public void EnsureSelectOnly_allows_read(string sql)
    {
        DiagSqlService.EnsureSelectOnly(DiagSqlService.NormalizeForCheck(sql));
    }

    [Theory]
    [InlineData("UPDATE prod_work_order SET Status=1")]
    [InlineData("DELETE FROM prod_work_order")]
    [InlineData("SELECT * INTO #t FROM prod_work_order")]
    [InlineData("SELECT 1; DROP TABLE prod_work_order")]
    [InlineData("EXEC sp_help")]
    [InlineData("INSERT INTO t VALUES (1)")]
    public void EnsureSelectOnly_rejects_write(string sql)
    {
        var n = DiagSqlService.NormalizeForCheck(sql);
        Assert.Throws<BusinessException>(() => DiagSqlService.EnsureSelectOnly(n));
    }
}
