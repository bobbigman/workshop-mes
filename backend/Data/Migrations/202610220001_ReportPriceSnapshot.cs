using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ahu.MicrosoftMes.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("202610220001_ReportPriceSnapshot")]
public class ReportPriceSnapshot : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(ReportPriceSnapshotSchema.UpgradeSql);
    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("工价快照用于历史工资追溯，不自动删除。");
}
