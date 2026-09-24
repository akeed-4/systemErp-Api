using System;
using Erp.BuildingBlocks.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Payments.Persistence.Migrations
{
    /// <inheritdoc />
    internal partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "payments");

            migrationBuilder.CreateTable(
                name: "PaymentMethods",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Type = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    Channel = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BankAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Icon = table.Column<string>(type: "varchar(60)", unicode: false, maxLength: 60, nullable: true),
                    CommissionPercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    RequiresReference = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentMethods", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Vouchers",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VoucherNumber = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: true),
                    Type = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    AmountInWordsAr = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    PartyType = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    PartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PartyName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PartyAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReferenceNumber = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReceivedOrPaidBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CostCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    JournalEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vouchers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VoucherAllocations",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VoucherId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetModule = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    TargetDocumentType = table.Column<string>(type: "varchar(60)", unicode: false, maxLength: 60, nullable: false),
                    TargetDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetDocumentNumber = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VoucherAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VoucherAllocations_Vouchers_VoucherId",
                        column: x => x.VoucherId,
                        principalSchema: "payments",
                        principalTable: "Vouchers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VoucherPayments",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VoucherId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    PaymentMethodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BankAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TreasuryAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VoucherPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VoucherPayments_PaymentMethods_PaymentMethodId",
                        column: x => x.PaymentMethodId,
                        principalSchema: "payments",
                        principalTable: "PaymentMethods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VoucherPayments_Vouchers_VoucherId",
                        column: x => x.VoucherId,
                        principalSchema: "payments",
                        principalTable: "Vouchers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentMethods_TenantId_Code",
                schema: "payments",
                table: "PaymentMethods",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VoucherAllocations_TenantId_TargetModule_TargetDocumentType_TargetDocumentId",
                schema: "payments",
                table: "VoucherAllocations",
                columns: new[] { "TenantId", "TargetModule", "TargetDocumentType", "TargetDocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_VoucherAllocations_TenantId_VoucherId",
                schema: "payments",
                table: "VoucherAllocations",
                columns: new[] { "TenantId", "VoucherId" });

            migrationBuilder.CreateIndex(
                name: "IX_VoucherPayments_TenantId_PaymentMethodId",
                schema: "payments",
                table: "VoucherPayments",
                columns: new[] { "TenantId", "PaymentMethodId" });

            migrationBuilder.CreateIndex(
                name: "IX_VoucherPayments_TenantId_VoucherId",
                schema: "payments",
                table: "VoucherPayments",
                columns: new[] { "TenantId", "VoucherId" });

            migrationBuilder.CreateIndex(
                name: "IX_Vouchers_TenantId_Date",
                schema: "payments",
                table: "Vouchers",
                columns: new[] { "TenantId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_Vouchers_TenantId_PartyType_PartyId",
                schema: "payments",
                table: "Vouchers",
                columns: new[] { "TenantId", "PartyType", "PartyId" });

            migrationBuilder.CreateIndex(
                name: "IX_Vouchers_TenantId_VoucherNumber",
                schema: "payments",
                table: "Vouchers",
                columns: new[] { "TenantId", "VoucherNumber" },
                unique: true,
                filter: "[VoucherNumber] IS NOT NULL");

            // Cross-module references (§10.1); the principal modules are migrated first (§17.2).
            migrationBuilder.AddCrossModuleForeignKey("payments", "PaymentMethods", "AccountId", "accounting", "Accounts");
            migrationBuilder.AddCrossModuleForeignKey("payments", "PaymentMethods", "BankAccountId", "banking", "BankAccounts");
            migrationBuilder.AddCrossModuleForeignKey("payments", "Vouchers", "PartyAccountId", "accounting", "Accounts");
            migrationBuilder.AddCrossModuleForeignKey("payments", "Vouchers", "CostCenterId", "accounting", "CostCenters");
            migrationBuilder.AddCrossModuleForeignKey("payments", "VoucherPayments", "TreasuryAccountId", "accounting", "Accounts");
            migrationBuilder.AddCrossModuleForeignKey("payments", "VoucherPayments", "BankAccountId", "banking", "BankAccounts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VoucherAllocations",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "VoucherPayments",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "PaymentMethods",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "Vouchers",
                schema: "payments");
        }
    }
}
