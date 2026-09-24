using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Catalog.Persistence.Migrations
{
    /// <inheritdoc />
    internal partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "catalog");

            migrationBuilder.CreateTable(
                name: "Roles",
                schema: "catalog",
                columns: table => new
                {
                    Code = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "Screens",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(60)", unicode: false, maxLength: 60, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Screens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionPlans",
                schema: "catalog",
                columns: table => new
                {
                    Code = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DescriptionAr = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    DescriptionEn = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    PriceMonthly = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PriceYearly = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    IsPopular = table.Column<bool>(type: "bit", nullable: false),
                    BadgeAr = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BadgeEn = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    MaxUsers = table.Column<int>(type: "int", nullable: true),
                    MaxInvoicesPerMonth = table.Column<int>(type: "int", nullable: true),
                    MaxBranches = table.Column<int>(type: "int", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionPlans", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "Tenants",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    TenancyMode = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    ConnectionStringEncrypted = table.Column<string>(type: "varchar(4000)", unicode: false, maxLength: 4000, nullable: true),
                    DatabaseName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    SchemaVersion = table.Column<string>(type: "varchar(2000)", unicode: false, maxLength: 2000, nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionPlanFeatures",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlanCode = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    FeatureAr = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    FeatureEn = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionPlanFeatures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionPlanFeatures_SubscriptionPlans_PlanCode",
                        column: x => x.PlanCode,
                        principalSchema: "catalog",
                        principalTable: "SubscriptionPlans",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TenantLoginIndex",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedPhone = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantLoginIndex", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantLoginIndex_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "catalog",
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TenantSubscriptions",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanCode = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    BillingCycle = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    PaidAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PaymentMethod = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    TransactionReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AutoRenew = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantSubscriptions_SubscriptionPlans_PlanCode",
                        column: x => x.PlanCode,
                        principalSchema: "catalog",
                        principalTable: "SubscriptionPlans",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TenantSubscriptions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "catalog",
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPlanFeatures_PlanCode",
                schema: "catalog",
                table: "SubscriptionPlanFeatures",
                column: "PlanCode");

            migrationBuilder.CreateIndex(
                name: "IX_TenantLoginIndex_NormalizedEmail",
                schema: "catalog",
                table: "TenantLoginIndex",
                column: "NormalizedEmail",
                filter: "[NormalizedEmail] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TenantLoginIndex_NormalizedPhone",
                schema: "catalog",
                table: "TenantLoginIndex",
                column: "NormalizedPhone",
                filter: "[NormalizedPhone] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TenantLoginIndex_TenantId_UserId",
                schema: "catalog",
                table: "TenantLoginIndex",
                columns: new[] { "TenantId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantLoginIndex_UserId",
                schema: "catalog",
                table: "TenantLoginIndex",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Code",
                schema: "catalog",
                table: "Tenants",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_TenancyMode",
                schema: "catalog",
                table: "Tenants",
                column: "TenancyMode");

            migrationBuilder.CreateIndex(
                name: "IX_TenantSubscriptions_PlanCode",
                schema: "catalog",
                table: "TenantSubscriptions",
                column: "PlanCode");

            migrationBuilder.CreateIndex(
                name: "IX_TenantSubscriptions_TenantId",
                schema: "catalog",
                table: "TenantSubscriptions",
                column: "TenantId",
                unique: true,
                filter: "[Status] IN ('active','trial')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Roles",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "Screens",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "SubscriptionPlanFeatures",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "TenantLoginIndex",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "TenantSubscriptions",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "SubscriptionPlans",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "Tenants",
                schema: "catalog");
        }
    }
}
