using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class ContractRequiresGuarantee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequiresGuarantee",
                table: "CommercialContract",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // العقود القائمة تحتفظ بما كان يُفرض عليها: ضمان إن كان نوعها يلزمه وكانت على المسار الكامل
            migrationBuilder.Sql(@"
UPDATE c SET [RequiresGuarantee] = 1
FROM [CommercialContract] c JOIN [ContractTypeDefinition] d ON d.[TenantId] = c.[TenantId] AND d.[Code] = c.[ContractType]
WHERE d.[RequiresGuarantee] = 1 AND c.[IsSimplified] = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequiresGuarantee",
                table: "CommercialContract");
        }
    }
}
