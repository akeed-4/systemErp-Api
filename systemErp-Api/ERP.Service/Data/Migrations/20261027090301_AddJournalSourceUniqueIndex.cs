using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddJournalSourceUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // فحص قبل أي تغيير: قيمة أطول من العمود الجديد أو قيود فعّالة مكررة لمستند واحد توقف الترحيل برسالة تسمّيها،
            // ولا يُمس أي قيد سابق؛ تُعالج يدوياً ثم يُعاد التشغيل.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [JournalEntry] WHERE LEN([SourceType]) > 64 OR LEN([Status]) > 32)
                BEGIN
                    THROW 50001, N'AddJournalSourceUniqueIndex: JournalEntry has SourceType longer than 64 or Status longer than 32 characters.', 1;
                END;

                DECLARE @duplicates nvarchar(max) = (
                    SELECT STRING_AGG(CONVERT(nvarchar(max), d.[SourceType] + N':' + CONVERT(nvarchar(36), d.[SourceReferenceId]) + N' (' + d.[Entries] + N')'), N'; ')
                    FROM (
                        SELECT [TenantId], [SourceType], [SourceReferenceId], STRING_AGG(CONVERT(nvarchar(max), [EntryNumber]), N',') AS [Entries]
                        FROM [JournalEntry]
                        WHERE [Status] = N'Posted' AND [SourceReferenceId] IS NOT NULL
                          AND [SourceType] IN (N'sales', N'purchase', N'sales_return', N'purchase_return', N'inventory_count', N'stock_adjustment', N'inventory_period_end', N'inventory_period_opening')
                        GROUP BY [TenantId], [SourceType], [SourceReferenceId]
                        HAVING COUNT(*) > 1) d);
                IF @duplicates IS NOT NULL
                BEGIN
                    DECLARE @message nvarchar(2048) = LEFT(N'AddJournalSourceUniqueIndex: duplicate active journal entries per source document: ' + @duplicates, 2047);
                    THROW 50002, @message, 1;
                END
                """);

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "JournalEntry",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "SourceType",
                table: "JournalEntry",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntry_TenantId_SourceType_SourceReferenceId",
                table: "JournalEntry",
                columns: new[] { "TenantId", "SourceType", "SourceReferenceId" },
                unique: true,
                filter: "[Status] = N'Posted' AND [SourceReferenceId] IS NOT NULL AND [SourceType] IN (N'sales', N'purchase', N'sales_return', N'purchase_return', N'inventory_count', N'stock_adjustment', N'inventory_period_end', N'inventory_period_opening')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_JournalEntry_TenantId_SourceType_SourceReferenceId",
                table: "JournalEntry");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "JournalEntry",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "SourceType",
                table: "JournalEntry",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64,
                oldNullable: true);
        }
    }
}
