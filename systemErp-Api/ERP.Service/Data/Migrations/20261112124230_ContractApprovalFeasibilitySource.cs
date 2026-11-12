using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class ContractApprovalFeasibilitySource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ApprovedValue",
                table: "ContractApproval",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "InvalidatedAt",
                table: "ContractApproval",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvalidationReason",
                table: "ContractApproval",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "ContractApproval",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceId",
                table: "ContractApproval",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceReference",
                table: "ContractApproval",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApprovedValue",
                table: "ContractApproval");

            migrationBuilder.DropColumn(
                name: "InvalidatedAt",
                table: "ContractApproval");

            migrationBuilder.DropColumn(
                name: "InvalidationReason",
                table: "ContractApproval");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "ContractApproval");

            migrationBuilder.DropColumn(
                name: "SourceId",
                table: "ContractApproval");

            migrationBuilder.DropColumn(
                name: "SourceReference",
                table: "ContractApproval");
        }
    }
}
