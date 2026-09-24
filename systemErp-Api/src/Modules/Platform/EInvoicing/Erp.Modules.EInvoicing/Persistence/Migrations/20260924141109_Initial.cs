using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.EInvoicing.Persistence.Migrations
{
    /// <inheritdoc />
    internal partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "einvoicing");

            migrationBuilder.CreateTable(
                name: "EInvoicingDevices",
                schema: "einvoicing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SerialNumber = table.Column<string>(type: "varchar(60)", unicode: false, maxLength: 60, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    Environment = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    Phase = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    ComplianceStatus = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    AutoSubmit = table.Column<bool>(type: "bit", nullable: false),
                    SolutionName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SolutionVersion = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TaxRegistrationNumber = table.Column<string>(type: "varchar(15)", unicode: false, maxLength: 15, nullable: true),
                    CsrCommonName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    OrganizationUnit = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    OrganizationName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CountryCode = table.Column<string>(type: "varchar(2)", unicode: false, maxLength: 2, nullable: false),
                    BusinessCategory = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CustomEndpointUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CsidEncrypted = table.Column<string>(type: "varchar(4000)", unicode: false, maxLength: 4000, nullable: true),
                    SecretEncrypted = table.Column<string>(type: "varchar(4000)", unicode: false, maxLength: 4000, nullable: true),
                    CertificatePem = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PrivateKeyEncrypted = table.Column<string>(type: "varchar(8000)", unicode: false, maxLength: 8000, nullable: true),
                    LastIcv = table.Column<long>(type: "bigint", nullable: false),
                    LastInvoiceHash = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    LastTestAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastTestStatus = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    LastTestLatencyMs = table.Column<int>(type: "int", nullable: true),
                    LastTestMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EInvoicingDevices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EInvoiceDocuments",
                schema: "einvoicing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceModule = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    SourceDocumentType = table.Column<string>(type: "varchar(60)", unicode: false, maxLength: 60, nullable: false),
                    SourceDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentNumber = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Kind = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TotalWithVat = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    VatTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    BuyerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BuyerVatNumber = table.Column<string>(type: "varchar(15)", unicode: false, maxLength: 15, nullable: true),
                    OriginalDocumentNumber = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Uuid = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Icv = table.Column<long>(type: "bigint", nullable: false),
                    PreviousInvoiceHash = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    InvoiceHash = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    QrCode = table.Column<string>(type: "varchar(1000)", unicode: false, maxLength: 1000, nullable: false),
                    Status = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ResponseMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EInvoiceDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EInvoiceDocuments_EInvoicingDevices_DeviceId",
                        column: x => x.DeviceId,
                        principalSchema: "einvoicing",
                        principalTable: "EInvoicingDevices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EInvoiceDocuments_TenantId_DeviceId_Icv",
                schema: "einvoicing",
                table: "EInvoiceDocuments",
                columns: new[] { "TenantId", "DeviceId", "Icv" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EInvoiceDocuments_TenantId_SourceModule_SourceDocumentType_SourceDocumentId",
                schema: "einvoicing",
                table: "EInvoiceDocuments",
                columns: new[] { "TenantId", "SourceModule", "SourceDocumentType", "SourceDocumentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EInvoiceDocuments_TenantId_Status_IssuedAt",
                schema: "einvoicing",
                table: "EInvoiceDocuments",
                columns: new[] { "TenantId", "Status", "IssuedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_EInvoiceDocuments_TenantId_Uuid",
                schema: "einvoicing",
                table: "EInvoiceDocuments",
                columns: new[] { "TenantId", "Uuid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EInvoicingDevices_TenantId_IsDefault",
                schema: "einvoicing",
                table: "EInvoicingDevices",
                columns: new[] { "TenantId", "IsDefault" },
                unique: true,
                filter: "[IsDefault] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_EInvoicingDevices_TenantId_SerialNumber",
                schema: "einvoicing",
                table: "EInvoicingDevices",
                columns: new[] { "TenantId", "SerialNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EInvoiceDocuments",
                schema: "einvoicing");

            migrationBuilder.DropTable(
                name: "EInvoicingDevices",
                schema: "einvoicing");
        }
    }
}
