using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class PurchaseExpenseCapitalizationAndSiteAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CapitalizeToItemCost",
                table: "PurchaseLandedCostLine",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "ExpenseAccountCode",
                table: "PurchaseLandedCostLine",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ExpenseAmount",
                table: "PurchaseLandedCostLine",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "CapitalizeToItemCost",
                table: "PurchaseExpenseType",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "ExpenseAccountCode",
                table: "PurchaseExpenseType",
                type: "nvarchar(max)",
                nullable: true);

            // قرار المنشأة: الشحن والنقل الداخلي والتأمين (الأنواع المزروعة) لا تدخل تكلفة الأصناف في السندات الجديدة، والسندات المعتمدة
            // تبقى كما رُحِّلت (أسطرها القائمة داخلة في التكلفة). الأنواع التي أنشأها المستخدم تبقى داخلة حتى يغيّرها.
            migrationBuilder.Sql("UPDATE [PurchaseExpenseType] SET [CapitalizeToItemCost] = 0 WHERE [IsSystem] = 1 AND [Code] IN ('FREIGHT', 'INLAND_TRANSPORT', 'INSURANCE');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CapitalizeToItemCost",
                table: "PurchaseLandedCostLine");

            migrationBuilder.DropColumn(
                name: "ExpenseAccountCode",
                table: "PurchaseLandedCostLine");

            migrationBuilder.DropColumn(
                name: "ExpenseAmount",
                table: "PurchaseLandedCostLine");

            migrationBuilder.DropColumn(
                name: "CapitalizeToItemCost",
                table: "PurchaseExpenseType");

            migrationBuilder.DropColumn(
                name: "ExpenseAccountCode",
                table: "PurchaseExpenseType");
        }
    }
}
