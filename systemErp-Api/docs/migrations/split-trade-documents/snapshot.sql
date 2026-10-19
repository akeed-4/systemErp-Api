/*
  لقطة تحقق لترحيل فصل مستندات التجارة العامة (SplitTradeDocuments).
  تُشغَّل قبل الترحيل وبعده على القاعدة نفسها، ويجب أن يتطابق المخرَجان حرفياً (صفر فروق):
    sqlcmd -S "(localdb)\MSSQLLocalDB" -d <Db> -E -W -h -1 -s "|" -i snapshot.sql -o before.txt
    ... dotnet ef database update ...
    sqlcmd ... -o after.txt  &&  diff before.txt after.txt
  قبل الترحيل: الفواتير كلها في Invoice. بعده: Invoice (المعارض) + الجداول الأربعة الجديدة.
  المعرّفات تُنقل كما هي، فاللقطة تقارن الصفوف نفسها لا مجاميع فقط.
*/
SET NOCOUNT ON;

IF OBJECT_ID('tempdb..#Inv') IS NOT NULL DROP TABLE #Inv;
IF OBJECT_ID('tempdb..#Item') IS NOT NULL DROP TABLE #Item;

CREATE TABLE #Inv (Src nvarchar(30), Id uniqueidentifier, TenantId uniqueidentifier, Module nvarchar(64), Kind nvarchar(64), InvoiceNumber nvarchar(450),
    Status nvarchar(max), Subtotal decimal(18,4), DiscountTotal decimal(18,4), VatTotal decimal(18,4), GrandTotal decimal(18,4), TotalCost decimal(18,4),
    GrossProfit decimal(18,4), JournalEntryId uniqueidentifier, OriginalInvoiceId uniqueidentifier, PartyId uniqueidentifier, ReferenceId uniqueidentifier);
CREATE TABLE #Item (Src nvarchar(30), Id uniqueidentifier, InvoiceId uniqueidentifier, ItemId uniqueidentifier, Quantity decimal(18,4), UnitPrice decimal(18,4),
    UnitCost decimal(18,4), Discount decimal(18,4), VatAmount decimal(18,4), VatCategory nvarchar(64), TotalBeforeVat decimal(18,4), TotalAfterVat decimal(18,4));

DECLARE @cols nvarchar(max) = N'Id, TenantId, Module, Kind, InvoiceNumber, Status, Subtotal, DiscountTotal, VatTotal, GrandTotal, TotalCost, GrossProfit, JournalEntryId, OriginalInvoiceId, PartyId, ReferenceId';
DECLARE @icols nvarchar(max) = N'Id, InvoiceId, ItemId, Quantity, UnitPrice, UnitCost, Discount, VatAmount, VatCategory, TotalBeforeVat, TotalAfterVat';
DECLARE @t nvarchar(128), @it nvarchar(128), @sql nvarchar(max);
DECLARE tables CURSOR LOCAL FAST_FORWARD FOR
    SELECT h, d FROM (VALUES (N'Invoice', N'InvoiceItem'), (N'SalesInvoice', N'SalesInvoiceItem'), (N'PurchaseInvoice', N'PurchaseInvoiceItem'),
                             (N'SalesReturn', N'SalesReturnItem'), (N'PurchaseReturn', N'PurchaseReturnItem')) x(h, d)
    WHERE OBJECT_ID(N'dbo.' + h) IS NOT NULL;
