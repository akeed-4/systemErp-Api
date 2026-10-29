using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPosTerminalRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowDiscount",
                table: "PosShift",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "AllowReturns",
                table: "PosShift",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "AllowedCategoryIdsJson",
                table: "PosShift",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<decimal>(
                name: "MaxDiscountPercent",
                table: "PosShift",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PosTerminalId",
                table: "PosShift",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WarehouseId",
                table: "PosShift",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PosTerminal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DeviceIdentifier = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    HardwareType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    HardwareModel = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    HardwareSerialNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConnectionType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PrinterModel = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PrinterConnectionType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PrinterAddress = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    HasCashDrawer = table.Column<bool>(type: "bit", nullable: false),
                    AllowReturns = table.Column<bool>(type: "bit", nullable: false),
                    AllowDiscount = table.Column<bool>(type: "bit", nullable: false),
                    MaxDiscountPercent = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    AllowedCategoryIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AllowedUserIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PosTerminal", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PosTerminal_TenantId",
                table: "PosTerminal",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PosTerminal");

            migrationBuilder.DropColumn(
                name: "AllowDiscount",
                table: "PosShift");

            migrationBuilder.DropColumn(
                name: "AllowReturns",
                table: "PosShift");

            migrationBuilder.DropColumn(
                name: "AllowedCategoryIdsJson",
                table: "PosShift");

            migrationBuilder.DropColumn(
                name: "MaxDiscountPercent",
                table: "PosShift");

            migrationBuilder.DropColumn(
                name: "PosTerminalId",
                table: "PosShift");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "PosShift");
        }
    }
}
