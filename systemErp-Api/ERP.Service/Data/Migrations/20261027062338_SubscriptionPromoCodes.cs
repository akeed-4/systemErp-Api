using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class SubscriptionPromoCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DiscountAmount",
                table: "Subscription",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "PromoCode",
                table: "Subscription",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountAmount",
                table: "OnlinePayment",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "PromoCode",
                table: "OnlinePayment",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PromoCode",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    DiscountType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Value = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PlanIds = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    BillingCycle = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ValidFrom = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ValidUntil = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MaxRedemptions = table.Column<int>(type: "int", nullable: true),
                    MaxPerTenant = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromoCode", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PromoCodeRedemption",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PromoCodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubscriptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PlanId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    RedeemedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromoCodeRedemption", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PromoCode_Code",
                table: "PromoCode",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PromoCodeRedemption_PromoCodeId",
                table: "PromoCodeRedemption",
                column: "PromoCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_PromoCodeRedemption_TenantId_PromoCodeId",
                table: "PromoCodeRedemption",
                columns: new[] { "TenantId", "PromoCodeId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PromoCode");

            migrationBuilder.DropTable(
                name: "PromoCodeRedemption");

            migrationBuilder.DropColumn(
                name: "DiscountAmount",
                table: "Subscription");

            migrationBuilder.DropColumn(
                name: "PromoCode",
                table: "Subscription");

            migrationBuilder.DropColumn(
                name: "DiscountAmount",
                table: "OnlinePayment");

            migrationBuilder.DropColumn(
                name: "PromoCode",
                table: "OnlinePayment");
        }
    }
}
