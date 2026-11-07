using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class ConstructionProjectSetup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConstructionProjectProfile",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EstimatedBudget = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    WorkdayStartMinutes = table.Column<int>(type: "int", nullable: false),
                    WorkdayEndMinutes = table.Column<int>(type: "int", nullable: false),
                    BreakStartMinutes = table.Column<int>(type: "int", nullable: true),
                    BreakEndMinutes = table.Column<int>(type: "int", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConstructionProjectProfile", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConstructionProjectProfile_CommercialContract_ContractId",
                        column: x => x.ContractId,
                        principalTable: "CommercialContract",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProjectMember",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectMember", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectMember_CommercialContract_ContractId",
                        column: x => x.ContractId,
                        principalTable: "CommercialContract",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProjectPhase",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PlannedStart = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PlannedEnd = table.Column<DateTime>(type: "datetime2", nullable: false),
                    WeightPercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectPhase", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectPhase_CommercialContract_ContractId",
                        column: x => x.ContractId,
                        principalTable: "CommercialContract",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProjectPhaseItem",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PhaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectPhaseItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectPhaseItem_ProjectPhase_PhaseId",
                        column: x => x.PhaseId,
                        principalTable: "ProjectPhase",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConstructionProjectProfile_ContractId",
                table: "ConstructionProjectProfile",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_ConstructionProjectProfile_TenantId",
                table: "ConstructionProjectProfile",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ConstructionProjectProfile_TenantId_ContractId",
                table: "ConstructionProjectProfile",
                columns: new[] { "TenantId", "ContractId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMember_ContractId",
                table: "ProjectMember",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMember_TenantId",
                table: "ProjectMember",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMember_TenantId_ContractId_UserId",
                table: "ProjectMember",
                columns: new[] { "TenantId", "ContractId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMember_TenantId_UserId",
                table: "ProjectMember",
                columns: new[] { "TenantId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectPhase_ContractId",
                table: "ProjectPhase",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectPhase_TenantId",
                table: "ProjectPhase",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectPhase_TenantId_ContractId",
                table: "ProjectPhase",
                columns: new[] { "TenantId", "ContractId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectPhaseItem_PhaseId",
                table: "ProjectPhaseItem",
                column: "PhaseId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectPhaseItem_TenantId",
                table: "ProjectPhaseItem",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectPhaseItem_TenantId_PhaseId_ContractItemId",
                table: "ProjectPhaseItem",
                columns: new[] { "TenantId", "PhaseId", "ContractItemId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConstructionProjectProfile");

            migrationBuilder.DropTable(
                name: "ProjectMember");

            migrationBuilder.DropTable(
                name: "ProjectPhaseItem");

            migrationBuilder.DropTable(
                name: "ProjectPhase");
        }
    }
}
