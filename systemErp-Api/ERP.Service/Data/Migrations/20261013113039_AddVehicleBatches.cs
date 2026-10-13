using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVehicleBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsTemporaryChassis",
                table: "Vehicle",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "BatchId",
                table: "InvoiceVehicleLine",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsTemporaryChassis",
                table: "InvoiceVehicleLine",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "InvoiceVehicleLine",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PriceOverride",
                table: "InvoiceVehicleLine",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InvoiceVehicleBatch",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModelId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TrimId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BrandNameAr = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ModelNameAr = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TrimNameAr = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Year = table.Column<int>(type: "int", nullable: false),
                    ColorExterior = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ColorInterior = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    VatMode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Discount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    TotalBeforeVat = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    VatAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalAfterVat = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceVehicleBatch", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceVehicleBatch_Invoice_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoice",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Vehicle_TenantId_PurchaseInvoiceId",
                table: "Vehicle",
                columns: new[] { "TenantId", "PurchaseInvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceVehicleLine_BatchId",
                table: "InvoiceVehicleLine",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceVehicleBatch_InvoiceId",
                table: "InvoiceVehicleBatch",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceVehicleBatch_TenantId",
                table: "InvoiceVehicleBatch",
                column: "TenantId");

            // الأرقام المؤقتة المولَّدة سابقاً (TMP + 14 رقماً) تأخذ العلامة
            migrationBuilder.Sql("UPDATE [Vehicle] SET [IsTemporaryChassis] = 1 WHERE [ChassisNumber] LIKE 'TMP' + REPLICATE('[0-9]', 14)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvoiceVehicleBatch");

            migrationBuilder.DropIndex(
                name: "IX_Vehicle_TenantId_PurchaseInvoiceId",
                table: "Vehicle");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceVehicleLine_BatchId",
                table: "InvoiceVehicleLine");

            migrationBuilder.DropColumn(
                name: "IsTemporaryChassis",
                table: "Vehicle");

            migrationBuilder.DropColumn(
                name: "BatchId",
                table: "InvoiceVehicleLine");

            migrationBuilder.DropColumn(
                name: "IsTemporaryChassis",
                table: "InvoiceVehicleLine");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "InvoiceVehicleLine");

            migrationBuilder.DropColumn(
                name: "PriceOverride",
                table: "InvoiceVehicleLine");
        }
    }
}
