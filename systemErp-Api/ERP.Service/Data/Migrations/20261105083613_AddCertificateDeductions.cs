using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCertificateDeductions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AdvanceRecovery",
                table: "ContractCertificate",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "NetPayable",
                table: "ContractCertificate",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OtherDeductions",
                table: "ContractCertificate",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PenaltyAmount",
                table: "ContractCertificate",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "RetentionAmount",
                table: "ContractCertificate",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxableAmount",
                table: "ContractCertificate",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalWithVat",
                table: "ContractCertificate",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "VatAmount",
                table: "ContractCertificate",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "VatRate",
                table: "ContractCertificate",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "ContractCertificateDeduction",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CertificateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractCertificateDeduction", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractCertificateDeduction_ContractCertificate_CertificateId",
                        column: x => x.CertificateId,
                        principalTable: "ContractCertificate",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContractCertificateDeduction_CertificateId",
                table: "ContractCertificateDeduction",
                column: "CertificateId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractCertificateDeduction_TenantId",
                table: "ContractCertificateDeduction",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContractCertificateDeduction");

            migrationBuilder.DropColumn(
                name: "AdvanceRecovery",
                table: "ContractCertificate");

            migrationBuilder.DropColumn(
                name: "NetPayable",
                table: "ContractCertificate");

            migrationBuilder.DropColumn(
                name: "OtherDeductions",
                table: "ContractCertificate");

            migrationBuilder.DropColumn(
                name: "PenaltyAmount",
                table: "ContractCertificate");

            migrationBuilder.DropColumn(
                name: "RetentionAmount",
                table: "ContractCertificate");

            migrationBuilder.DropColumn(
                name: "TaxableAmount",
                table: "ContractCertificate");

            migrationBuilder.DropColumn(
                name: "TotalWithVat",
                table: "ContractCertificate");

            migrationBuilder.DropColumn(
                name: "VatAmount",
                table: "ContractCertificate");

            migrationBuilder.DropColumn(
                name: "VatRate",
                table: "ContractCertificate");
        }
    }
}
