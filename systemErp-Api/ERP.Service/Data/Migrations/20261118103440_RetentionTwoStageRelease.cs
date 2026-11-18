using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class RetentionTwoStageRelease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FinalAcceptanceDate",
                table: "CommercialContract",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "InitialAcceptanceDate",
                table: "CommercialContract",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaintenancePeriodMonths",
                table: "CommercialContract",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RetentionInitialReleasePercent",
                table: "CommercialContract",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 100m);

            // العقود القائمة في الاستلام الأولي: تاريخه من آخر انتقال إليه في سجل الحالات (بداية فترة الصيانة)
            migrationBuilder.Sql(@"
UPDATE c SET InitialAcceptanceDate = CAST(l.LastAt AS date)
FROM CommercialContract c
CROSS APPLY (SELECT MAX(s.[At]) AS LastAt FROM ContractStatusLog s WHERE s.ContractId = c.Id AND s.ToStatus = N'initial_acceptance') l
WHERE c.Stage = N'initial_acceptance' AND l.LastAt IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FinalAcceptanceDate",
                table: "CommercialContract");

            migrationBuilder.DropColumn(
                name: "InitialAcceptanceDate",
                table: "CommercialContract");

            migrationBuilder.DropColumn(
                name: "MaintenancePeriodMonths",
                table: "CommercialContract");

            migrationBuilder.DropColumn(
                name: "RetentionInitialReleasePercent",
                table: "CommercialContract");
        }
    }
}
