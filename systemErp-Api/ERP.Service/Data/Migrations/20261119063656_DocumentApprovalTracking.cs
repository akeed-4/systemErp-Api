using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class DocumentApprovalTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentApprovalTrack",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    ScreenId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Url = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    RequesterUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequesterName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DecidedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecidedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentApprovalTrack", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentApprovalTrack_TenantId",
                table: "DocumentApprovalTrack",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentApprovalTrack_TenantId_DocumentType_DocumentId",
                table: "DocumentApprovalTrack",
                columns: new[] { "TenantId", "DocumentType", "DocumentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentApprovalTrack_TenantId_RequesterUserId_SubmittedAt",
                table: "DocumentApprovalTrack",
                columns: new[] { "TenantId", "RequesterUserId", "SubmittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentApprovalTrack_TenantId_Status_ScreenId",
                table: "DocumentApprovalTrack",
                columns: new[] { "TenantId", "Status", "ScreenId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentApprovalTrack");
        }
    }
}
