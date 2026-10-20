using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <summary>
    /// نقل مستندات التجارة العامة ونقاط البيع المحفوظة قبل SplitTradeDocuments من جدول Invoice (الذي أصبح لفواتير المعارض فقط)
    /// إلى جدول كل نوع مع بنودها، بنفس المعرّفات حتى تبقى القيود وتوزيعات السندات ودفعات الفواتير والمرتجعات مرتبطة بها.
    /// </summary>
    public partial class MoveLegacyTradeDocuments : Migration
    {
        /// <summary>رأس الجدول الهدف وبنوده لكل نوع فاتورة.</summary>
        private static readonly (string Kind, string Header, string Items)[] Targets =
        {
            ("Sales", "SalesInvoice", "SalesInvoiceItem"),
            ("Purchase", "PurchaseInvoice", "PurchaseInvoiceItem"),
            ("SalesReturn", "SalesReturn", "SalesReturnItem"),
            ("PurchaseReturn", "PurchaseReturn", "PurchaseReturnItem"),
        };

        /// <summary>مستند تجارة عامة: ليس من وحدة المعارض ولا يحمل سيارات.</summary>
        private const string IsTradeDocument =
            "i.[Module] <> N'car_showroom' " +
            "AND NOT EXISTS (SELECT 1 FROM [InvoiceVehicleLine] l WHERE l.[InvoiceId] = i.[Id]) " +
            "AND NOT EXISTS (SELECT 1 FROM [InvoiceVehicleBatch] b WHERE b.[InvoiceId] = i.[Id])";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var (kind, header, items) in Targets)
            {
                var filter = $"i.[Kind] = N'{kind}' AND {IsTradeDocument} AND NOT EXISTS (SELECT 1 FROM [{header}] t WHERE t.[Id] = i.[Id])";
                migrationBuilder.Sql(CopySql("Invoice", "InvoiceItem", header, items, filter));
                migrationBuilder.Sql($"""
                    DELETE it FROM [InvoiceItem] it WHERE it.[InvoiceId] IN (SELECT [Id] FROM [{header}]);
                    DELETE i FROM [Invoice] i WHERE i.[Id] IN (SELECT [Id] FROM [{header}]);
                    """);
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // يعيد كل مستندات الأنواع الأربعة إلى Invoice (كان هو الجدول الوحيد قبل الفصل).
            foreach (var (_, header, items) in Targets)
            {
                migrationBuilder.Sql(CopySql(header, items, "Invoice", "InvoiceItem", "NOT EXISTS (SELECT 1 FROM [Invoice] t WHERE t.[Id] = i.[Id])"));
                migrationBuilder.Sql($"""
                    DELETE FROM [{items}];
                    DELETE FROM [{header}];
                    """);
            }
        }

        /// <summary>
        /// ينسخ الرؤوس المطابقة للشرط (بالاسم المستعار i) ثم بنودها، بالأعمدة المشتركة بين الجدولين
        /// حتى لا ينكسر النقل إن أضيف عمود لأحدهما لاحقاً.
        /// </summary>
        private static string CopySql(string fromHeader, string fromItems, string toHeader, string toItems, string filter) => $"""
            DECLARE @cols nvarchar(max), @sql nvarchar(max);

            SELECT @cols = STRING_AGG(CAST(QUOTENAME(c.name) AS nvarchar(max)), N', ')
            FROM sys.columns c
            WHERE c.object_id = OBJECT_ID(N'{toHeader}') AND c.is_computed = 0
              AND EXISTS (SELECT 1 FROM sys.columns s WHERE s.object_id = OBJECT_ID(N'{fromHeader}') AND s.name = c.name);

            SET @sql = N'SELECT i.[Id] INTO #moved FROM [{fromHeader}] i WHERE ' + N'{filter.Replace("'", "''")}' + N';
                INSERT INTO [{toHeader}] (' + @cols + N') SELECT ' + @cols + N' FROM [{fromHeader}] WHERE [Id] IN (SELECT [Id] FROM #moved);';

            DECLARE @itemCols nvarchar(max);
            SELECT @itemCols = STRING_AGG(CAST(QUOTENAME(c.name) AS nvarchar(max)), N', ')
            FROM sys.columns c
            WHERE c.object_id = OBJECT_ID(N'{toItems}') AND c.is_computed = 0
              AND EXISTS (SELECT 1 FROM sys.columns s WHERE s.object_id = OBJECT_ID(N'{fromItems}') AND s.name = c.name);

            SET @sql = @sql + N'
                INSERT INTO [{toItems}] (' + @itemCols + N') SELECT ' + @itemCols + N' FROM [{fromItems}] WHERE [InvoiceId] IN (SELECT [Id] FROM #moved);';

            EXEC sp_executesql @sql;
            """;
    }
}
