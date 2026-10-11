using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPartyIdentification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IdNumber",
                table: "Supplier",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdType",
                table: "Supplier",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PartyIdNumber",
                table: "Invoice",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PartyIdType",
                table: "Invoice",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdNumber",
                table: "Customer",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdType",
                table: "Customer",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CrNumber",
                table: "BankEntity",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "BankEntity",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdNumber",
                table: "BankEntity",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdType",
                table: "BankEntity",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "BankEntity",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VatNumber",
                table: "BankEntity",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IdNumber",
                table: "Supplier");

            migrationBuilder.DropColumn(
                name: "IdType",
                table: "Supplier");

            migrationBuilder.DropColumn(
                name: "PartyIdNumber",
                table: "Invoice");

            migrationBuilder.DropColumn(
                name: "PartyIdType",
                table: "Invoice");

            migrationBuilder.DropColumn(
                name: "IdNumber",
                table: "Customer");

            migrationBuilder.DropColumn(
                name: "IdType",
                table: "Customer");

            migrationBuilder.DropColumn(
                name: "CrNumber",
                table: "BankEntity");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "BankEntity");

            migrationBuilder.DropColumn(
                name: "IdNumber",
                table: "BankEntity");

            migrationBuilder.DropColumn(
                name: "IdType",
                table: "BankEntity");

            migrationBuilder.DropColumn(
                name: "Phone",
                table: "BankEntity");

            migrationBuilder.DropColumn(
                name: "VatNumber",
                table: "BankEntity");
        }
    }
}
