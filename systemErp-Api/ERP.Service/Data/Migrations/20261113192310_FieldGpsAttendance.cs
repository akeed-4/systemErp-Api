using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class FieldGpsAttendance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "Tenant",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "Asia/Riyadh");

            migrationBuilder.AddColumn<int>(
                name: "GeofenceRadiusMeters",
                table: "ConstructionProjectProfile",
                type: "int",
                nullable: false,
                defaultValue: 200);

            migrationBuilder.AddColumn<bool>(
                name: "GpsAttendanceEnabled",
                table: "ConstructionProjectProfile",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MaxGpsAccuracyMeters",
                table: "ConstructionProjectProfile",
                type: "int",
                nullable: false,
                defaultValue: 50);

            migrationBuilder.AddColumn<decimal>(
                name: "SiteLatitude",
                table: "ConstructionProjectProfile",
                type: "decimal(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SiteLongitude",
                table: "ConstructionProjectProfile",
                type: "decimal(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CheckInAt",
                table: "AttendanceRecord",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CheckOutAt",
                table: "AttendanceRecord",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ContractId",
                table: "AttendanceRecord",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "AttendanceRecord",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "manual");

            migrationBuilder.AddColumn<int>(
                name: "WorkedMinutes",
                table: "AttendanceRecord",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AttendanceGpsPunch",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttendanceRecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DeviceCapturedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Latitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: false),
                    Longitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: false),
                    AccuracyMeters = table.Column<decimal>(type: "decimal(9,1)", precision: 9, scale: 1, nullable: false),
                    DistanceMeters = table.Column<decimal>(type: "decimal(12,1)", precision: 12, scale: 1, nullable: false),
                    GeofenceRadiusMeters = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceGpsPunch", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceGpsPunch_AttendanceRecord_AttendanceRecordId",
                        column: x => x.AttendanceRecordId,
                        principalTable: "AttendanceRecord",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AttendanceGpsPunch_CommercialContract_ContractId",
                        column: x => x.ContractId,
                        principalTable: "CommercialContract",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecord_ContractId",
                table: "AttendanceRecord",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecord_TenantId_ContractId_Date",
                table: "AttendanceRecord",
                columns: new[] { "TenantId", "ContractId", "Date" });

            // سجل واحد للموظف في اليوم: نسخة مكررة سابقة (من حفظ متزامن) يُبقى منها أحدثها تعديلاً قبل الفهرس الفريد.
            // التكرار كان يُفشل كشف اليوم أصلاً (يقرأ السجلات بمفتاح الموظف)، فلا تُفقد قيمة معروضة.
            migrationBuilder.Sql(@"
WITH ranked AS (
    SELECT Id, ROW_NUMBER() OVER (PARTITION BY TenantId, EmployeeId, [Date]
                                  ORDER BY COALESCE(UpdatedAt, CreatedAt) DESC, CreatedAt DESC, Id) AS rn
    FROM AttendanceRecord)
DELETE FROM ranked WHERE rn > 1;");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecord_TenantId_EmployeeId_Date",
                table: "AttendanceRecord",
                columns: new[] { "TenantId", "EmployeeId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceGpsPunch_AttendanceRecordId_Kind",
                table: "AttendanceGpsPunch",
                columns: new[] { "AttendanceRecordId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceGpsPunch_ContractId",
                table: "AttendanceGpsPunch",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceGpsPunch_TenantId",
                table: "AttendanceGpsPunch",
                column: "TenantId");

            migrationBuilder.AddForeignKey(
                name: "FK_AttendanceRecord_CommercialContract_ContractId",
                table: "AttendanceRecord",
                column: "ContractId",
                principalTable: "CommercialContract",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AttendanceRecord_CommercialContract_ContractId",
                table: "AttendanceRecord");

            migrationBuilder.DropTable(
                name: "AttendanceGpsPunch");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceRecord_ContractId",
                table: "AttendanceRecord");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceRecord_TenantId_ContractId_Date",
                table: "AttendanceRecord");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceRecord_TenantId_EmployeeId_Date",
                table: "AttendanceRecord");

            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "Tenant");

            migrationBuilder.DropColumn(
                name: "GeofenceRadiusMeters",
                table: "ConstructionProjectProfile");

            migrationBuilder.DropColumn(
                name: "GpsAttendanceEnabled",
                table: "ConstructionProjectProfile");

            migrationBuilder.DropColumn(
                name: "MaxGpsAccuracyMeters",
                table: "ConstructionProjectProfile");

            migrationBuilder.DropColumn(
                name: "SiteLatitude",
                table: "ConstructionProjectProfile");

            migrationBuilder.DropColumn(
                name: "SiteLongitude",
                table: "ConstructionProjectProfile");

            migrationBuilder.DropColumn(
                name: "CheckInAt",
                table: "AttendanceRecord");

            migrationBuilder.DropColumn(
                name: "CheckOutAt",
                table: "AttendanceRecord");

            migrationBuilder.DropColumn(
                name: "ContractId",
                table: "AttendanceRecord");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "AttendanceRecord");

            migrationBuilder.DropColumn(
                name: "WorkedMinutes",
                table: "AttendanceRecord");
        }
    }
}
