using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddContractBoq : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LineCode",
                table: "CommercialContractItem",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LineKind",
                table: "CommercialContractItem",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "measured");

            migrationBuilder.AddColumn<decimal>(
                name: "OverrunPercent",
                table: "CommercialContractItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "RevisedQuantity",
                table: "CommercialContractItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "SectionName",
                table: "CommercialContractItem",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "CommercialContractItem",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ConsultantName",
                table: "CommercialContract",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProjectLocation",
                table: "CommercialContract",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProjectName",
                table: "CommercialContract",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            // البنود القائمة: الكمية المعتمدة = الكمية، والترتيب بتاريخ الإنشاء داخل كل عقد
            migrationBuilder.Sql(@"
UPDATE i SET [RevisedQuantity] = i.[Quantity], [SortOrder] = o.[Rn]
FROM [CommercialContractItem] i
JOIN (SELECT [Id], ROW_NUMBER() OVER (PARTITION BY [ContractId] ORDER BY [CreatedAt], [Id]) AS [Rn] FROM [CommercialContractItem]) o ON o.[Id] = i.[Id];");

            // نوع «مقاولات بجدول كميات» لكل منشأة قائمة (المنشآت الجديدة تُزرع من ContractTypeSeed عند التسجيل)
            migrationBuilder.Sql(@"
INSERT INTO [ContractTypeDefinition] ([Id], [TenantId], [CreatedAt], [Code], [NameAr], [NameEn], [Workflow], [Billing], [SimpleLimit], [RequiresGuarantee], [IsActive], [SortOrder])
SELECT NEWID(), t.[TenantId], SYSUTCDATETIME(), 'construction_boq', N'مقاولات بجدول كميات', N'Construction (BOQ)', 'full', 'boq_certificate', NULL, 1, 1, 5
FROM (SELECT [Id] AS [TenantId] FROM [Tenant] UNION SELECT DISTINCT [TenantId] FROM [ContractTypeDefinition]) t
WHERE NOT EXISTS (SELECT 1 FROM [ContractTypeDefinition] x WHERE x.[TenantId] = t.[TenantId] AND x.[Code] = 'construction_boq');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DELETE d FROM [ContractTypeDefinition] d
WHERE d.[Code] = 'construction_boq'
  AND NOT EXISTS (SELECT 1 FROM [CommercialContract] c WHERE c.[TenantId] = d.[TenantId] AND c.[ContractType] = d.[Code]);");

            migrationBuilder.DropColumn(
                name: "LineCode",
                table: "CommercialContractItem");

            migrationBuilder.DropColumn(
                name: "LineKind",
                table: "CommercialContractItem");

            migrationBuilder.DropColumn(
                name: "OverrunPercent",
                table: "CommercialContractItem");

            migrationBuilder.DropColumn(
                name: "RevisedQuantity",
                table: "CommercialContractItem");

            migrationBuilder.DropColumn(
                name: "SectionName",
                table: "CommercialContractItem");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "CommercialContractItem");

            migrationBuilder.DropColumn(
                name: "ConsultantName",
                table: "CommercialContract");

            migrationBuilder.DropColumn(
                name: "ProjectLocation",
                table: "CommercialContract");

            migrationBuilder.DropColumn(
                name: "ProjectName",
                table: "CommercialContract");
        }
    }
}
