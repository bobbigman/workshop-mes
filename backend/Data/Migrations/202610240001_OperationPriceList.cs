using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ahu.MicrosoftMes.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("202610240001_OperationPriceList")]
public class OperationPriceListMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(OperationPriceListSchema.UpgradeSql);
    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("工序价目表包含历史价格来源，不自动删表。");
}
