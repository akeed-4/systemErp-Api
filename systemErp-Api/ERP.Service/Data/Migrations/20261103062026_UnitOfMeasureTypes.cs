using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class UnitOfMeasureTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "ConversionFactor",
                table: "UnitOfMeasure",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AddColumn<string>(
                name: "UnitType",
                table: "UnitOfMeasure",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "count");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UnitType",
                table: "UnitOfMeasure");

            migrationBuilder.AlterColumn<decimal>(
                name: "ConversionFactor",
                table: "UnitOfMeasure",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,8)",
                oldPrecision: 18,
                oldScale: 8);
        }
    }
}
