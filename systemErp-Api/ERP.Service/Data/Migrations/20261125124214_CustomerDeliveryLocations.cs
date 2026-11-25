using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class CustomerDeliveryLocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequireDeliveryLocationReview",
                table: "Tenant",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryAddress",
                table: "DeliveryNote",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DeliveryLatitude",
                table: "DeliveryNote",
                type: "decimal(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeliveryLocationId",
                table: "DeliveryNote",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DeliveryLongitude",
                table: "DeliveryNote",
                type: "decimal(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LocationReviewNotes",
                table: "DeliveryNote",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LocationReviewedAt",
                table: "DeliveryNote",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LocationReviewedByName",
                table: "DeliveryNote",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LocationReviewedByUserId",
                table: "DeliveryNote",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CustomerDeliveryLocation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    District = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Latitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: true),
                    Longitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: true),
                    ContactName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    ContactPhone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerDeliveryLocation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerDeliveryLocation_Customer_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryNote_TenantId_DeliveryLocationId",
                table: "DeliveryNote",
                columns: new[] { "TenantId", "DeliveryLocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDeliveryLocation_CustomerId",
                table: "CustomerDeliveryLocation",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDeliveryLocation_TenantId",
                table: "CustomerDeliveryLocation",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDeliveryLocation_TenantId_CustomerId",
                table: "CustomerDeliveryLocation",
                columns: new[] { "TenantId", "CustomerId" },
                unique: true,
                filter: "[IsDefault] = 1 AND [IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDeliveryLocation_TenantId_CustomerId_IsActive",
                table: "CustomerDeliveryLocation",
                columns: new[] { "TenantId", "CustomerId", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerDeliveryLocation");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryNote_TenantId_DeliveryLocationId",
                table: "DeliveryNote");

            migrationBuilder.DropColumn(
                name: "RequireDeliveryLocationReview",
                table: "Tenant");

            migrationBuilder.DropColumn(
                name: "DeliveryAddress",
                table: "DeliveryNote");

            migrationBuilder.DropColumn(
                name: "DeliveryLatitude",
                table: "DeliveryNote");

            migrationBuilder.DropColumn(
                name: "DeliveryLocationId",
                table: "DeliveryNote");

            migrationBuilder.DropColumn(
                name: "DeliveryLongitude",
                table: "DeliveryNote");

            migrationBuilder.DropColumn(
                name: "LocationReviewNotes",
                table: "DeliveryNote");

            migrationBuilder.DropColumn(
                name: "LocationReviewedAt",
                table: "DeliveryNote");

            migrationBuilder.DropColumn(
                name: "LocationReviewedByName",
                table: "DeliveryNote");

            migrationBuilder.DropColumn(
                name: "LocationReviewedByUserId",
                table: "DeliveryNote");
        }
    }
}
