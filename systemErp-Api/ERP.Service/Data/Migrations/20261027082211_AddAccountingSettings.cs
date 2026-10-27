using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountingSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccountingSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InventoryMethod = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    InventoryMethodEffectiveFrom = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PreviousInventoryMethod = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InventoryAccountCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CogsAccountCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InventoryShortageAccountCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InventorySurplusAccountCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PurchasesAccountCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PurchaseReturnsAccountCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OpeningInventoryAccountCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EndingInventoryAccountCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SalesRevenueAccountCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SalesReturnsAccountCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OutputVatAccountCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InputVatAccountCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountingSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountingSettings_TenantId",
                table: "AccountingSettings",
                column: "TenantId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountingSettings");
        }
    }
}
