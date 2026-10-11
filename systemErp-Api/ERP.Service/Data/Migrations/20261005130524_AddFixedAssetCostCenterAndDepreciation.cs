using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFixedAssetCostCenterAndDepreciation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CostCenterId",
                table: "FixedAsset",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DepreciationExpenseAccountId",
                table: "FixedAsset",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FixedAssetDepreciation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FixedAssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Period = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CostCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JournalEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JournalEntryNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PostedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PostedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PostedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FixedAssetDepreciation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FixedAssetDepreciation_CostCenter_CostCenterId",
                        column: x => x.CostCenterId,
                        principalTable: "CostCenter",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FixedAssetDepreciation_FixedAsset_FixedAssetId",
                        column: x => x.FixedAssetId,
                        principalTable: "FixedAsset",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FixedAsset_CostCenterId",
                table: "FixedAsset",
                column: "CostCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_FixedAsset_DepreciationExpenseAccountId",
                table: "FixedAsset",
                column: "DepreciationExpenseAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_FixedAssetDepreciation_CostCenterId",
                table: "FixedAssetDepreciation",
                column: "CostCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_FixedAssetDepreciation_FixedAssetId",
                table: "FixedAssetDepreciation",
                column: "FixedAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_FixedAssetDepreciation_TenantId",
                table: "FixedAssetDepreciation",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_FixedAssetDepreciation_TenantId_FixedAssetId_Period",
                table: "FixedAssetDepreciation",
                columns: new[] { "TenantId", "FixedAssetId", "Period" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_FixedAsset_Account_DepreciationExpenseAccountId",
                table: "FixedAsset",
                column: "DepreciationExpenseAccountId",
                principalTable: "Account",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_FixedAsset_CostCenter_CostCenterId",
                table: "FixedAsset",
                column: "CostCenterId",
                principalTable: "CostCenter",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FixedAsset_Account_DepreciationExpenseAccountId",
                table: "FixedAsset");

            migrationBuilder.DropForeignKey(
                name: "FK_FixedAsset_CostCenter_CostCenterId",
                table: "FixedAsset");

            migrationBuilder.DropTable(
                name: "FixedAssetDepreciation");

            migrationBuilder.DropIndex(
                name: "IX_FixedAsset_CostCenterId",
                table: "FixedAsset");

            migrationBuilder.DropIndex(
                name: "IX_FixedAsset_DepreciationExpenseAccountId",
                table: "FixedAsset");

            migrationBuilder.DropColumn(
                name: "CostCenterId",
                table: "FixedAsset");

            migrationBuilder.DropColumn(
                name: "DepreciationExpenseAccountId",
                table: "FixedAsset");
        }
    }
}
