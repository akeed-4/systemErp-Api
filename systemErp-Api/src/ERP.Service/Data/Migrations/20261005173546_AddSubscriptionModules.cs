using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionModules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ModuleKeys",
                table: "Subscription",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "accounting,car_showroom");

            migrationBuilder.AddColumn<string>(
                name: "ModuleKeys",
                table: "PlanDefinition",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "accounting,car_showroom");

            migrationBuilder.UpdateData(
                table: "PlanDefinition",
                keyColumn: "Id",
                keyValue: "Enterprise",
                column: "ModuleKeys",
                value: "accounting,car_showroom");

            migrationBuilder.UpdateData(
                table: "PlanDefinition",
                keyColumn: "Id",
                keyValue: "Professional",
                column: "ModuleKeys",
                value: "accounting,car_showroom");

            migrationBuilder.UpdateData(
                table: "PlanDefinition",
                keyColumn: "Id",
                keyValue: "Starter",
                column: "ModuleKeys",
                value: "accounting,car_showroom");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ModuleKeys",
                table: "Subscription");

            migrationBuilder.DropColumn(
                name: "ModuleKeys",
                table: "PlanDefinition");
        }
    }
}

