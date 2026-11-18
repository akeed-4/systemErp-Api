using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExecutionPlanMaterialEdits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "StudyCost",
                table: "ProjectMaterialRequirement",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "StudyQuantity",
                table: "ProjectMaterialRequirement",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "StudyUnitCost",
                table: "ProjectMaterialRequirement",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "StudyCostAtGeneration",
                table: "ProjectExecutionPlan",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StudyFingerprint",
                table: "ProjectExecutionPlan",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StudyCost",
                table: "ProjectMaterialRequirement");

            migrationBuilder.DropColumn(
                name: "StudyQuantity",
                table: "ProjectMaterialRequirement");

            migrationBuilder.DropColumn(
                name: "StudyUnitCost",
                table: "ProjectMaterialRequirement");

            migrationBuilder.DropColumn(
                name: "StudyCostAtGeneration",
                table: "ProjectExecutionPlan");

            migrationBuilder.DropColumn(
                name: "StudyFingerprint",
                table: "ProjectExecutionPlan");
        }
    }
}
