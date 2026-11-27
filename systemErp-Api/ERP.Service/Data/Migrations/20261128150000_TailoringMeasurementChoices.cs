using ERP.Service.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <summary>
    /// الخياطة — قياسات الثوب التفصيلية: نوع إدخال حقل القياس (رقم أو اختيار بقيمه) لصفات الجسم، وقيم الاختيار في نسخة القياس ولقطة سطر الأمر،
    /// وإصدار الحقول الافتراضية في الإعداد (صفر = يُستكمل قالب الثوب عند أول فتح بعد الترقية).
    /// </summary>
    [DbContext(typeof(ErpDbContext))]
    [Migration("20261128150000_TailoringMeasurementChoices")]
    public partial class TailoringMeasurementChoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InputType", table: "TailoringMeasurementField", type: "nvarchar(16)", maxLength: 16, nullable: false, defaultValue: "number");
            migrationBuilder.AddColumn<string>(
                name: "ChoicesJson", table: "TailoringMeasurementField", type: "nvarchar(max)", nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "ChoicesJson", table: "TailoringMeasurementVersion", type: "nvarchar(max)", nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "MeasurementChoicesJson", table: "TailoringOrderLine", type: "nvarchar(max)", nullable: true);
            migrationBuilder.AddColumn<int>(
                name: "DefaultsVersion", table: "TailoringSettings", type: "int", nullable: false, defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "InputType", table: "TailoringMeasurementField");
            migrationBuilder.DropColumn(name: "ChoicesJson", table: "TailoringMeasurementField");
            migrationBuilder.DropColumn(name: "ChoicesJson", table: "TailoringMeasurementVersion");
            migrationBuilder.DropColumn(name: "MeasurementChoicesJson", table: "TailoringOrderLine");
            migrationBuilder.DropColumn(name: "DefaultsVersion", table: "TailoringSettings");
        }
    }
}
