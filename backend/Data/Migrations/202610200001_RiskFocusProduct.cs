using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ahu.MicrosoftMes.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("202610200001_RiskFocusProduct")]
public class RiskFocusProduct : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(RiskBriefSchema.UpgradeSql);

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("重点产品配置含用户偏好，不自动删除；回退应用时保留兼容列。");
}
