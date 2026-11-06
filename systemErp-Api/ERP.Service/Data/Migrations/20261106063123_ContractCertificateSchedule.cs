using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class ContractCertificateSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CertificateCount",
                table: "CommercialContract",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CertificatePeriodMonths",
                table: "CommercialContract",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CertificateSchedule",
                table: "CommercialContract",
                type: "nvarchar(max)",
                nullable: false,
                // العقود القائمة: مستخلص حسب الطلب (سلوكها قبل الجدولة)
                defaultValue: "on_demand");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CertificateCount",
                table: "CommercialContract");

            migrationBuilder.DropColumn(
                name: "CertificatePeriodMonths",
                table: "CommercialContract");

            migrationBuilder.DropColumn(
                name: "CertificateSchedule",
                table: "CommercialContract");
        }
    }
}
