using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformAnnouncements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlatformAnnouncement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TitleAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TitleEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BodyAr = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    BodyEn = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    BadgeAr = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    BadgeEn = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    CtaLabelAr = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    CtaLabelEn = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    CtaUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Icon = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Style = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Display = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Audience = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    PlanIds = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    TenantIds = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Frequency = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    StartsAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EndsAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformAnnouncement", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "PlatformAnnouncement",
                columns: new[] { "Id", "Audience", "BadgeAr", "BadgeEn", "BodyAr", "BodyEn", "CreatedAt", "CtaLabelAr", "CtaLabelEn", "CtaUrl", "Display", "EndsAt", "Frequency", "Icon", "IsActive", "PlanIds", "Priority", "StartsAt", "Style", "TenantIds", "TitleAr", "TitleEn", "UpdatedAt" },
                values: new object[] { new Guid("8f4b1c2e-6a3d-4e57-9b21-3c5d7e9f0a14"), "trial", "اكتشف نمو", "DISCOVER NUMO", "جرّب إدارة حساباتك وفواتيرك وتقاريرك في مكان واحد، واكتشف كيف يساعدك نمو على تنظيم أعمال منشأتك.", "Manage your accounts, invoices, and reports in one place. Discover how Numo can help organize your business.", new DateTime(2026, 11, 4, 0, 0, 0, 0, DateTimeKind.Utc), "اكتشف الباقات", "Explore plans", "/subscription", "popup", null, "every_login", "campaign", true, null, 0, null, "promo", null, "استفد من الفترة المجانية لتجربة نمو", "Try Numo during your free trial", null });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformAnnouncement_IsActive_StartsAt_EndsAt",
                table: "PlatformAnnouncement",
                columns: new[] { "IsActive", "StartsAt", "EndsAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlatformAnnouncement");
        }
    }
}
