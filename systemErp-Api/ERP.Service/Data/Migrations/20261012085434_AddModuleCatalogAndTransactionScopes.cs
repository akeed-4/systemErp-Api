using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddModuleCatalogAndTransactionScopes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Module",
                table: "Voucher",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "accounting");

            migrationBuilder.AddColumn<string>(
                name: "Module",
                table: "JournalEntry",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "accounting");

            migrationBuilder.AddColumn<string>(
                name: "Module",
                table: "Invoice",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "accounting");

            migrationBuilder.Sql("""
                UPDATE i SET Module = 'car_showroom'
                FROM Invoice i
                WHERE i.ReferenceType LIKE 'car[_]%'
                   OR EXISTS (SELECT 1 FROM InvoiceVehicleLine vl WHERE vl.InvoiceId = i.Id);

                     UPDATE Invoice SET Module = 'pos'
                     WHERE ReferenceType = 'pos_transaction';

                UPDATE v SET Module = 'car_showroom'
                FROM Voucher v
                WHERE v.DepositVehicleId IS NOT NULL
                   OR EXISTS (
                       SELECT 1 FROM VoucherAllocation va
                       JOIN Invoice i ON i.Id = va.InvoiceId
                       WHERE va.VoucherId = v.Id AND i.Module = 'car_showroom'
                   );

                UPDATE j SET Module = 'car_showroom'
                FROM JournalEntry j
                WHERE j.SourceType LIKE 'car[_]%'
                   OR j.ReferenceType LIKE 'car[_]%'
                   OR EXISTS (
                       SELECT 1 FROM Invoice i
                       WHERE i.Module = 'car_showroom'
                         AND (i.JournalEntryId = j.Id OR i.Id IN (j.ReferenceId, j.SourceReferenceId))
                   )
                   OR EXISTS (
                       SELECT 1 FROM Voucher v
                       WHERE v.Module = 'car_showroom'
                         AND (v.JournalEntryId = j.Id OR v.Id IN (j.ReferenceId, j.SourceReferenceId))
                   );

                     UPDATE j SET Module = 'pos'
                     FROM JournalEntry j
                     WHERE j.SourceType LIKE 'pos[_]%'
                         OR j.ReferenceType = 'pos_transaction';

                     UPDATE j SET Module = 'hr'
                     FROM JournalEntry j
                     WHERE j.SourceType IN ('payroll', 'employee_loan', 'end_of_service');

                     UPDATE reversal SET Module = original.Module
                     FROM JournalEntry reversal
                     JOIN JournalEntry original ON original.Id = reversal.SourceReferenceId
                     WHERE reversal.SourceType = 'reversal';
                """);

            migrationBuilder.CreateTable(
                name: "PlatformModuleDefinition",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DescriptionAr = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DescriptionEn = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Icon = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformModuleDefinition", x => x.Key);
                });

            migrationBuilder.InsertData(
                table: "PlatformModuleDefinition",
                columns: new[] { "Key", "DescriptionAr", "DescriptionEn", "Icon", "IsActive", "NameAr", "NameEn", "Number", "SortOrder" },
                values: new object[,]
                {
                    { "accounting", "الحسابات والقيود والسندات والتقارير المالية.", "Accounts, journals, vouchers and financial reports.", "account_balance", true, "النظام المحاسبي", "Accounting", 1, 1 },
                    { "car_showroom", "مخزون المركبات والشراء والاستيراد وعقود البيع وتقارير المعرض.", "Vehicle inventory, procurement, sales contracts and showroom reports.", "directions_car", true, "معارض السيارات", "Car Showroom", 2, 2 },
                    { "hr", "الموظفون والإجازات والحضور والسلف والرواتب ونهاية الخدمة.", "Employees, leaves, attendance, loans, payroll and end of service.", "groups", true, "شؤون الموظفين", "Human Resources", 4, 4 },
                    { "pos", "البيع بالورديات والباركود والمرتجعات وطرق الدفع.", "Shift-based sales, barcode, returns and payment methods.", "point_of_sale", true, "نقاط البيع", "Point of Sale", 3, 3 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformModuleDefinition_Number",
                table: "PlatformModuleDefinition",
                column: "Number",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlatformModuleDefinition");

            migrationBuilder.DropColumn(
                name: "Module",
                table: "Voucher");

            migrationBuilder.DropColumn(
                name: "Module",
                table: "JournalEntry");

            migrationBuilder.DropColumn(
                name: "Module",
                table: "Invoice");
        }
    }
}
