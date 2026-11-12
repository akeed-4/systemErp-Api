using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class SiteReportItemAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SiteReportItemAttachment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReportId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    table.PrimaryKey("PK_SiteReportItemAttachment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SiteReportItemAttachment_CommercialContractItem_ContractItemId",
                        column: x => x.ContractItemId,
                        principalTable: "CommercialContractItem",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SiteReportItemAttachment_CommercialContract_ContractId",
                        column: x => x.ContractId,
                        principalTable: "CommercialContract",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SiteReportItemAttachment_SiteDailyReport_ReportId",
                        column: x => x.ReportId,
                        principalTable: "SiteDailyReport",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SiteReportItemAttachment_ContractId",
                table: "SiteReportItemAttachment",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteReportItemAttachment_ContractItemId",
                table: "SiteReportItemAttachment",
                column: "ContractItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteReportItemAttachment_ReportId",
                table: "SiteReportItemAttachment",
                column: "ReportId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteReportItemAttachment_TenantId",
                table: "SiteReportItemAttachment",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteReportItemAttachment_TenantId_ContractId",
                table: "SiteReportItemAttachment",
                columns: new[] { "TenantId", "ContractId" });

            migrationBuilder.CreateIndex(
                name: "IX_SiteReportItemAttachment_TenantId_ReportId_ContractItemId",
                table: "SiteReportItemAttachment",
                columns: new[] { "TenantId", "ReportId", "ContractItemId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SiteReportItemAttachment");
        }
    }
}
