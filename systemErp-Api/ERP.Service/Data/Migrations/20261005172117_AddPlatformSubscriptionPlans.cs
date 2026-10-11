using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformSubscriptionPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlanDefinition",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PriceMonthly = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PriceYearly = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    MaxUsers = table.Column<int>(type: "int", nullable: true),
                    MaxInvoicesPerMonth = table.Column<int>(type: "int", nullable: true),
                    Branches = table.Column<int>(type: "int", nullable: true),
                    ZatcaPhase2Enabled = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanDefinition", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "PlanDefinition",
                columns: new[] { "Id", "Branches", "IsActive", "MaxInvoicesPerMonth", "MaxUsers", "NameAr", "NameEn", "PriceMonthly", "PriceYearly", "SortOrder", "UpdatedAt", "ZatcaPhase2Enabled" },
                values: new object[,]
                {
                    { "Enterprise", null, true, null, null, "باقة المجموعات والمؤسسات (Enterprise)", "Enterprise Corporate Plan", 999m, 9990m, 3, null, true },
                    { "Professional", 3, true, null, 10, "باقة الشركات المتقدمة (Professional)", "Professional Business Plan", 499m, 4990m, 2, null, true },
                    { "Starter", 1, true, 500, 2, "باقة البداية (Starter)", "Starter Plan", 199m, 1990m, 1, null, false }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlanDefinition");
        }
    }
}
