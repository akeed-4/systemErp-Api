using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHrEmployeeProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "Employee",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankName",
                table: "Employee",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BirthDate",
                table: "Employee",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ContractEndDate",
                table: "Employee",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ContractStartDate",
                table: "Employee",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContractType",
                table: "Employee",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "unlimited");

            migrationBuilder.AddColumn<Guid>(
                name: "DepartmentId",
                table: "Employee",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Employee",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Gender",
                table: "Employee",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GosiNumber",
                table: "Employee",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "IdExpiryDate",
                table: "Employee",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdType",
                table: "Employee",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "national_id");

            migrationBuilder.AddColumn<string>(
                name: "Nationality",
                table: "Employee",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "Employee",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PassportExpiryDate",
                table: "Employee",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PassportNumber",
                table: "Employee",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "Employee",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProbationEndDate",
                table: "Employee",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TerminationDate",
                table: "Employee",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TerminationReason",
                table: "Employee",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Department",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ManagerEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Department", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Employee_DepartmentId",
                table: "Employee",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Department_TenantId",
                table: "Department",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Department_TenantId_Code",
                table: "Department",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Employee_Department_DepartmentId",
                table: "Employee",
                column: "DepartmentId",
                principalTable: "Department",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Employee_Department_DepartmentId",
                table: "Employee");

            migrationBuilder.DropTable(
                name: "Department");

            migrationBuilder.DropIndex(
                name: "IX_Employee_DepartmentId",
                table: "Employee");

            migrationBuilder.DropColumn(
                name: "Address",
                table: "Employee");

            migrationBuilder.DropColumn(
                name: "BankName",
                table: "Employee");

            migrationBuilder.DropColumn(
                name: "BirthDate",
                table: "Employee");

            migrationBuilder.DropColumn(
                name: "ContractEndDate",
                table: "Employee");

            migrationBuilder.DropColumn(
                name: "ContractStartDate",
                table: "Employee");

            migrationBuilder.DropColumn(
                name: "ContractType",
                table: "Employee");

            migrationBuilder.DropColumn(
                name: "DepartmentId",
                table: "Employee");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "Employee");

            migrationBuilder.DropColumn(
                name: "Gender",
                table: "Employee");

            migrationBuilder.DropColumn(
                name: "GosiNumber",
                table: "Employee");

            migrationBuilder.DropColumn(
                name: "IdExpiryDate",
                table: "Employee");

            migrationBuilder.DropColumn(
                name: "IdType",
                table: "Employee");

            migrationBuilder.DropColumn(
                name: "Nationality",
                table: "Employee");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "Employee");

            migrationBuilder.DropColumn(
                name: "PassportExpiryDate",
                table: "Employee");

            migrationBuilder.DropColumn(
                name: "PassportNumber",
                table: "Employee");

            migrationBuilder.DropColumn(
                name: "Phone",
                table: "Employee");

            migrationBuilder.DropColumn(
                name: "ProbationEndDate",
                table: "Employee");

            migrationBuilder.DropColumn(
                name: "TerminationDate",
                table: "Employee");

            migrationBuilder.DropColumn(
                name: "TerminationReason",
                table: "Employee");
        }
    }
}
