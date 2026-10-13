using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExpenseTypesAndChargeLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExpenseType",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Stage = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DefaultTreatment = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DefaultAllocationMethod = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    VatCategory = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsVatRecoverable = table.Column<bool>(type: "bit", nullable: false),
                    IsInputTax = table.Column<bool>(type: "bit", nullable: false),
                    DefaultAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    ShowToCustomer = table.Column<bool>(type: "bit", nullable: false),
                    AccountCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsSystem = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpenseType", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdditionalChargeLine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DocumentNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExpenseTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpenseTypeCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExpenseTypeNameAr = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Stage = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CurrencyCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExchangeRate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    BaseAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    VatCategory = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    VatAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    IsVatRecoverable = table.Column<bool>(type: "bit", nullable: false),
                    IsInputTax = table.Column<bool>(type: "bit", nullable: false),
                    CostAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Treatment = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AllocationMethod = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SupplierName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SupplierInvoiceNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TammOperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReplacesLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    JournalEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdditionalChargeLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdditionalChargeLine_ExpenseType_ExpenseTypeId",
                        column: x => x.ExpenseTypeId,
                        principalTable: "ExpenseType",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AdditionalChargeLine_Supplier_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Supplier",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AdditionalChargeAllocation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdditionalChargeAllocation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdditionalChargeAllocation_AdditionalChargeLine_LineId",
                        column: x => x.LineId,
                        principalTable: "AdditionalChargeLine",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AdditionalChargeAllocation_Vehicle_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicle",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdditionalChargeAllocation_LineId",
                table: "AdditionalChargeAllocation",
                column: "LineId");

            migrationBuilder.CreateIndex(
                name: "IX_AdditionalChargeAllocation_TenantId",
                table: "AdditionalChargeAllocation",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AdditionalChargeAllocation_TenantId_VehicleId",
                table: "AdditionalChargeAllocation",
                columns: new[] { "TenantId", "VehicleId" });

            migrationBuilder.CreateIndex(
                name: "IX_AdditionalChargeAllocation_VehicleId",
                table: "AdditionalChargeAllocation",
                column: "VehicleId");

            migrationBuilder.CreateIndex(
                name: "IX_AdditionalChargeLine_ExpenseTypeId",
                table: "AdditionalChargeLine",
                column: "ExpenseTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_AdditionalChargeLine_SupplierId",
                table: "AdditionalChargeLine",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_AdditionalChargeLine_TenantId",
                table: "AdditionalChargeLine",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AdditionalChargeLine_TenantId_DocumentType_DocumentId",
                table: "AdditionalChargeLine",
                columns: new[] { "TenantId", "DocumentType", "DocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_AdditionalChargeLine_TenantId_TammOperationId",
                table: "AdditionalChargeLine",
                columns: new[] { "TenantId", "TammOperationId" },
                unique: true,
                filter: "[TammOperationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseType_TenantId",
                table: "ExpenseType",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseType_TenantId_Code",
                table: "ExpenseType",
                columns: new[] { "TenantId", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdditionalChargeAllocation");

            migrationBuilder.DropTable(
                name: "AdditionalChargeLine");

            migrationBuilder.DropTable(
                name: "ExpenseType");
        }
    }
}