OPEN tables;
FETCH NEXT FROM tables INTO @t, @it;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @sql = N'INSERT #Inv SELECT N''' + @t + N''', ' + @cols + N' FROM dbo.' + QUOTENAME(@t) + N';'
             + N'INSERT #Item SELECT N''' + @it + N''', ' + @icols + N' FROM dbo.' + QUOTENAME(@it) + N';';
    EXEC sp_executesql @sql;
    FETCH NEXT FROM tables INTO @t, @it;
END
CLOSE tables; DEALLOCATE tables;

-- 1) الفواتير: كل صف بمعرّفه وأرقامه (يكشف أي فقد أو تكرار أو تغيّر قيمة)
SELECT 'INV', CONVERT(nvarchar(36), Id), CONVERT(nvarchar(36), TenantId), Module, Kind, InvoiceNumber, Status,
       Subtotal, DiscountTotal, VatTotal, GrandTotal, TotalCost, GrossProfit,
       ISNULL(CONVERT(nvarchar(36), JournalEntryId), '-'), ISNULL(CONVERT(nvarchar(36), OriginalInvoiceId), '-'),
       ISNULL(CONVERT(nvarchar(36), PartyId), '-'), ISNULL(CONVERT(nvarchar(36), ReferenceId), '-')
FROM #Inv ORDER BY Id;

-- 2) البنود: كل سطر بمعرّفه وفاتورته
SELECT 'ITEM', CONVERT(nvarchar(36), Id), CONVERT(nvarchar(36), InvoiceId), CONVERT(nvarchar(36), ItemId),
       Quantity, UnitPrice, UnitCost, Discount, VatAmount, VatCategory, TotalBeforeVat, TotalAfterVat
FROM #Item ORDER BY Id;

-- 3) سلامة الارتباط: بند بلا رأس، ورأس في جدول وبنده في جدول آخر
SELECT 'ORPHAN_ITEMS', COUNT(*) FROM #Item it WHERE NOT EXISTS (SELECT 1 FROM #Inv i WHERE i.Id = it.InvoiceId);
SELECT 'DUPLICATE_INVOICE_IDS', COUNT(*) FROM (SELECT Id FROM #Inv GROUP BY Id HAVING COUNT(*) > 1) d;
SELECT 'DUPLICATE_ITEM_IDS', COUNT(*) FROM (SELECT Id FROM #Item GROUP BY Id HAVING COUNT(*) > 1) d;
SELECT 'SPLIT_WITHOUT_INVOICE', COUNT(*) FROM dbo.InvoicePaymentSplit s WHERE NOT EXISTS (SELECT 1 FROM #Inv i WHERE i.Id = s.InvoiceId);
SELECT 'ALLOCATION_WITHOUT_INVOICE', COUNT(*) FROM dbo.VoucherAllocation a WHERE NOT EXISTS (SELECT 1 FROM #Inv i WHERE i.Id = a.InvoiceId);
SELECT 'RETURN_WITHOUT_ORIGINAL', COUNT(*) FROM #Inv r WHERE r.OriginalInvoiceId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM #Inv o WHERE o.Id = r.OriginalInvoiceId);

-- 4) مجاميع الفواتير لكل منشأة ووحدة ونوع
SELECT 'INV_TOTALS', CONVERT(nvarchar(36), TenantId), Module, Kind, COUNT(*), SUM(Subtotal), SUM(VatTotal), SUM(GrandTotal), SUM(TotalCost), SUM(GrossProfit)
FROM #Inv GROUP BY TenantId, Module, Kind ORDER BY 2, 3, 4;

-- 5) الضريبة: لكل منشأة ونوع مستند وتصنيف ضريبي (أساس الإقرار)
SELECT 'VAT', CONVERT(nvarchar(36), i.TenantId), i.Kind, it.VatCategory, i.Status, COUNT(*), SUM(it.TotalBeforeVat), SUM(it.VatAmount)
FROM #Item it JOIN #Inv i ON i.Id = it.InvoiceId GROUP BY i.TenantId, i.Kind, it.VatCategory, i.Status ORDER BY 2, 3, 4, 5;

-- 6) ميزان المراجعة من القيود (لكل منشأة وحساب وحالة قيد)
SELECT 'TB', CONVERT(nvarchar(36), l.TenantId), l.AccountCode, e.Status, SUM(l.Debit), SUM(l.Credit)
FROM dbo.JournalEntryLine l JOIN dbo.JournalEntry e ON e.Id = l.JournalEntryId
GROUP BY l.TenantId, l.AccountCode, e.Status ORDER BY 2, 3, 4;
SELECT 'TB_TOTAL', CONVERT(nvarchar(36), TenantId), SUM(Debit), SUM(Credit), SUM(Debit) - SUM(Credit) FROM dbo.JournalEntryLine GROUP BY TenantId ORDER BY 2;

-- 7) أرصدة الحسابات والأطراف والمخزون
SELECT 'ACCOUNT', CONVERT(nvarchar(36), TenantId), Code, Balance FROM dbo.Account ORDER BY 2, 3;
SELECT 'CUSTOMER', CONVERT(nvarchar(36), Id), CurrentBalance FROM dbo.Customer ORDER BY 2;
SELECT 'SUPPLIER', CONVERT(nvarchar(36), Id), CurrentBalance FROM dbo.Supplier ORDER BY 2;
SELECT 'PRODUCT', CONVERT(nvarchar(36), Id), CurrentStock, AverageCost FROM dbo.Product ORDER BY 2;
SELECT 'STOCK_MOVES', COUNT(*), SUM(Quantity), SUM(Quantity * UnitCost) FROM dbo.StockMovement;
