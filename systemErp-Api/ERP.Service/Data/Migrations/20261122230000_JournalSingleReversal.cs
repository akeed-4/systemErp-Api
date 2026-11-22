using ERP.Service.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <summary>
    /// قيد عكسي واحد لكل قيد أصلي (ضمان على مستوى قاعدة البيانات): طلبا عكس متزامنان لا يُنشئان قيدين عكسيين، فالثاني يفشل بتعارض 409.
    /// يتوقف الترحيل برسالة واضحة إن وُجد في البيانات قيد معكوس أكثر من مرة (يُعالج يدوياً قبل إعادة التشغيل).
    /// </summary>
    [DbContext(typeof(ErpDbContext))]
    [Migration("20261122230000_JournalSingleReversal")]
    public partial class JournalSingleReversal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM [JournalEntry] WHERE [SourceType] = N'reversal' AND [SourceReferenceId] IS NOT NULL
           GROUP BY [TenantId], [SourceReferenceId] HAVING COUNT(*) > 1)
    THROW 50001, N'JournalSingleReversal: journal entries reversed more than once exist; resolve the duplicate reversals before applying this migration.', 1;");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntry_TenantId_SourceReferenceId",
                table: "JournalEntry",
                columns: new[] { "TenantId", "SourceReferenceId" },
                unique: true,
                filter: "[SourceType] = N'reversal' AND [SourceReferenceId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_JournalEntry_TenantId_SourceReferenceId", table: "JournalEntry");
        }
    }
}
