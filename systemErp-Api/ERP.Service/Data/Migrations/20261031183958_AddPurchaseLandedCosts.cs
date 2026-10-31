using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseLandedCosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "WeightKg",
                table: "Product",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PurchaseExpenseType",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    VatCategory = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsVatRecoverable = table.Column<bool>(type: "bit", nullable: false),
                    IsInputTax = table.Column<bool>(type: "bit", nullable: false),
                    DefaultAllocationMethod = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsSystem = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseExpenseType", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseLandedCost",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VoucherNumber = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Payee = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SupplierName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PaymentAccountCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SupplierInvoiceNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CurrencyCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExchangeRate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Subtotal = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    VatTotal = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    GrandTotal = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    BaseCostTotal = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    BaseInputVat = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    InventoryAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ExpensedAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ExpensedAccountCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    JournalEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CancelJournalEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseLandedCost", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseLandedCost_Supplier_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Supplier",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseLandedCostInvoice",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VoucherId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseLandedCostInvoice", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseLandedCostInvoice_PurchaseLandedCost_VoucherId",
                        column: x => x.VoucherId,
                        principalTable: "PurchaseLandedCost",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseLandedCostLine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VoucherId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    ExpenseTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpenseTypeCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExpenseTypeNameAr = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    VatAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    BaseAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    BaseVatAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CostAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    InputVatAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    AllocationMethod = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseLandedCostLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseLandedCostLine_PurchaseExpenseType_ExpenseTypeId",
                        column: x => x.ExpenseTypeId,
                        principalTable: "PurchaseExpenseType",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseLandedCostLine_PurchaseLandedCost_VoucherId",
                        column: x => x.VoucherId,
                        principalTable: "PurchaseLandedCost",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseLandedCostAllocation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    InvoiceItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NetQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitCostIncrease = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    StockMovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseLandedCostAllocation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseLandedCostAllocation_Product_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Product",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseLandedCostAllocation_PurchaseLandedCostLine_LineId",
                        column: x => x.LineId,
                        principalTable: "PurchaseLandedCostLine",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseExpenseType_TenantId",
                table: "PurchaseExpenseType",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseExpenseType_TenantId_Code",
                table: "PurchaseExpenseType",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseLandedCost_SupplierId",
                table: "PurchaseLandedCost",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseLandedCost_TenantId",
                table: "PurchaseLandedCost",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseLandedCost_TenantId_VoucherNumber",
                table: "PurchaseLandedCost",
                columns: new[] { "TenantId", "VoucherNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseLandedCostAllocation_ItemId",
                table: "PurchaseLandedCostAllocation",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseLandedCostAllocation_LineId",
                table: "PurchaseLandedCostAllocation",
                column: "LineId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseLandedCostAllocation_TenantId",
                table: "PurchaseLandedCostAllocation",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseLandedCostInvoice_TenantId",
                table: "PurchaseLandedCostInvoice",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseLandedCostInvoice_VoucherId",
                table: "PurchaseLandedCostInvoice",
                column: "VoucherId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseLandedCostLine_ExpenseTypeId",
                table: "PurchaseLandedCostLine",
                column: "ExpenseTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseLandedCostLine_TenantId",
                table: "PurchaseLandedCostLine",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseLandedCostLine_VoucherId",
                table: "PurchaseLandedCostLine",
                column: "VoucherId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PurchaseLandedCostAllocation");

            migrationBuilder.DropTable(
                name: "PurchaseLandedCostInvoice");

            migrationBuilder.DropTable(
                name: "PurchaseLandedCostLine");

            migrationBuilder.DropTable(
                name: "PurchaseExpenseType");

            migrationBuilder.DropTable(
                name: "PurchaseLandedCost");

            migrationBuilder.DropColumn(
                name: "WeightKg",
                table: "Product");
        }
    }
}
