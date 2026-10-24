using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace ahu.MicrosoftMes.Data.Migrations;
[DbContext(typeof(AppDbContext))]
[Migration("202610240002_OperationPriceTableScope")]
public class OperationPriceTableScopeMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(OperationPriceTableScopeSchema.AddColumnsSql);
        migrationBuilder.Sql(OperationPriceTableScopeSchema.FinalizeSql);
    }
    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("保留产品范围及历史价目表依据，禁止自动删列。");
}
