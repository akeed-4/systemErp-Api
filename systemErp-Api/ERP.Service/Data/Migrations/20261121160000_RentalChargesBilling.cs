using System;
using ERP.Service.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <summary>
    /// تأجير المعدات R3a: إعداد التأجير، وأنواع الرسوم، والرسوم، والفوترة الدورية وأسطرها، وعمود المفوتر تراكمياً على سطر العقد (جدول الوحدة).
    /// </summary>
    [DbContext(typeof(ErpDbContext))]
    [Migration("20261121160000_RentalChargesBilling")]
    public partial class RentalChargesBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(name: "BilledAmount", table: "RentalContractLine", type: "decimal(18,4)", precision: 18, scale: 4, nullable: false, defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "RentalSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LateFeeFactor = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    LateGraceHours = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    RequireDepositBeforeDelivery = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table => table.PrimaryKey("PK_RentalSettings", x => x.Id));

            migrationBuilder.CreateTable(
                name: "RentalChargeType",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    RevenueAccountCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    DefaultTaxTreatment = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    RequiresClassification = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table => table.PrimaryKey("PK_RentalChargeType", x => x.Id));

            migrationBuilder.CreateTable(
                name: "RentalBilling",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BillingNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BilledUntil = table.Column<DateTime>(type: "datetime2", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    RentAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ChargesAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    NetAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    VatAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CancelReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RentalBilling", x => x.Id);
                    table.ForeignKey(name: "FK_RentalBilling_RentalContract_ContractId", column: x => x.ContractId, principalTable: "RentalContract",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RentalBillingLine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BillingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PeriodFrom = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PeriodTo = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PreviousBilledThrough = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PreviousBilledAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RentalBillingLine", x => x.Id);
                    table.ForeignKey(name: "FK_RentalBillingLine_RentalBilling_BillingId", column: x => x.BillingId, principalTable: "RentalBilling",
                        principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(name: "FK_RentalBillingLine_RentalContractLine_ContractLineId", column: x => x.ContractLineId, principalTable: "RentalContractLine",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RentalCharge",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChargeTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TaxTreatment = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    SourceType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    BillingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WaiveReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    WaivedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ApprovedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RentalCharge", x => x.Id);
                    table.ForeignKey(name: "FK_RentalCharge_RentalBilling_BillingId", column: x => x.BillingId, principalTable: "RentalBilling",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_RentalCharge_RentalChargeType_ChargeTypeId", column: x => x.ChargeTypeId, principalTable: "RentalChargeType",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_RentalCharge_RentalContractLine_LineId", column: x => x.LineId, principalTable: "RentalContractLine",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_RentalCharge_RentalContract_ContractId", column: x => x.ContractId, principalTable: "RentalContract",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(name: "IX_RentalSettings_TenantId", table: "RentalSettings", column: "TenantId");
            migrationBuilder.CreateIndex(name: "IX_RentalChargeType_TenantId", table: "RentalChargeType", column: "TenantId");
            migrationBuilder.CreateIndex(name: "IX_RentalChargeType_TenantId_Code", table: "RentalChargeType", columns: new[] { "TenantId", "Code" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_RentalBilling_ContractId", table: "RentalBilling", column: "ContractId");
            migrationBuilder.CreateIndex(name: "IX_RentalBilling_TenantId", table: "RentalBilling", column: "TenantId");
            migrationBuilder.CreateIndex(name: "IX_RentalBilling_TenantId_BillingNumber", table: "RentalBilling", columns: new[] { "TenantId", "BillingNumber" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_RentalBilling_TenantId_ContractId", table: "RentalBilling", columns: new[] { "TenantId", "ContractId" });
            migrationBuilder.CreateIndex(name: "IX_RentalBillingLine_BillingId", table: "RentalBillingLine", column: "BillingId");
            migrationBuilder.CreateIndex(name: "IX_RentalBillingLine_ContractLineId", table: "RentalBillingLine", column: "ContractLineId");
            migrationBuilder.CreateIndex(name: "IX_RentalBillingLine_TenantId", table: "RentalBillingLine", column: "TenantId");
            migrationBuilder.CreateIndex(name: "IX_RentalCharge_BillingId", table: "RentalCharge", column: "BillingId");
            migrationBuilder.CreateIndex(name: "IX_RentalCharge_ChargeTypeId", table: "RentalCharge", column: "ChargeTypeId");
            migrationBuilder.CreateIndex(name: "IX_RentalCharge_ContractId", table: "RentalCharge", column: "ContractId");
            migrationBuilder.CreateIndex(name: "IX_RentalCharge_LineId", table: "RentalCharge", column: "LineId");
            migrationBuilder.CreateIndex(name: "IX_RentalCharge_TenantId", table: "RentalCharge", column: "TenantId");
            migrationBuilder.CreateIndex(name: "IX_RentalCharge_TenantId_ContractId_Status", table: "RentalCharge", columns: new[] { "TenantId", "ContractId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "RentalCharge");
            migrationBuilder.DropTable(name: "RentalBillingLine");
            migrationBuilder.DropTable(name: "RentalBilling");
            migrationBuilder.DropTable(name: "RentalChargeType");
            migrationBuilder.DropTable(name: "RentalSettings");
            migrationBuilder.DropColumn(name: "BilledAmount", table: "RentalContractLine");
        }
    }
}
