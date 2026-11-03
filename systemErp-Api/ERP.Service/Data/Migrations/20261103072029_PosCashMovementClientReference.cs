using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class PosCashMovementClientReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClientReference",
                table: "PosCashMovement",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PosCashMovement_TenantId_ClientReference",
                table: "PosCashMovement",
                columns: new[] { "TenantId", "ClientReference" },
                unique: true,
                filter: "[ClientReference] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PosCashMovement_TenantId_ClientReference",
                table: "PosCashMovement");

            migrationBuilder.DropColumn(
                name: "ClientReference",
                table: "PosCashMovement");
        }
    }
}
