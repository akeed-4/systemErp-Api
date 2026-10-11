using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPosModule : Migration
    {
        // نقطة البيع كانت جزءاً من وحدة accounting وصارت وحدة مستقلة (pos). كل باقة واشتراك يملك accounting يأخذ pos
        // حتى لا يفقد عميل حالي نقطة البيع. تحديث شرطي لا يمسّ ما خصّصه مدير المنصة من وحدات (بدل UpdateData على بذور الباقات).
        private const string AddPos = @"
UPDATE [{0}] SET [ModuleKeys] = [ModuleKeys] + ',pos'
WHERE ',' + [ModuleKeys] + ',' LIKE '%,accounting,%' AND ',' + [ModuleKeys] + ',' NOT LIKE '%,pos,%';";

        private const string RemovePos = @"
UPDATE [{0}] SET [ModuleKeys] = REPLACE(REPLACE(',' + [ModuleKeys] + ',', ',pos,', ','), ',,', ',')
WHERE ',' + [ModuleKeys] + ',' LIKE '%,pos,%';
UPDATE [{0}] SET [ModuleKeys] = SUBSTRING([ModuleKeys], 2, LEN([ModuleKeys]) - 2)
WHERE [ModuleKeys] LIKE ',%,';";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(string.Format(AddPos, "PlanDefinition"));
            migrationBuilder.Sql(string.Format(AddPos, "Subscription"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(string.Format(RemovePos, "Subscription"));
            migrationBuilder.Sql(string.Format(RemovePos, "PlanDefinition"));
        }
    }
}
