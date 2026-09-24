using System;
using Erp.BuildingBlocks.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Banking.Persistence.Migrations
{
    /// <inheritdoc />
    internal partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "banking");

            migrationBuilder.CreateTable(
                name: "Banks",
                schema: "banking",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SwiftCode = table.Column<string>(type: "varchar(11)", unicode: false, maxLength: 11, nullable: true),
                    IsFinancingInstitution = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Banks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BankAccounts",
                schema: "banking",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    BankId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AccountNumber = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    Iban = table.Column<string>(type: "varchar(34)", unicode: false, maxLength: 34, nullable: true),
                    Branch = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CurrencyCode = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: false),
                    OpeningBalance = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankAccounts_Banks_BankId",
                        column: x => x.BankId,
                        principalSchema: "banking",
                        principalTable: "Banks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankAccounts_TenantId_AccountId",
                schema: "banking",
                table: "BankAccounts",
                columns: new[] { "TenantId", "AccountId" },
                unique: true,
                filter: "[AccountId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BankAccounts_TenantId_BankId",
                schema: "banking",
                table: "BankAccounts",
                columns: new[] { "TenantId", "BankId" });

            migrationBuilder.CreateIndex(
                name: "IX_BankAccounts_TenantId_Code",
                schema: "banking",
                table: "BankAccounts",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_BankAccounts_TenantId_Iban",
                schema: "banking",
                table: "BankAccounts",
                columns: new[] { "TenantId", "Iban" },
                unique: true,
                filter: "[Iban] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Banks_TenantId_Code",
                schema: "banking",
                table: "Banks",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            // Cross-module references (§10.1); the principal modules are migrated first (§17.2).
            migrationBuilder.AddCrossModuleForeignKey("banking", "BankAccounts", "AccountId", "accounting", "Accounts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BankAccounts",
                schema: "banking");

            migrationBuilder.DropTable(
                name: "Banks",
                schema: "banking");
        }
    }
}
