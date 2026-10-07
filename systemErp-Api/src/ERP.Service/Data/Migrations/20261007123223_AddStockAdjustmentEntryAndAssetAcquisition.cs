using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStockAdjustmentEntryAndAssetAcquisition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CounterAccountCode",
                table: "StockMovement",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "JournalEntryId",
                table: "StockMovement",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AcquisitionJournalEntryId",
                table: "FixedAsset",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CounterAccountCode",
                table: "StockMovement");

            migrationBuilder.DropColumn(
                name: "JournalEntryId",
                table: "StockMovement");

            migrationBuilder.DropColumn(
                name: "AcquisitionJournalEntryId",
                table: "FixedAsset");
        }
    }
}
