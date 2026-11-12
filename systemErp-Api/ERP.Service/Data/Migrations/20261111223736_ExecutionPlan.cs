using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExecutionPlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DatesSuggested",
                table: "ProjectPhase",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "ProjectPhase",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PlannedCost",
                table: "ProjectPhase",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "PredecessorIds",
                table: "ProjectPhase",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "ProjectPhase",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "manual");

            migrationBuilder.AddColumn<Guid>(
                name: "SupervisorUserId",
                table: "ProjectPhase",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PhaseId",
                table: "ProjectMaterialRequirement",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PlannedUnitCost",
                table: "ProjectMaterialRequirement",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "QuantityPerUnit",
                table: "ProjectMaterialRequirement",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "ProjectExecutionPlan",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    GeneratedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReturnNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SubmittedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BaselineSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectExecutionPlan", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProjectResourceRequirement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PhaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ContractItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResourceType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    HoursPerUnit = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PlannedHours = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PlannedCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Ownership = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    SuggestedCount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    SourceCostRowId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectResourceRequirement", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProjectSnapshot",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    Json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TotalCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ContractValue = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TakenAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TakenBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TakenByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectSnapshot", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectExecutionPlan_TenantId",
                table: "ProjectExecutionPlan",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectExecutionPlan_TenantId_ContractId",
                table: "ProjectExecutionPlan",
                columns: new[] { "TenantId", "ContractId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectResourceRequirement_TenantId",
                table: "ProjectResourceRequirement",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectResourceRequirement_TenantId_ContractId",
                table: "ProjectResourceRequirement",
                columns: new[] { "TenantId", "ContractId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectSnapshot_TenantId",
                table: "ProjectSnapshot",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectSnapshot_TenantId_ContractId_Kind_Revision",
                table: "ProjectSnapshot",
                columns: new[] { "TenantId", "ContractId", "Kind", "Revision" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProjectExecutionPlan");

            migrationBuilder.DropTable(
                name: "ProjectResourceRequirement");

            migrationBuilder.DropTable(
                name: "ProjectSnapshot");

            migrationBuilder.DropColumn(
                name: "DatesSuggested",
                table: "ProjectPhase");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "ProjectPhase");

            migrationBuilder.DropColumn(
                name: "PlannedCost",
                table: "ProjectPhase");

            migrationBuilder.DropColumn(
                name: "PredecessorIds",
                table: "ProjectPhase");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "ProjectPhase");

            migrationBuilder.DropColumn(
                name: "SupervisorUserId",
                table: "ProjectPhase");

            migrationBuilder.DropColumn(
                name: "PhaseId",
                table: "ProjectMaterialRequirement");

            migrationBuilder.DropColumn(
                name: "PlannedUnitCost",
                table: "ProjectMaterialRequirement");

            migrationBuilder.DropColumn(
                name: "QuantityPerUnit",
                table: "ProjectMaterialRequirement");
        }
    }
}
