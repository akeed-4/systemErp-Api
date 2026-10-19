using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <summary>
    /// معرّف الباقة من enum إلى رمز نصي (SubscriptionPlanId) ليُنشئ مدير المنصة باقات جديدة ويحذفها.
    /// القيم المخزنة نصاً أصلاً (Starter…) تُحوَّل إلى الرمز (starter…) في مكانها، فتبقى أسعار الباقات وحدودها
    /// كما عدّلها المدير ولا تُعاد زراعتها. لا تغيير في أعمدة الجداول.
    /// </summary>
    public partial class PlanIdsAsCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE dbo.PlanDefinition SET Id = LOWER(Id);
                UPDATE dbo.Subscription SET PlanType = LOWER(PlanType);
                UPDATE dbo.OnlinePayment SET PlanId = LOWER(PlanId) WHERE PlanId IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // التراجع ممكن فقط بالباقات الثلاث الأصلية: الباقة المُنشأة لاحقاً لا مقابل لها في الـ enum القديم
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM dbo.PlanDefinition WHERE Id NOT IN (N'starter', N'professional', N'enterprise'))
                   OR EXISTS (SELECT 1 FROM dbo.Subscription WHERE PlanType NOT IN (N'starter', N'professional', N'enterprise'))
                   OR EXISTS (SELECT 1 FROM dbo.OnlinePayment WHERE PlanId IS NOT NULL AND PlanId NOT IN (N'starter', N'professional', N'enterprise'))
                    THROW 50201, N'PlanIdsAsCodes (down): custom plans exist; delete them before rolling back.', 1;
                UPDATE dbo.PlanDefinition SET Id = UPPER(LEFT(Id, 1)) + SUBSTRING(Id, 2, 64);
                UPDATE dbo.Subscription SET PlanType = UPPER(LEFT(PlanType, 1)) + SUBSTRING(PlanType, 2, 64);
                UPDATE dbo.OnlinePayment SET PlanId = UPPER(LEFT(PlanId, 1)) + SUBSTRING(PlanId, 2, 64) WHERE PlanId IS NOT NULL;
                """);
        }
    }
}
