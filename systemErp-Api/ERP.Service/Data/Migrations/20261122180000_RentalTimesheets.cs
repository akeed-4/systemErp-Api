using System;
using ERP.Service.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <summary>
    /// كشوف ساعات التأجير (T1): طريقة فوترة العقد (العقود القائمة تبقى «بالمدة»)، وشروط كشف الساعات على السطر (الحد الأدنى اليومي، وأيام العمل
    /// في الفترة، ونسبة الانتظار)، وإعداد الطريقة الافتراضية وشرط صورة الكشف، وجدولا الكشف وأيامه.
    /// </summary>
    [DbContext(typeof(ErpDbContext))]
    [Migration("20261122180000_RentalTimesheets")]
    public partial class RentalTimesheets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "BillingMode", table: "RentalContract", type: "nvarchar(16)", maxLength: 16, nullable: false, defaultValue: "time");
            migrationBuilder.AddColumn<decimal>(name: "DailyMinimumHours", table: "RentalContractLine", type: "decimal(18,4)", precision: 18, scale: 4, nullable: false, defaultValue: 0m);
            migrationBuilder.AddColumn<decimal>(name: "WorkingDaysPerPeriod", table: "RentalContractLine", type: "decimal(18,4)", precision: 18, scale: 4, nullable: false, defaultValue: 0m);
            migrationBuilder.AddColumn<decimal>(name: "StandbyPercent", table: "RentalContractLine", type: "decimal(18,4)", precision: 18, scale: 4, nullable: false, defaultValue: 100m);
            migrationBuilder.AddColumn<string>(name: "DefaultBillingMode", table: "RentalSettings", type: "nvarchar(16)", maxLength: 16, nullable: false, defaultValue: "timesheet");
            migrationBuilder.AddColumn<bool>(name: "RequireTimesheetPhoto", table: "RentalSettings", type: "bit", nullable: false, defaultValue: true);

            migrationBuilder.CreateTable(
                name: "RentalTimesheet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TimesheetNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EquipmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PeriodFrom = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PeriodTo = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    OperatorEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClientApproverName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ClientApproverTitle = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    ClientSignature = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClientApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    WorkDays = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    StandbyDays = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    BreakdownDays = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    WorkedHours = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    StandbyHours = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    BreakdownHours = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    BilledQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    OvertimeHours = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    RentAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    StandbyAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    OvertimeAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    OperatorAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    BillingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RejectReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SubmittedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RentalTimesheet", x => x.Id);
                    table.ForeignKey(name: "FK_RentalTimesheet_Employee_OperatorEmployeeId", column: x => x.OperatorEmployeeId, principalTable: "Employee",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_RentalTimesheet_Equipment_EquipmentId", column: x => x.EquipmentId, principalTable: "Equipment",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_RentalTimesheet_RentalBilling_BillingId", column: x => x.BillingId, principalTable: "RentalBilling",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_RentalTimesheet_RentalContractLine_LineId", column: x => x.LineId, principalTable: "RentalContractLine",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_RentalTimesheet_RentalContract_ContractId", column: x => x.ContractId, principalTable: "RentalContract",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RentalTimesheetDay",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TimesheetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    WorkedHours = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    StandbyHours = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    BreakdownHours = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    MeterStart = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    MeterEnd = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RentalTimesheetDay", x => x.Id);
                    table.ForeignKey(name: "FK_RentalTimesheetDay_RentalTimesheet_TimesheetId", column: x => x.TimesheetId, principalTable: "RentalTimesheet",
                        principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(name: "IX_RentalTimesheet_BillingId", table: "RentalTimesheet", column: "BillingId");
            migrationBuilder.CreateIndex(name: "IX_RentalTimesheet_ContractId", table: "RentalTimesheet", column: "ContractId");
            migrationBuilder.CreateIndex(name: "IX_RentalTimesheet_EquipmentId", table: "RentalTimesheet", column: "EquipmentId");
            migrationBuilder.CreateIndex(name: "IX_RentalTimesheet_LineId", table: "RentalTimesheet", column: "LineId");
            migrationBuilder.CreateIndex(name: "IX_RentalTimesheet_OperatorEmployeeId", table: "RentalTimesheet", column: "OperatorEmployeeId");
            migrationBuilder.CreateIndex(name: "IX_RentalTimesheet_TenantId", table: "RentalTimesheet", column: "TenantId");
            migrationBuilder.CreateIndex(name: "IX_RentalTimesheet_TenantId_ContractId_Status", table: "RentalTimesheet", columns: new[] { "TenantId", "ContractId", "Status" });
            migrationBuilder.CreateIndex(name: "IX_RentalTimesheet_TenantId_LineId_PeriodFrom_PeriodTo", table: "RentalTimesheet",
                columns: new[] { "TenantId", "LineId", "PeriodFrom", "PeriodTo" });
            migrationBuilder.CreateIndex(name: "IX_RentalTimesheet_TenantId_TimesheetNumber", table: "RentalTimesheet", columns: new[] { "TenantId", "TimesheetNumber" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_RentalTimesheetDay_TenantId", table: "RentalTimesheetDay", column: "TenantId");
            migrationBuilder.CreateIndex(name: "IX_RentalTimesheetDay_TimesheetId", table: "RentalTimesheetDay", column: "TimesheetId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "RentalTimesheetDay");
            migrationBuilder.DropTable(name: "RentalTimesheet");
            migrationBuilder.DropColumn(name: "BillingMode", table: "RentalContract");
            migrationBuilder.DropColumn(name: "DailyMinimumHours", table: "RentalContractLine");
            migrationBuilder.DropColumn(name: "WorkingDaysPerPeriod", table: "RentalContractLine");
            migrationBuilder.DropColumn(name: "StandbyPercent", table: "RentalContractLine");
            migrationBuilder.DropColumn(name: "DefaultBillingMode", table: "RentalSettings");
            migrationBuilder.DropColumn(name: "RequireTimesheetPhoto", table: "RentalSettings");
        }
    }
}
