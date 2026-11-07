using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class ConstructionResourceCostSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CostSource",
                table: "ConstructionResource",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "manual");

            // الموارد التي كانت «التكلفة من الصنف» تأخذ المصدر product قبل حذف العمود القديم
            migrationBuilder.Sql("UPDATE [ConstructionResource] SET [CostSource] = N'product' WHERE [UseProductCost] = 1");

            migrationBuilder.DropColumn(
                name: "UseProductCost",
                table: "ConstructionResource");

            migrationBuilder.AddColumn<decimal>(
                name: "HoursPerUnit",
                table: "ConstructionResource",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<Guid>(
                name: "HrDepartmentId",
                table: "ConstructionResource",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HrEmployeeCount",
                table: "ConstructionResource",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "HrJobTitle",
                table: "ConstructionResource",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConstructionResource_HrDepartmentId",
                table: "ConstructionResource",
                column: "HrDepartmentId");

            migrationBuilder.AddForeignKey(
                name: "FK_ConstructionResource_Department_HrDepartmentId",
                table: "ConstructionResource",
                column: "HrDepartmentId",
                principalTable: "Department",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ConstructionResource_Department_HrDepartmentId",
                table: "ConstructionResource");

            migrationBuilder.DropIndex(
                name: "IX_ConstructionResource_HrDepartmentId",
                table: "ConstructionResource");

            migrationBuilder.DropColumn(
                name: "CostSource",
                table: "ConstructionResource");

            migrationBuilder.DropColumn(
                name: "HoursPerUnit",
                table: "ConstructionResource");

            migrationBuilder.DropColumn(
                name: "HrDepartmentId",
                table: "ConstructionResource");

            migrationBuilder.DropColumn(
                name: "HrEmployeeCount",
                table: "ConstructionResource");

            migrationBuilder.DropColumn(
                name: "HrJobTitle",
                table: "ConstructionResource");

            migrationBuilder.AddColumn<bool>(
                name: "UseProductCost",
                table: "ConstructionResource",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }
    }
}
