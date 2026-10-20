using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddImportCostAdvances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ImportCostAdvanceId",
                table: "LandedCostVoucherLine",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImportCostAdvanceNumber",
                table: "LandedCostVoucherLine",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "SupplierId",
                table: "LandedCostVoucher",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<string>(
                name: "ImportCostsClearingAccount",
                table: "CarShowroomSettings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "1144");

            migrationBuilder.CreateTable(
                name: "ImportCostAdvance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProcurementOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProcurementOrderNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExpenseTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpenseTypeCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExpenseTypeNameAr = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SupplierId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SupplierName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SupplierInvoiceNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CurrencyCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExchangeRate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    VatAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CostAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    InputVatAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PaymentMethodCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PaymentAccountCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SettledAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    SettledCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    JournalEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportCostAdvance", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportCostAdvance_CarProcurementOrder_ProcurementOrderId",
                        column: x => x.ProcurementOrderId,
                        principalTable: "CarProcurementOrder",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ImportCostAdvance_ExpenseType_ExpenseTypeId",
                        column: x => x.ExpenseTypeId,
                        principalTable: "ExpenseType",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ImportCostAdvance_Supplier_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Supplier",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LandedCostVoucherLine_ImportCostAdvanceId",
                table: "LandedCostVoucherLine",
                column: "ImportCostAdvanceId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportCostAdvance_ExpenseTypeId",
                table: "ImportCostAdvance",
                column: "ExpenseTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportCostAdvance_ProcurementOrderId",
                table: "ImportCostAdvance",
                column: "ProcurementOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportCostAdvance_SupplierId",
                table: "ImportCostAdvance",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportCostAdvance_TenantId",
                table: "ImportCostAdvance",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportCostAdvance_TenantId_Number",
                table: "ImportCostAdvance",
                columns: new[] { "TenantId", "Number" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_LandedCostVoucherLine_ImportCostAdvance_ImportCostAdvanceId",
                table: "LandedCostVoucherLine",
                column: "ImportCostAdvanceId",
                principalTable: "ImportCostAdvance",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LandedCostVoucherLine_ImportCostAdvance_ImportCostAdvanceId",
                table: "LandedCostVoucherLine");

            migrationBuilder.DropTable(
                name: "ImportCostAdvance");

            migrationBuilder.DropIndex(
                name: "IX_LandedCostVoucherLine_ImportCostAdvanceId",
                table: "LandedCostVoucherLine");

            migrationBuilder.DropColumn(
                name: "ImportCostAdvanceId",
                table: "LandedCostVoucherLine");

            migrationBuilder.DropColumn(
                name: "ImportCostAdvanceNumber",
                table: "LandedCostVoucherLine");

            migrationBuilder.DropColumn(
                name: "ImportCostsClearingAccount",
                table: "CarShowroomSettings");

            migrationBuilder.AlterColumn<Guid>(
                name: "SupplierId",
                table: "LandedCostVoucher",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);
        }
    }
}
