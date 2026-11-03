using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class PosPromotionSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PosPromotionSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AllowManualDiscount = table.Column<bool>(type: "bit", nullable: false),
                    MaxLineDiscountPercent = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    MaxInvoiceDiscountPercent = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    AllowCoupons = table.Column<bool>(type: "bit", nullable: false),
                    AllowCouponWithOffers = table.Column<bool>(type: "bit", nullable: false),
                    AllowLoyaltyRedemption = table.Column<bool>(type: "bit", nullable: false),
                    LoyaltyEnabled = table.Column<bool>(type: "bit", nullable: false),
                    SarPerEarnedPoint = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PointValueSar = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    MinPointsToRedeem = table.Column<int>(type: "int", nullable: false),
                    MaxRedeemPercent = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    SilverThreshold = table.Column<int>(type: "int", nullable: false),
                    GoldThreshold = table.Column<int>(type: "int", nullable: false),
                    PlatinumThreshold = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PosPromotionSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PosPromotionSettings_TenantId",
                table: "PosPromotionSettings",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PosPromotionSettings");
        }
    }
}
