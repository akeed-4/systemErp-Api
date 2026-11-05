using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddContractBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "InvoiceId",
                table: "ContractInvoice",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "CertificateId",
                table: "ContractInvoice",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "JournalEntryId",
                table: "ContractInvoice",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AdvanceJournalEntryId",
                table: "ContractCertificate",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "InvoicedAt",
                table: "ContractCertificate",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContractAdvancesPaidAccountCode",
                table: "AccountingSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContractAdvancesReceivedAccountCode",
                table: "AccountingSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContractOtherDeductionsAccountCode",
                table: "AccountingSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContractPenaltiesExpenseAccountCode",
                table: "AccountingSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContractPenaltiesIncomeAccountCode",
                table: "AccountingSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContractRetentionPayableAccountCode",
                table: "AccountingSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContractRetentionReceivableAccountCode",
                table: "AccountingSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContractInvoice_TenantId_CertificateId",
                table: "ContractInvoice",
                columns: new[] { "TenantId", "CertificateId" },
                unique: true,
                filter: "[CertificateId] IS NOT NULL AND [Kind] <> N'credit_note'");

            migrationBuilder.CreateIndex(
                name: "IX_ContractInvoice_TenantId_ContractId_Advance",
                table: "ContractInvoice",
                columns: new[] { "TenantId", "ContractId", "Kind" },
                unique: true,
                filter: "[Kind] = N'advance'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ContractInvoice_TenantId_CertificateId",
                table: "ContractInvoice");

            migrationBuilder.DropIndex(
                name: "IX_ContractInvoice_TenantId_ContractId_Advance",
                table: "ContractInvoice");

            migrationBuilder.DropColumn(
                name: "CertificateId",
                table: "ContractInvoice");

            migrationBuilder.DropColumn(
                name: "JournalEntryId",
                table: "ContractInvoice");

            migrationBuilder.DropColumn(
                name: "AdvanceJournalEntryId",
                table: "ContractCertificate");

            migrationBuilder.DropColumn(
                name: "InvoicedAt",
                table: "ContractCertificate");

            migrationBuilder.DropColumn(
                name: "ContractAdvancesPaidAccountCode",
                table: "AccountingSettings");

            migrationBuilder.DropColumn(
                name: "ContractAdvancesReceivedAccountCode",
                table: "AccountingSettings");

            migrationBuilder.DropColumn(
                name: "ContractOtherDeductionsAccountCode",
                table: "AccountingSettings");

            migrationBuilder.DropColumn(
                name: "ContractPenaltiesExpenseAccountCode",
                table: "AccountingSettings");

            migrationBuilder.DropColumn(
                name: "ContractPenaltiesIncomeAccountCode",
                table: "AccountingSettings");

            migrationBuilder.DropColumn(
                name: "ContractRetentionPayableAccountCode",
                table: "AccountingSettings");

            migrationBuilder.DropColumn(
                name: "ContractRetentionReceivableAccountCode",
                table: "AccountingSettings");

            migrationBuilder.AlterColumn<Guid>(
                name: "InvoiceId",
                table: "ContractInvoice",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);
        }
    }
}
