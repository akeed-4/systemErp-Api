using ERP.Service.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <summary>
    /// التسعير الشامل للضريبة في المبيعات معطّل للمنشأة افتراضياً (حتى للمالك والمدير) حتى يُفعَّل في إعدادات الحسابات؛
    /// وصلاحيته لم تعد ممنوحة افتراضياً لأي دور (المنح الصريح المحفوظ يبقى). الفواتير المحفوظة بطريقتها تُعدَّل كما هي.
    /// </summary>
    [DbContext(typeof(ErpDbContext))]
    [Migration("20261123090000_SalesVatInclusiveSwitch")]
    public partial class SalesVatInclusiveSwitch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(name: "AllowSalesVatInclusivePricing", table: "AccountingSettings", type: "bit", nullable: false, defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AllowSalesVatInclusivePricing", table: "AccountingSettings");
        }
    }
}
