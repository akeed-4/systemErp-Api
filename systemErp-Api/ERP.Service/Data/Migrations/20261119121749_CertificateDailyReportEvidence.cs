using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class CertificateDailyReportEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "SiteDailyReport",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "ContractCertificate",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancelledBy",
                table: "ContractCertificate",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RejectedAt",
                table: "ContractCertificate",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectedBy",
                table: "ContractCertificate",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ContractCertificateEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CertificateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReportId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HourEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SlotStart = table.Column<int>(type: "int", nullable: false),
                    SlotEnd = table.Column<int>(type: "int", nullable: false),
                    EntryQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractCertificateEvidence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractCertificateEvidence_CommercialContractItem_ContractItemId",
                        column: x => x.ContractItemId,
                        principalTable: "CommercialContractItem",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContractCertificateEvidence_ContractCertificate_CertificateId",
                        column: x => x.CertificateId,
                        principalTable: "ContractCertificate",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContractCertificateEvidence_CertificateId_HourEntryId",
                table: "ContractCertificateEvidence",
                columns: new[] { "CertificateId", "HourEntryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContractCertificateEvidence_ContractItemId",
                table: "ContractCertificateEvidence",
                column: "ContractItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractCertificateEvidence_TenantId",
                table: "ContractCertificateEvidence",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractCertificateEvidence_TenantId_HourEntryId",
                table: "ContractCertificateEvidence",
                columns: new[] { "TenantId", "HourEntryId" });

            migrationBuilder.CreateIndex(
                name: "IX_ContractCertificateEvidence_TenantId_ReportId",
                table: "ContractCertificateEvidence",
                columns: new[] { "TenantId", "ReportId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContractCertificateEvidence");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "SiteDailyReport");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "ContractCertificate");

            migrationBuilder.DropColumn(
                name: "CancelledBy",
                table: "ContractCertificate");

            migrationBuilder.DropColumn(
                name: "RejectedAt",
                table: "ContractCertificate");

            migrationBuilder.DropColumn(
                name: "RejectedBy",
                table: "ContractCertificate");
        }
    }
}
