using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentNumberingSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "NumberSequence",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "Mode",
                table: "NumberSequence",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "auto");

            migrationBuilder.AddColumn<bool>(
                name: "ResetYearly",
                table: "NumberSequence",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Scope",
                table: "NumberSequence",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "NumberSequence");

            migrationBuilder.DropColumn(
                name: "Mode",
                table: "NumberSequence");

            migrationBuilder.DropColumn(
                name: "ResetYearly",
                table: "NumberSequence");

            migrationBuilder.DropColumn(
                name: "Scope",
                table: "NumberSequence");
        }
    }
}
