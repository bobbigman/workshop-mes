using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ahu.MicrosoftMes.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("202610250001_PriceChanges")]
public class PriceChangesMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(PriceChangeSchema.UpgradeSql);
    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("调价历史和资金审计不可自动删除");
}
