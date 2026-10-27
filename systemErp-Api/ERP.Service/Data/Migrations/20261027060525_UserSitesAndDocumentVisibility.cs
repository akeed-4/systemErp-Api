using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class UserSitesAndDocumentVisibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "WarehouseId",
                table: "Voucher",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AllowedWarehouseIds",
                table: "User",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentVisibility",
                table: "Tenant",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "site");

            migrationBuilder.AddColumn<Guid>(
                name: "WarehouseId",
                table: "JournalEntry",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WarehouseId",
                table: "CommercialContract",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WarehouseId",
                table: "CarSalesContract",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WarehouseId",
                table: "CarProcurementOrder",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "Voucher");

            migrationBuilder.DropColumn(
                name: "AllowedWarehouseIds",
                table: "User");

            migrationBuilder.DropColumn(
                name: "DocumentVisibility",
                table: "Tenant");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "JournalEntry");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "CommercialContract");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "CarSalesContract");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "CarProcurementOrder");
        }
    }
}
