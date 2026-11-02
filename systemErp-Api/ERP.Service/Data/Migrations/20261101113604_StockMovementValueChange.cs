using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <summary>
    /// كل حركة تحفظ ما غيّرته في قيمة الصنف الدفترية (ValueChange)، وبطاقة الصنف تقرأ القيمة منها.
    /// الحركات القائمة: قيمتها الكمية × التكلفة بإشارتها (التحويل صفر). ثم لكل صنف لا يساوي مجموع حركاته قيمته الدفترية
    /// (إعادة تسعير سابقة لم تُسجَّل في سجله: آخر شراء، المعيارية، إعادة الاحتساب، التكاليف الإضافية، عكس فاتورة معدَّلة)
    /// حركة إعادة تقييم واحدة بالفرق، فتطابق البطاقةُ الدفاترَ من أول يوم.
    /// </summary>
    public partial class StockMovementValueChange : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ValueChange",
                table: "StockMovement",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            // العمود Type نصي (اسم القيمة لا رقمها)؛ مقارنته بأرقام تعطي Msg 245.
            // وارد مشتريات وتسوية إضافة: موجب؛ مبيعات وتسوية خصم: سالب؛ التحويل: صفر
            migrationBuilder.Sql(@"
UPDATE [StockMovement] SET [ValueChange] =
    CASE WHEN [Type] IN (N'InPurchase', N'AdjustmentIn') THEN ROUND([Quantity] * [UnitCost], 2)
         WHEN [Type] IN (N'OutSales', N'AdjustmentOut') THEN -ROUND([Quantity] * [UnitCost], 2)
         ELSE 0 END;");

            migrationBuilder.Sql(@"
INSERT INTO [StockMovement] ([Id], [TenantId], [CreatedAt], [ItemId], [WarehouseId], [ItemName], [Date], [Type], [Quantity], [UnitCost],
    [ReferenceNumber], [RemainingStock], [RemainingQuantity], [ValueChange], [Notes], [SourceType])
SELECT NEWID(), p.[TenantId], SYSUTCDATETIME(), p.[Id], NULL, p.[NameAr], CAST(DATEADD(hour, 3, SYSUTCDATETIME()) AS date), N'Revaluation', 0, p.[AverageCost],
    N'OPENING-VALUE', p.[CurrentStock], 0, d.[Diff], N'مطابقة قيمة الصنف مع إعادة تسعير سابقة لم تُسجَّل في سجله', N'inventory_revaluation'
FROM [Product] p
CROSS APPLY (SELECT ROUND(p.[CurrentStock] * p.[AverageCost], 2)
    - ISNULL((SELECT SUM(m.[ValueChange]) FROM [StockMovement] m WHERE m.[ItemId] = p.[Id]), 0) AS [Diff]) d
WHERE d.[Diff] <> 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM [StockMovement] WHERE [Type] = N'Revaluation' AND [ReferenceNumber] = N'OPENING-VALUE';");
            migrationBuilder.DropColumn(
                name: "ValueChange",
                table: "StockMovement");
        }
    }
}
