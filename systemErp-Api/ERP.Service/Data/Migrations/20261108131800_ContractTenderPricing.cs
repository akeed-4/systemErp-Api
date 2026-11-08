using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class ContractTenderPricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "TenderUnitPrice",
                table: "CommercialContractItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountPercent",
                table: "CommercialContract",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OverheadPercent",
                table: "CommercialContract",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ProfitPercent",
                table: "CommercialContract",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "RiskPercent",
                table: "CommercialContract",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TenderValue",
                table: "CommercialContract",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            // العقود القائمة بلا خصم: سعر العطاء = السعر المتعاقد عليه، وقيمة العطاء = قيمة العقد
            migrationBuilder.Sql("UPDATE [CommercialContractItem] SET [TenderUnitPrice] = [UnitPrice];");
            migrationBuilder.Sql("UPDATE [CommercialContract] SET [TenderValue] = [ContractValue];");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TenderUnitPrice",
                table: "CommercialContractItem");

            migrationBuilder.DropColumn(
                name: "DiscountPercent",
                table: "CommercialContract");

            migrationBuilder.DropColumn(
                name: "OverheadPercent",
                table: "CommercialContract");

            migrationBuilder.DropColumn(
                name: "ProfitPercent",
                table: "CommercialContract");

            migrationBuilder.DropColumn(
                name: "RiskPercent",
                table: "CommercialContract");

            migrationBuilder.DropColumn(
                name: "TenderValue",
                table: "CommercialContract");
        }
    }
}
