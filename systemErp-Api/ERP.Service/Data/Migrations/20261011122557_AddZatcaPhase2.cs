using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddZatcaPhase2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ZatcaConfig_BuildingNumber",
                table: "Tenant",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ZatcaConfig_ComplianceRequestId",
                table: "Tenant",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ZatcaConfig_District",
                table: "Tenant",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ZatcaConfig_OnboardedAt",
                table: "Tenant",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ZatcaConfig_PostalCode",
                table: "Tenant",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ZatcaConfig_Street",
                table: "Tenant",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SendToZatca",
                table: "Invoice",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ZatcaChain",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Counter = table.Column<long>(type: "bigint", nullable: false),
                    LastHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZatcaChain", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ZatcaChain_TenantId",
                table: "ZatcaChain",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ZatcaChain");

            migrationBuilder.DropColumn(
                name: "ZatcaConfig_BuildingNumber",
                table: "Tenant");

            migrationBuilder.DropColumn(
                name: "ZatcaConfig_ComplianceRequestId",
                table: "Tenant");

            migrationBuilder.DropColumn(
                name: "ZatcaConfig_District",
                table: "Tenant");

            migrationBuilder.DropColumn(
                name: "ZatcaConfig_OnboardedAt",
                table: "Tenant");

            migrationBuilder.DropColumn(
                name: "ZatcaConfig_PostalCode",
                table: "Tenant");

            migrationBuilder.DropColumn(
                name: "ZatcaConfig_Street",
                table: "Tenant");

            migrationBuilder.DropColumn(
                name: "SendToZatca",
                table: "Invoice");
        }
    }
}
