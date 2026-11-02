using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPosOfflineSales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClientReference",
                table: "PosTransaction",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsOffline",
                table: "PosTransaction",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "OfflineIssuedAt",
                table: "PosTransaction",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OfflinePrintedTotal",
                table: "PosTransaction",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OfflineReceiptNumber",
                table: "PosTransaction",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OfflineReviewNote",
                table: "PosTransaction",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PosTransaction_TenantId_ClientReference",
                table: "PosTransaction",
                columns: new[] { "TenantId", "ClientReference" },
                unique: true,
                filter: "[ClientReference] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PosTransaction_TenantId_ClientReference",
                table: "PosTransaction");

            migrationBuilder.DropColumn(
                name: "ClientReference",
                table: "PosTransaction");

            migrationBuilder.DropColumn(
                name: "IsOffline",
                table: "PosTransaction");

            migrationBuilder.DropColumn(
                name: "OfflineIssuedAt",
                table: "PosTransaction");

            migrationBuilder.DropColumn(
                name: "OfflinePrintedTotal",
                table: "PosTransaction");

            migrationBuilder.DropColumn(
                name: "OfflineReceiptNumber",
                table: "PosTransaction");

            migrationBuilder.DropColumn(
                name: "OfflineReviewNote",
                table: "PosTransaction");
        }
    }
}
