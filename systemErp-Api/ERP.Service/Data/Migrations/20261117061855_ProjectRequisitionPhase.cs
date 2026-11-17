using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProjectRequisitionPhase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PhaseId",
                table: "MaterialRequisition",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaterialRequisition_TenantId_PhaseId",
                table: "MaterialRequisition",
                columns: new[] { "TenantId", "PhaseId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MaterialRequisition_TenantId_PhaseId",
                table: "MaterialRequisition");

            migrationBuilder.DropColumn(
                name: "PhaseId",
                table: "MaterialRequisition");
        }
    }
}
