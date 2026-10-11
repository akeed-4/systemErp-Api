using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVatExemptionReasonAndBuyerAddress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VatExemptionReasonCode",
                table: "InvoiceItem",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PartyAdditionalNo",
                table: "Invoice",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PartyBuildingNo",
                table: "Invoice",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PartyCity",
                table: "Invoice",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PartyCountry",
                table: "Invoice",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PartyDistrict",
                table: "Invoice",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PartyPostalCode",
                table: "Invoice",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PartyStreet",
                table: "Invoice",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VatExemptionReasonCode",
                table: "InvoiceItem");

            migrationBuilder.DropColumn(
                name: "PartyAdditionalNo",
                table: "Invoice");

            migrationBuilder.DropColumn(
                name: "PartyBuildingNo",
                table: "Invoice");

            migrationBuilder.DropColumn(
                name: "PartyCity",
                table: "Invoice");

            migrationBuilder.DropColumn(
                name: "PartyCountry",
                table: "Invoice");

            migrationBuilder.DropColumn(
                name: "PartyDistrict",
                table: "Invoice");

            migrationBuilder.DropColumn(
                name: "PartyPostalCode",
                table: "Invoice");

            migrationBuilder.DropColumn(
                name: "PartyStreet",
                table: "Invoice");
        }
    }
}
