using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ahu.MicrosoftMes.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("202610230001_ProcessingPrice")]
public class ProcessingPrice : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(ProcessingPriceSchema.UpgradeSql);
    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("加工计价包含工单历史依据，不自动删除。");
}
