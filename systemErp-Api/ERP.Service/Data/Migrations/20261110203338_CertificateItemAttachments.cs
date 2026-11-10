using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class CertificateItemAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContractCertificateItemAttachment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CertificateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    FileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Content = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    UploadedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    UploadedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractCertificateItemAttachment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractCertificateItemAttachment_CommercialContractItem_ContractItemId",
                        column: x => x.ContractItemId,
                        principalTable: "CommercialContractItem",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContractCertificateItemAttachment_CommercialContract_ContractId",
                        column: x => x.ContractId,
                        principalTable: "CommercialContract",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContractCertificateItemAttachment_ContractCertificate_CertificateId",
                        column: x => x.CertificateId,
                        principalTable: "ContractCertificate",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContractCertificateItemAttachment_CertificateId",
                table: "ContractCertificateItemAttachment",
                column: "CertificateId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractCertificateItemAttachment_ContractId",
                table: "ContractCertificateItemAttachment",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractCertificateItemAttachment_ContractItemId",
                table: "ContractCertificateItemAttachment",
                column: "ContractItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractCertificateItemAttachment_TenantId",
                table: "ContractCertificateItemAttachment",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractCertificateItemAttachment_TenantId_CertificateId_ContractItemId",
                table: "ContractCertificateItemAttachment",
                columns: new[] { "TenantId", "CertificateId", "ContractItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_ContractCertificateItemAttachment_TenantId_ProjectContractId",
                table: "ContractCertificateItemAttachment",
                columns: new[] { "TenantId", "ProjectContractId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContractCertificateItemAttachment");
        }
    }
}
