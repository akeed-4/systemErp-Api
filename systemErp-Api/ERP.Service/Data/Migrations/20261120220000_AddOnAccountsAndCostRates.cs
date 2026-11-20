using System;
using ERP.Service.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <summary>
    /// أساس الورش والتأجير: سبعة أدوار حسابات فارغة على إعدادات الحسابات (لا افتراضي لها ولا تُهيّأ هنا؛ زر التهيئة أو أول ترحيل في
    /// الوحدة)، وسجل التكلفة المعيارية لساعة الموظف بغرضه. لا يتغير شيء لأي منشأة قائمة.
    /// </summary>
    [DbContext(typeof(ErpDbContext))]
    [Migration("20261120220000_AddOnAccountsAndCostRates")]
    public partial class AddOnAccountsAndCostRates : Migration
    {
        private static readonly string[] RoleColumns =
        {
            "WorkshopWipAccountCode", "WorkshopAbsorbedLaborAccountCode", "WorkshopCostOfRevenueAccountCode", "WorkshopWarrantyExpenseAccountCode",
            "RentalRevenueAccountCode", "RentalCompensationIncomeAccountCode", "RentalDepositsLiabilityAccountCode",
        };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var column in RoleColumns)
                migrationBuilder.AddColumn<string>(name: column, table: "AccountingSettings", type: "nvarchar(max)", nullable: true);

            migrationBuilder.CreateTable(
                name: "EmployeeStandardCostRate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CostPerHour = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeStandardCostRate", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeStandardCostRate_Employee_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employee",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeStandardCostRate_EmployeeId",
                table: "EmployeeStandardCostRate",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeStandardCostRate_TenantId",
                table: "EmployeeStandardCostRate",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeStandardCostRate_TenantId_EmployeeId_Purpose_EffectiveFrom",
                table: "EmployeeStandardCostRate",
                columns: new[] { "TenantId", "EmployeeId", "Purpose", "EffectiveFrom" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "EmployeeStandardCostRate");
            foreach (var column in RoleColumns)
                migrationBuilder.DropColumn(name: column, table: "AccountingSettings");
        }
    }
}
