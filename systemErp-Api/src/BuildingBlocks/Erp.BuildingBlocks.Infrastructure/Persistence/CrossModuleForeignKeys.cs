using Microsoft.EntityFrameworkCore.Migrations;

namespace Erp.BuildingBlocks.Infrastructure.Persistence;

/// <summary>
/// Foreign keys to another module's table (§10.1). They are added by hand in the dependent module's migration because the
/// principal is not part of that module's EF model; the principal module is always migrated first (§17.2 order).
/// </summary>
public static class CrossModuleForeignKeys
{
    public static void AddCrossModuleForeignKey(
        this MigrationBuilder migrationBuilder,
        string schema,
        string table,
        string column,
        string principalSchema,
        string principalTable,
        string principalColumn = "Id",
        ReferentialAction onDelete = ReferentialAction.Restrict) =>
        migrationBuilder.AddForeignKey(
            name: $"FK_{table}_{principalSchema}_{principalTable}_{column}",
            schema: schema,
            table: table,
            column: column,
            principalSchema: principalSchema,
            principalTable: principalTable,
            principalColumn: principalColumn,
            onDelete: onDelete);
}
