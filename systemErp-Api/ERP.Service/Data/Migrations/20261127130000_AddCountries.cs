using System;
using ERP.Service.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <summary>قائمة دول المنشأة (تُعبّأ بدول العالم عند أول طلب للقائمة من CountryService، لا في الترحيل).</summary>
    [DbContext(typeof(ErpDbContext))]
    [Migration("20261127130000_AddCountries")]
    public partial class AddCountries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Country",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    Code3 = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                    NameAr = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    NationalityAr = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    NationalityEn = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DialCode = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: true),
                    CurrencyCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Country", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Country_TenantId",
                table: "Country",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Country_TenantId_Code",
                table: "Country",
                columns: new[] { "TenantId", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Country");
        }
    }
}
