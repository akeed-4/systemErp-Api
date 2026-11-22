using ERP.Service.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <summary>
    /// إضافتا «ورش السيارات» و«تأجير المعدات» في دليل الوحدات. لا تُمنحان لأي اشتراك هنا: يمنحهما مدير المنصة من «الوحدات»
    /// (سعرهما حسب الاتفاق حتى يتقرر)، فلا يتغير شيء لأي منشأة قائمة.
    /// </summary>
    [DbContext(typeof(ErpDbContext))]
    [Migration("20261120200000_WorkshopRentalAddOns")]
    public partial class WorkshopRentalAddOns : Migration
    {
        private static readonly string[] Columns = { "Key", "DescriptionAr", "DescriptionEn", "Icon", "IsActive", "NameAr", "NameEn", "Number", "SortOrder" };
        // الهجرة بلا ملف Designer فلا نموذج هدف يستنتج منه EF أنواع الأعمدة: تُذكر صراحة
        private static readonly string[] ColumnTypes = { "nvarchar(64)", "nvarchar(max)", "nvarchar(max)", "nvarchar(max)", "bit", "nvarchar(200)", "nvarchar(200)", "int", "int" };
        private const string KeyType = "nvarchar(64)";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "PlatformModuleDefinition",
                columns: Columns,
                columnTypes: ColumnTypes,
                values: new object[] { "workshop", "مركبات العملاء والاستقبال وأوامر العمل والتقديرات والفنيين وقطع الغيار والجودة والضمان.", "Customer vehicles, reception, work orders, estimates, technicians, spare parts, quality and warranty.", "car_repair", true, "ورش السيارات", "Automotive Workshop", 7, 7 });

            migrationBuilder.InsertData(
                table: "PlatformModuleDefinition",
                columns: Columns,
                columnTypes: ColumnTypes,
                values: new object[] { "equipment_rental", "تسعير المعدات وتوفرها وحجوزاتها وعقود التأجير والتسليم والاستلام والفوترة الدورية والتأمينات.", "Equipment pricing, availability, bookings, rental contracts, handovers, periodic billing and deposits.", "front_loader", true, "تأجير المعدات", "Equipment Rental", 8, 8 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE [Subscription] SET ModuleKeys = REPLACE(REPLACE(ModuleKeys, ',workshop', ''), ',equipment_rental', '') WHERE ModuleKeys LIKE '%,workshop%' OR ModuleKeys LIKE '%,equipment_rental%';");

            migrationBuilder.DeleteData(table: "PlatformModuleDefinition", keyColumn: "Key", keyColumnType: KeyType, keyValue: "workshop");
            migrationBuilder.DeleteData(table: "PlatformModuleDefinition", keyColumn: "Key", keyColumnType: KeyType, keyValue: "equipment_rental");
        }
    }
}
