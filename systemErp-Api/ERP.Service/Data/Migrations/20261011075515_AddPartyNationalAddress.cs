using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPartyNationalAddress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AdditionalNo",
                table: "Supplier",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuildingNo",
                table: "Supplier",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Country",
                table: "Supplier",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "SA");

            migrationBuilder.AddColumn<string>(
                name: "District",
                table: "Supplier",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostalCode",
                table: "Supplier",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Street",
                table: "Supplier",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Country",
                table: "Customer",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "SA");

            migrationBuilder.AddColumn<string>(
                name: "AdditionalNo",
                table: "BankEntity",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuildingNo",
                table: "BankEntity",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "BankEntity",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Country",
                table: "BankEntity",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "SA");

            migrationBuilder.AddColumn<string>(
                name: "District",
                table: "BankEntity",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostalCode",
                table: "BankEntity",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Street",
                table: "BankEntity",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdditionalNo",
                table: "Supplier");

            migrationBuilder.DropColumn(
                name: "BuildingNo",
                table: "Supplier");

            migrationBuilder.DropColumn(
                name: "Country",
                table: "Supplier");

            migrationBuilder.DropColumn(
                name: "District",
                table: "Supplier");

            migrationBuilder.DropColumn(
                name: "PostalCode",
                table: "Supplier");

            migrationBuilder.DropColumn(
                name: "Street",
                table: "Supplier");

            migrationBuilder.DropColumn(
                name: "Country",
                table: "Customer");

            migrationBuilder.DropColumn(
                name: "AdditionalNo",
                table: "BankEntity");

            migrationBuilder.DropColumn(
                name: "BuildingNo",
                table: "BankEntity");

            migrationBuilder.DropColumn(
                name: "City",
                table: "BankEntity");

            migrationBuilder.DropColumn(
                name: "Country",
                table: "BankEntity");

            migrationBuilder.DropColumn(
                name: "District",
                table: "BankEntity");

            migrationBuilder.DropColumn(
                name: "PostalCode",
                table: "BankEntity");

            migrationBuilder.DropColumn(
                name: "Street",
                table: "BankEntity");
        }
    }
}
