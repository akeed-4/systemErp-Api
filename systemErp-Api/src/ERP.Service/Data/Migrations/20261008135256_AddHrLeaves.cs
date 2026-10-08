using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHrLeaves : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "LeaveDeduction",
                table: "PayrollLine",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "UnpaidLeaveDays",
                table: "PayrollLine",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "AnnualLeaveDays",
                table: "Employee",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OpeningLeaveBalance",
                table: "Employee",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "OpeningLeaveBalanceDate",
                table: "Employee",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LeaveType",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsAnnual = table.Column<bool>(type: "bit", nullable: false),
                    PayPercent = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    MaxDaysPerYear = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveType", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LeaveRequest",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestNumber = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EmployeeName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LeaveTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeaveTypeName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsAnnual = table.Column<bool>(type: "bit", nullable: false),
                    PayPercent = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Days = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DecidedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveRequest", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeaveRequest_Employee_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employee",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveRequest_LeaveType_LeaveTypeId",
                        column: x => x.LeaveTypeId,
                        principalTable: "LeaveType",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequest_EmployeeId",
                table: "LeaveRequest",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequest_LeaveTypeId",
                table: "LeaveRequest",
                column: "LeaveTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequest_TenantId",
                table: "LeaveRequest",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequest_TenantId_RequestNumber",
                table: "LeaveRequest",
                columns: new[] { "TenantId", "RequestNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeaveType_TenantId",
                table: "LeaveType",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveType_TenantId_Code",
                table: "LeaveType",
                columns: new[] { "TenantId", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LeaveRequest");

            migrationBuilder.DropTable(
                name: "LeaveType");

            migrationBuilder.DropColumn(
                name: "LeaveDeduction",
                table: "PayrollLine");

            migrationBuilder.DropColumn(
                name: "UnpaidLeaveDays",
                table: "PayrollLine");

            migrationBuilder.DropColumn(
                name: "AnnualLeaveDays",
                table: "Employee");

            migrationBuilder.DropColumn(
                name: "OpeningLeaveBalance",
                table: "Employee");

            migrationBuilder.DropColumn(
                name: "OpeningLeaveBalanceDate",
                table: "Employee");
        }
    }
}
