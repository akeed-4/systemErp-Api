using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class InventoryVoucherReasons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CounterAccountCode",
                table: "InventoryVoucherLine",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reason",
                table: "InventoryVoucher",
                type: "nvarchar(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "InventoryOpeningReceiptAccountCode",
                table: "AccountingSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InventoryProjectIssueAccountCode",
                table: "AccountingSettings",
                type: "nvarchar(max)",
                nullable: true);

            // المستندات القائمة: السبب الافتراضي لنوعها (كانت على الحساب الافتراضي للنوع)، والسطر المرحّل يحفظ حساب قيد مستنده كما هو.
            // المسودة تُفرَّغ من الحساب فيُحدَّد من الإعدادات عند ترحيلها؛ القيود المرحّلة لا تُمس.
            migrationBuilder.Sql("UPDATE [InventoryVoucher] SET [Reason] = CASE WHEN [Kind] = 'receipt' THEN 'other_receipt' ELSE 'internal_use' END WHERE [Reason] = '';");
            migrationBuilder.Sql(@"UPDATE l SET l.[CounterAccountCode] = v.[CounterAccountCode]
FROM [InventoryVoucherLine] l INNER JOIN [InventoryVoucher] v ON v.[Id] = l.[VoucherId]
WHERE v.[Status] <> 'draft' AND l.[CounterAccountCode] IS NULL;");
            migrationBuilder.Sql("UPDATE [InventoryVoucher] SET [CounterAccountCode] = '' WHERE [Status] = 'draft';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CounterAccountCode",
                table: "InventoryVoucherLine");

            migrationBuilder.DropColumn(
                name: "Reason",
                table: "InventoryVoucher");

            migrationBuilder.DropColumn(
                name: "InventoryOpeningReceiptAccountCode",
                table: "AccountingSettings");

            migrationBuilder.DropColumn(
                name: "InventoryProjectIssueAccountCode",
                table: "AccountingSettings");
        }
    }
}
