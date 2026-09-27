using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPosCashDrawer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClosingDenominations",
                table: "PosShift",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalCashIn",
                table: "PosShift",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalCashOut",
                table: "PosShift",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "VarianceJournalEntryId",
                table: "PosShift",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PosCashMovement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShiftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CounterAccountCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    JournalEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedByName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PosCashMovement", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PosCashMovement_TenantId",
                table: "PosCashMovement",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PosCashMovement");

            migrationBuilder.DropColumn(
                name: "ClosingDenominations",
                table: "PosShift");

            migrationBuilder.DropColumn(
                name: "TotalCashIn",
                table: "PosShift");

            migrationBuilder.DropColumn(
                name: "TotalCashOut",
                table: "PosShift");

            migrationBuilder.DropColumn(
                name: "VarianceJournalEntryId",
                table: "PosShift");
        }
    }
}
