using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class FeasibilityStudies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FeasibilityNumber",
                table: "CommercialContract",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FeasibilityStatus",
                table: "CommercialContract",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommercialContract_TenantId_FeasibilityStatus",
                table: "CommercialContract",
                columns: new[] { "TenantId", "FeasibilityStatus" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CommercialContract_TenantId_FeasibilityStatus",
                table: "CommercialContract");

            migrationBuilder.DropColumn(
                name: "FeasibilityNumber",
                table: "CommercialContract");

            migrationBuilder.DropColumn(
                name: "FeasibilityStatus",
                table: "CommercialContract");
        }
    }
}
