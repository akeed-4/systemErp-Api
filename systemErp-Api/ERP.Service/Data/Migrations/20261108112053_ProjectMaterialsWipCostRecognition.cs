using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProjectMaterialsWipCostRecognition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BoqItemId",
                table: "ProjectMaterialIssueLine",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "ProjectMaterialIssueLine",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<string>(
                name: "DebitAccountCode",
                table: "ProjectMaterialIssue",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CostJournalEntryId",
                table: "ContractCertificate",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreditNoteInvoiceId",
                table: "ContractCertificate",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreditNoteNumber",
                table: "ContractCertificate",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "InvoiceCancelledAt",
                table: "ContractCertificate",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceCancelledBy",
                table: "ContractCertificate",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MaterialCost",
                table: "ContractCertificate",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "ContractCostOfRevenueAccountCode",
                table: "AccountingSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProjectMaterialsWipAccountCode",
                table: "AccountingSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ContractCertificateMaterial",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CertificateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IssueId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IssueLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IssueNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IssueDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Sku = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ItemName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractCertificateMaterial", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractCertificateMaterial_ContractCertificate_CertificateId",
                        column: x => x.CertificateId,
                        principalTable: "ContractCertificate",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ContractCertificateMaterial_ProjectMaterialIssueLine_IssueLineId",
                        column: x => x.IssueLineId,
                        principalTable: "ProjectMaterialIssueLine",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMaterialIssueLine_TenantId_BoqItemId",
                table: "ProjectMaterialIssueLine",
                columns: new[] { "TenantId", "BoqItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_ContractInvoice_TenantId_CertificateId_CreditNote",
                table: "ContractInvoice",
                columns: new[] { "TenantId", "CertificateId", "Kind" },
                unique: true,
                filter: "[CertificateId] IS NOT NULL AND [Kind] = N'credit_note'");

            migrationBuilder.CreateIndex(
                name: "IX_ContractCertificateMaterial_CertificateId_IssueLineId",
                table: "ContractCertificateMaterial",
                columns: new[] { "CertificateId", "IssueLineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContractCertificateMaterial_IssueLineId",
                table: "ContractCertificateMaterial",
                column: "IssueLineId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractCertificateMaterial_TenantId",
                table: "ContractCertificateMaterial",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractCertificateMaterial_TenantId_IssueLineId",
                table: "ContractCertificateMaterial",
                columns: new[] { "TenantId", "IssueLineId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContractCertificateMaterial");

            migrationBuilder.DropIndex(
                name: "IX_ProjectMaterialIssueLine_TenantId_BoqItemId",
                table: "ProjectMaterialIssueLine");

            migrationBuilder.DropIndex(
                name: "IX_ContractInvoice_TenantId_CertificateId_CreditNote",
                table: "ContractInvoice");

            migrationBuilder.DropColumn(
                name: "BoqItemId",
                table: "ProjectMaterialIssueLine");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "ProjectMaterialIssueLine");

            migrationBuilder.DropColumn(
                name: "DebitAccountCode",
                table: "ProjectMaterialIssue");

            migrationBuilder.DropColumn(
                name: "CostJournalEntryId",
                table: "ContractCertificate");

            migrationBuilder.DropColumn(
                name: "CreditNoteInvoiceId",
                table: "ContractCertificate");

            migrationBuilder.DropColumn(
                name: "CreditNoteNumber",
                table: "ContractCertificate");

            migrationBuilder.DropColumn(
                name: "InvoiceCancelledAt",
                table: "ContractCertificate");

            migrationBuilder.DropColumn(
                name: "InvoiceCancelledBy",
                table: "ContractCertificate");

            migrationBuilder.DropColumn(
                name: "MaterialCost",
                table: "ContractCertificate");

            migrationBuilder.DropColumn(
                name: "ContractCostOfRevenueAccountCode",
                table: "AccountingSettings");

            migrationBuilder.DropColumn(
                name: "ProjectMaterialsWipAccountCode",
                table: "AccountingSettings");
        }
    }
}
