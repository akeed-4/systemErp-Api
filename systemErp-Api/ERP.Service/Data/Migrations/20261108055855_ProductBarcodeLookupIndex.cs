using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProductBarcodeLookupIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BarcodeLookupKey",
                table: "Product",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true,
                computedColumnSql: "CONVERT(nvarchar(450), LEFT([Barcode], 450))",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "IX_Product_TenantId_BarcodeLookupKey",
                table: "Product",
                columns: new[] { "TenantId", "BarcodeLookupKey" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Product_TenantId_BarcodeLookupKey",
                table: "Product");

            migrationBuilder.DropColumn(
                name: "BarcodeLookupKey",
                table: "Product");
        }
    }
}
