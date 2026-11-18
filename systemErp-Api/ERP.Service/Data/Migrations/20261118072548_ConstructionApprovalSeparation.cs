using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class ConstructionApprovalSeparation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SubmittedByUserId",
                table: "SiteDailyReport",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SubmittedByUserId",
                table: "ProjectExecutionPlan",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ConstructionSeparateApprover",
                table: "AccountingSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SubmittedByUserId",
                table: "SiteDailyReport");

            migrationBuilder.DropColumn(
                name: "SubmittedByUserId",
                table: "ProjectExecutionPlan");

            migrationBuilder.DropColumn(
                name: "ConstructionSeparateApprover",
                table: "AccountingSettings");
        }
    }
}
