using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProjectCostAndSiteMaterials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CostJournalEntryId",
                table: "SiteDailyReport",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PostedEquipmentCost",
                table: "SiteDailyReport",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PostedLaborCost",
                table: "SiteDailyReport",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceLineId",
                table: "ProjectMaterialIssueLine",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "ProjectMaterialIssue",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "issue"); // المستندات القائمة كلها أذونات تسليم

            migrationBuilder.AddColumn<string>(
                name: "ProjectAbsorbedCostAccountCode",
                table: "AccountingSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProjectEquipmentCostAccountCode",
                table: "AccountingSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProjectLaborCostAccountCode",
                table: "AccountingSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMaterialIssueLine_TenantId_SourceLineId",
                table: "ProjectMaterialIssueLine",
                columns: new[] { "TenantId", "SourceLineId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProjectMaterialIssueLine_TenantId_SourceLineId",
                table: "ProjectMaterialIssueLine");

            migrationBuilder.DropColumn(
                name: "CostJournalEntryId",
                table: "SiteDailyReport");

            migrationBuilder.DropColumn(
                name: "PostedEquipmentCost",
                table: "SiteDailyReport");

            migrationBuilder.DropColumn(
                name: "PostedLaborCost",
                table: "SiteDailyReport");

            migrationBuilder.DropColumn(
                name: "SourceLineId",
                table: "ProjectMaterialIssueLine");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "ProjectMaterialIssue");

            migrationBuilder.DropColumn(
                name: "ProjectAbsorbedCostAccountCode",
                table: "AccountingSettings");

            migrationBuilder.DropColumn(
                name: "ProjectEquipmentCostAccountCode",
                table: "AccountingSettings");

            migrationBuilder.DropColumn(
                name: "ProjectLaborCostAccountCode",
                table: "AccountingSettings");
        }
    }
}
