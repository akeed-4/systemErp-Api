using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Settings.Persistence.Migrations
{
    /// <inheritdoc />
    internal partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "settings");

            migrationBuilder.CreateTable(
                name: "Currencies",
                schema: "settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    IsBaseCurrency = table.Column<bool>(type: "bit", nullable: false),
                    ExchangeRate = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    DecimalPlaces = table.Column<int>(type: "int", nullable: false),
                    LastUpdated = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Currencies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DocumentSequences",
                schema: "settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentType = table.Column<string>(type: "varchar(60)", unicode: false, maxLength: 60, nullable: false),
                    ScopeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Prefix = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    NextNumber = table.Column<long>(type: "bigint", nullable: false),
                    Padding = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentSequences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TenantSettings",
                schema: "settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefaultLanguage = table.Column<string>(type: "varchar(5)", unicode: false, maxLength: 5, nullable: false),
                    DefaultVatRate = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    IsCarShowroomEnabled = table.Column<bool>(type: "bit", nullable: false),
                    IsPosEnabled = table.Column<bool>(type: "bit", nullable: false),
                    IsGeneralTradingEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Currencies_TenantId_Code",
                schema: "settings",
                table: "Currencies",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Currencies_TenantId_IsBaseCurrency",
                schema: "settings",
                table: "Currencies",
                columns: new[] { "TenantId", "IsBaseCurrency" },
                unique: true,
                filter: "[IsBaseCurrency] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentSequences_TenantId_DocumentType_ScopeId_Year",
                schema: "settings",
                table: "DocumentSequences",
                columns: new[] { "TenantId", "DocumentType", "ScopeId", "Year" },
                unique: true,
                filter: "[ScopeId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TenantSettings_TenantId",
                schema: "settings",
                table: "TenantSettings",
                column: "TenantId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Currencies",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "DocumentSequences",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "TenantSettings",
                schema: "settings");
        }
    }
}
