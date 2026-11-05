using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddContractCertificates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContractCertificate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SequenceNo = table.Column<int>(type: "int", nullable: false),
                    CertificateNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CertificateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PeriodFrom = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PeriodTo = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PreviousWorkValue = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CurrentWorkValue = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CumulativeWorkValue = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReturnNote = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SubmittedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractCertificate", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractCertificate_CommercialContract_ContractId",
                        column: x => x.ContractId,
                        principalTable: "CommercialContract",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContractCertificateLine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CertificateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    SectionName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LineKind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ContractQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    OverrunPercent = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    PreviousQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CurrentQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CumulativeQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PreviousAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CurrentAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CumulativeAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CompletionPercent = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractCertificateLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractCertificateLine_CommercialContractItem_ContractItemId",
                        column: x => x.ContractItemId,
                        principalTable: "CommercialContractItem",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContractCertificateLine_ContractCertificate_CertificateId",
                        column: x => x.CertificateId,
                        principalTable: "ContractCertificate",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContractCertificate_ContractId",
                table: "ContractCertificate",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractCertificate_TenantId",
                table: "ContractCertificate",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractCertificate_TenantId_CertificateNumber",
                table: "ContractCertificate",
                columns: new[] { "TenantId", "CertificateNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContractCertificate_TenantId_ContractId_Open",
                table: "ContractCertificate",
                columns: new[] { "TenantId", "ContractId" },
                unique: true,
                filter: "[Status] IN (N'draft', N'submitted')");

            migrationBuilder.CreateIndex(
                name: "IX_ContractCertificate_TenantId_ContractId_SequenceNo",
                table: "ContractCertificate",
                columns: new[] { "TenantId", "ContractId", "SequenceNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContractCertificateLine_CertificateId_ContractItemId",
                table: "ContractCertificateLine",
                columns: new[] { "CertificateId", "ContractItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContractCertificateLine_ContractItemId",
                table: "ContractCertificateLine",
                column: "ContractItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractCertificateLine_TenantId",
                table: "ContractCertificateLine",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContractCertificateLine");

            migrationBuilder.DropTable(
                name: "ContractCertificate");
        }
    }
}
