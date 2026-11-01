using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <summary>
    /// «تضمين أعباء الشحن والتخليص في التكلفة» صار يُطبَّق عند اعتماد سند التكاليف الإضافية، وكان مخزَّناً false افتراضياً
    /// بينما النظام يرفع تكلفة الأصناف دائماً. يُضبط true للسياسات القائمة فلا يتغيّر سلوك أي منشأة حتى توقفه بنفسها.
    /// </summary>
    public partial class KeepFreightInCostForExistingPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE [CostingPolicy] SET [IncludeFreightAndCustoms] = 1 WHERE [IncludeFreightAndCustoms] = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // لا تُعرف القيم السابقة (كانت بلا أثر): لا شيء يُعاد
        }
    }
}
