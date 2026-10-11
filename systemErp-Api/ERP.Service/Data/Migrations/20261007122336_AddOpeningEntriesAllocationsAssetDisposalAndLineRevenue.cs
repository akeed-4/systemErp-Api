using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOpeningEntriesAllocationsAssetDisposalAndLineRevenue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "VatAmount",
                table: "Voucher",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "OpeningEntryId",
                table: "Supplier",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RevenueAccountCode",
                table: "InvoiceItem",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DisposalJournalEntryId",
                table: "FixedAsset",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DisposalProceeds",
                table: "FixedAsset",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DisposedAt",
                table: "FixedAsset",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SalvageValue",
                table: "FixedAsset",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "OpeningEntryId",
                table: "Customer",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OpeningEntryId",
                table: "BankEntity",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "VoucherAllocation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VoucherId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VoucherAllocation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VoucherAllocation_Invoice_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoice",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VoucherAllocation_Voucher_VoucherId",
                        column: x => x.VoucherId,
                        principalTable: "Voucher",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VoucherAllocation_InvoiceId",
                table: "VoucherAllocation",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_VoucherAllocation_TenantId",
                table: "VoucherAllocation",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_VoucherAllocation_VoucherId",
                table: "VoucherAllocation",
                column: "VoucherId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VoucherAllocation");

            migrationBuilder.DropColumn(
                name: "VatAmount",
                table: "Voucher");

            migrationBuilder.DropColumn(
                name: "OpeningEntryId",
                table: "Supplier");

            migrationBuilder.DropColumn(
                name: "RevenueAccountCode",
                table: "InvoiceItem");

            migrationBuilder.DropColumn(
                name: "DisposalJournalEntryId",
                table: "FixedAsset");

            migrationBuilder.DropColumn(
                name: "DisposalProceeds",
                table: "FixedAsset");

            migrationBuilder.DropColumn(
                name: "DisposedAt",
                table: "FixedAsset");

            migrationBuilder.DropColumn(
                name: "SalvageValue",
                table: "FixedAsset");

            migrationBuilder.DropColumn(
                name: "OpeningEntryId",
                table: "Customer");

            migrationBuilder.DropColumn(
                name: "OpeningEntryId",
                table: "BankEntity");
        }
    }
}
