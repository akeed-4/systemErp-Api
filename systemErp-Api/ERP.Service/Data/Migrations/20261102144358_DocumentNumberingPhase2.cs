using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class DocumentNumberingPhase2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ReturnNumber",
                table: "PosSalesReturn",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "ReturnNumber",
                table: "DeliveryReturnNote",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "DeliveryNumber",
                table: "DeliveryNote",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "AgreementNumber",
                table: "Agreement",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.CreateIndex(
                name: "IX_PosSalesReturn_TenantId_ReturnNumber",
                table: "PosSalesReturn",
                columns: new[] { "TenantId", "ReturnNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryReturnNote_TenantId_ReturnNumber",
                table: "DeliveryReturnNote",
                columns: new[] { "TenantId", "ReturnNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryNote_TenantId_DeliveryNumber",
                table: "DeliveryNote",
                columns: new[] { "TenantId", "DeliveryNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Agreement_TenantId_AgreementNumber",
                table: "Agreement",
                columns: new[] { "TenantId", "AgreementNumber" },
                unique: true);

            // سند الاستلام (شراء) صار نوعاً بعدّاد مستقل عن بيان التسليم (بيع): يبدأ من قيمة العدّاد المشترك السابق (العام ولكل مستودع)
            // فلا يتكرر رقم قائم مهما كانت البادئة التي صدرت بها الأرقام السابقة
            migrationBuilder.Sql(@"
INSERT INTO [NumberSequence] ([Id], [TenantId], [CreatedAt], [Key], [Prefix], [LastValue], [Padding], [Version], [Mode], [Scope], [ResetYearly], [IsActive])
SELECT NEWID(), s.[TenantId], SYSUTCDATETIME(), STUFF(s.[Key], 1, 13, N'goods_receipt'),
    CASE WHEN s.[Key] = N'delivery_note' THEN N'GRN-' ELSE s.[Prefix] END, s.[LastValue], s.[Padding], NEWID(), N'auto', NULL, 0, 1
FROM [NumberSequence] s
WHERE (s.[Key] = N'delivery_note' OR LEFT(s.[Key], 14) = N'delivery_note@')
  AND NOT EXISTS (SELECT 1 FROM [NumberSequence] g WHERE g.[TenantId] = s.[TenantId] AND g.[Key] = STUFF(s.[Key], 1, 13, N'goods_receipt'));");

            // العدّاد العام لبيانات التسليم ببادئتها (كان يحمل بادئة أول مستند صدر به، وقد تكون GRN-)
            migrationBuilder.Sql("UPDATE [NumberSequence] SET [Prefix] = N'DN-' WHERE [Key] = N'delivery_note' AND [Prefix] = N'GRN-';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM [NumberSequence] WHERE [Key] = N'goods_receipt' OR LEFT([Key], 14) = N'goods_receipt@';");

            migrationBuilder.DropIndex(
                name: "IX_PosSalesReturn_TenantId_ReturnNumber",
                table: "PosSalesReturn");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryReturnNote_TenantId_ReturnNumber",
                table: "DeliveryReturnNote");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryNote_TenantId_DeliveryNumber",
                table: "DeliveryNote");

            migrationBuilder.DropIndex(
                name: "IX_Agreement_TenantId_AgreementNumber",
                table: "Agreement");

            migrationBuilder.AlterColumn<string>(
                name: "ReturnNumber",
                table: "PosSalesReturn",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "ReturnNumber",
                table: "DeliveryReturnNote",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "DeliveryNumber",
                table: "DeliveryNote",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "AgreementNumber",
                table: "Agreement",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");
        }
    }
}
