using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class FieldAttendanceExceptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OutOfRangePolicy",
                table: "ConstructionProjectProfile",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "reject");

            migrationBuilder.AddColumn<string>(
                name: "GpsStatus",
                table: "AttendanceRecord",
                type: "nvarchar(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExceptionReason",
                table: "AttendanceGpsPunch",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "OutsideGeofence",
                table: "AttendanceGpsPunch",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ReviewNote",
                table: "AttendanceGpsPunch",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewStatus",
                table: "AttendanceGpsPunch",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAt",
                table: "AttendanceGpsPunch",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewedBy",
                table: "AttendanceGpsPunch",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReviewedByUserId",
                table: "AttendanceGpsPunch",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceGpsPunch_TenantId_ContractId_ReviewStatus",
                table: "AttendanceGpsPunch",
                columns: new[] { "TenantId", "ContractId", "ReviewStatus" });

            // الحضور الميداني السابق كان داخل النطاق دائماً (لم يكن يُقبل غيره)
            migrationBuilder.Sql("UPDATE AttendanceRecord SET GpsStatus = 'valid' WHERE Source = 'mobile_gps' AND GpsStatus IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AttendanceGpsPunch_TenantId_ContractId_ReviewStatus",
                table: "AttendanceGpsPunch");

            migrationBuilder.DropColumn(
                name: "OutOfRangePolicy",
                table: "ConstructionProjectProfile");

            migrationBuilder.DropColumn(
                name: "GpsStatus",
                table: "AttendanceRecord");

            migrationBuilder.DropColumn(
                name: "ExceptionReason",
                table: "AttendanceGpsPunch");

            migrationBuilder.DropColumn(
                name: "OutsideGeofence",
                table: "AttendanceGpsPunch");

            migrationBuilder.DropColumn(
                name: "ReviewNote",
                table: "AttendanceGpsPunch");

            migrationBuilder.DropColumn(
                name: "ReviewStatus",
                table: "AttendanceGpsPunch");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "AttendanceGpsPunch");

            migrationBuilder.DropColumn(
                name: "ReviewedBy",
                table: "AttendanceGpsPunch");

            migrationBuilder.DropColumn(
                name: "ReviewedByUserId",
                table: "AttendanceGpsPunch");
        }
    }
}
