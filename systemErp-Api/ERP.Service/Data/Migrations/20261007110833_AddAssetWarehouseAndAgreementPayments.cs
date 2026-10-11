using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAssetWarehouseAndAgreementPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "WarehouseId",
                table: "FixedAssetDepreciation",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WarehouseId",
                table: "FixedAsset",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AgreementPayment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgreementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Percentage = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgreementPayment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgreementPayment_Agreement_AgreementId",
                        column: x => x.AgreementId,
                        principalTable: "Agreement",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FixedAssetDepreciation_WarehouseId",
                table: "FixedAssetDepreciation",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_FixedAsset_WarehouseId",
                table: "FixedAsset",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_AgreementPayment_AgreementId",
                table: "AgreementPayment",
                column: "AgreementId");

            migrationBuilder.CreateIndex(
                name: "IX_AgreementPayment_TenantId",
                table: "AgreementPayment",
                column: "TenantId");

            migrationBuilder.AddForeignKey(
                name: "FK_FixedAsset_Warehouse_WarehouseId",
                table: "FixedAsset",
                column: "WarehouseId",
                principalTable: "Warehouse",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_FixedAssetDepreciation_Warehouse_WarehouseId",
                table: "FixedAssetDepreciation",
                column: "WarehouseId",
                principalTable: "Warehouse",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FixedAsset_Warehouse_WarehouseId",
                table: "FixedAsset");

            migrationBuilder.DropForeignKey(
                name: "FK_FixedAssetDepreciation_Warehouse_WarehouseId",
                table: "FixedAssetDepreciation");

            migrationBuilder.DropTable(
                name: "AgreementPayment");

            migrationBuilder.DropIndex(
                name: "IX_FixedAssetDepreciation_WarehouseId",
                table: "FixedAssetDepreciation");

            migrationBuilder.DropIndex(
                name: "IX_FixedAsset_WarehouseId",
                table: "FixedAsset");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "FixedAssetDepreciation");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "FixedAsset");
        }
    }
}
