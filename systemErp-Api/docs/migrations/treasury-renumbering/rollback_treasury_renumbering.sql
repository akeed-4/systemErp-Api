/* =====================================================================
   التراجع عن توحيد ترقيم البنوك والصناديق (بعد تنفيذ فعلي لـ renumber_treasury_accounts.sql)
   ---------------------------------------------------------------------
   يقرأ dbo.TreasuryRenumberLog للتشغيل @Batch (ولمنشأة واحدة اختيارياً) ويعيد لكل حساب رمزه وأبه القديمين، ومعه كل
   المراجع في الأعمدة نفسها، داخل معاملة لكل منشأة وبالتحقق نفسه (عدد القيود والمجاميع والأرصدة والمراجع قبل = بعد).
   الافتراضي @DryRun = 1 (تجربة مع تراجع).

   يُرفض لمنشأة إن كان رمز قديم مطلوب استعادته مستخدماً الآن لحساب آخر (أُنشئ بعد الترحيل، مثل صندوق جديد 1112):
   يُعالج يدوياً. الحسابات المنشأة بعد الترحيل تبقى برموزها الجديدة، وعدّادات treasury_code لا تُحذف (لا تؤثر في الترقيم القديم).
   ===================================================================== */
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @DryRun bit = 1;
DECLARE @Batch uniqueidentifier = NULL;      -- مطلوب: من نتيجة التنفيذ أو من السجل
DECLARE @OnlyTenant uniqueidentifier = NULL;

IF @Batch IS NULL THROW 51010, 'set @Batch (see dbo.TreasuryRenumberLog)', 1;

IF OBJECT_ID('tempdb..#cols') IS NOT NULL DROP TABLE #cols;
SELECT c.TABLE_SCHEMA AS Sch, c.TABLE_NAME AS Tbl, c.COLUMN_NAME AS Col
INTO #cols
FROM INFORMATION_SCHEMA.COLUMNS c
JOIN INFORMATION_SCHEMA.TABLES t ON t.TABLE_SCHEMA = c.TABLE_SCHEMA AND t.TABLE_NAME = c.TABLE_NAME AND t.TABLE_TYPE = 'BASE TABLE'
JOIN INFORMATION_SCHEMA.COLUMNS tc ON tc.TABLE_SCHEMA = c.TABLE_SCHEMA AND tc.TABLE_NAME = c.TABLE_NAME AND tc.COLUMN_NAME = 'TenantId'
WHERE c.DATA_TYPE IN ('nvarchar', 'varchar')
  AND c.TABLE_NAME <> 'TreasuryRenumberLog'
  AND (c.COLUMN_NAME LIKE '%AccountCode'
       OR (c.TABLE_NAME = 'CarShowroomSettings' AND c.COLUMN_NAME LIKE '%Account')
       OR (c.TABLE_NAME = 'Account' AND c.COLUMN_NAME = 'ParentCode'));

IF OBJECT_ID('tempdb..#log') IS NOT NULL DROP TABLE #log;
SELECT TenantId, AccountId AS Id, NewCode AS FromCode, OldCode AS ToCode, NewParentCode AS FromParent, OldParentCode AS ToParent
INTO #log
FROM dbo.TreasuryRenumberLog
WHERE Batch = @Batch AND RolledBackAt IS NULL AND (@OnlyTenant IS NULL OR TenantId = @OnlyTenant);

IF OBJECT_ID('tempdb..#result') IS NOT NULL DROP TABLE #result;
CREATE TABLE #result (TenantId uniqueidentifier, Accounts int, Outcome nvarchar(400));

DECLARE @tenant uniqueidentifier, @sql nvarchar(max), @n int, @problems int, @lock int;
DECLARE tenants CURSOR LOCAL FAST_FORWARD FOR SELECT DISTINCT TenantId FROM #log;
OPEN tenants;
FETCH NEXT FROM tenants INTO @tenant;
WHILE @@FETCH_STATUS = 0
BEGIN
    BEGIN TRY
        IF OBJECT_ID('tempdb..#tm') IS NOT NULL DROP TABLE #tm;
        SELECT Id, FromCode, ToCode, FromParent, ToParent INTO #tm FROM #log WHERE TenantId = @tenant;
        SELECT @n = COUNT(*) FROM #tm;

        -- الحساب ما زال بالرمز الذي وضعه الترحيل، والرمز القديم غير مشغول بحساب آخر
        IF EXISTS (SELECT 1 FROM #tm m JOIN [Account] a ON a.Id = m.Id WHERE a.Code <> m.FromCode)
            OR EXISTS (SELECT 1 FROM #tm m JOIN [Account] x ON x.TenantId = @tenant AND x.Code = m.ToCode
                       WHERE NOT EXISTS (SELECT 1 FROM #tm y WHERE y.Id = x.Id))
        BEGIN
            INSERT #result VALUES (@tenant, @n, N'مرفوض: رمز تغيّر بعد الترحيل أو رمز قديم مستخدم لحساب آخر — مراجعة يدوية');
            FETCH NEXT FROM tenants INTO @tenant;
            CONTINUE;
        END

        BEGIN TRANSACTION;
        EXEC @lock = sp_getapplock @Resource = 'treasury_renumbering', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000;
        IF @lock < 0 THROW 51001, 'treasury renumbering lock timeout', 1;
        SELECT @n = @n FROM [Account] WITH (TABLOCKX, HOLDLOCK) WHERE 1 = 0;

        IF OBJECT_ID('tempdb..#before') IS NOT NULL DROP TABLE #before;
        CREATE TABLE #before (Kind varchar(140), Id uniqueidentifier, Cnt bigint, Amount decimal(38, 6));
        INSERT #before SELECT 'balance', a.Id, 0, a.Balance FROM [Account] a JOIN #tm m ON m.Id = a.Id;
        INSERT #before SELECT 'lines', m.Id, COUNT(l.Id), ISNULL(SUM(l.Debit - l.Credit), 0)
            FROM #tm m LEFT JOIN [JournalEntryLine] l ON l.TenantId = @tenant AND l.AccountCode = m.FromCode GROUP BY m.Id;
        SET @sql = N'';
        SELECT @sql = @sql + N'
INSERT #before SELECT ' + QUOTENAME(Tbl + '.' + Col, '''') + N', m.Id, COUNT(r.' + QUOTENAME(Col) + N'), 0
FROM #tm m LEFT JOIN ' + QUOTENAME(Sch) + N'.' + QUOTENAME(Tbl) + N' r ON r.TenantId = @t AND r.' + QUOTENAME(Col) + N' = m.FromCode GROUP BY m.Id;'
        FROM #cols;
        EXEC sp_executesql @sql, N'@t uniqueidentifier', @t = @tenant;

        UPDATE a SET a.Code = 'TMP' + m.ToCode, a.UpdatedAt = SYSUTCDATETIME()
        FROM [Account] a JOIN #tm m ON m.Id = a.Id WHERE m.FromCode <> m.ToCode;
        UPDATE a SET a.Code = m.ToCode FROM [Account] a JOIN #tm m ON m.Id = a.Id WHERE m.FromCode <> m.ToCode;
        UPDATE a SET a.ParentCode = m.ToParent FROM [Account] a JOIN #tm m ON m.Id = a.Id WHERE ISNULL(a.ParentCode, '') <> ISNULL(m.ToParent, '');

        SET @sql = N'';
        SELECT @sql = @sql + N'
UPDATE r SET r.' + QUOTENAME(Col) + N' = m.ToCode
FROM ' + QUOTENAME(Sch) + N'.' + QUOTENAME(Tbl) + N' r JOIN #tm m ON r.' + QUOTENAME(Col) + N' = m.FromCode AND m.FromCode <> m.ToCode
WHERE r.TenantId = @t;'
        FROM #cols;
        EXEC sp_executesql @sql, N'@t uniqueidentifier', @t = @tenant;

        IF OBJECT_ID('tempdb..#after') IS NOT NULL DROP TABLE #after;
        CREATE TABLE #after (Kind varchar(140), Id uniqueidentifier, Cnt bigint, Amount decimal(38, 6));
        INSERT #after SELECT 'balance', a.Id, 0, a.Balance FROM [Account] a JOIN #tm m ON m.Id = a.Id;
        INSERT #after SELECT 'lines', m.Id, COUNT(l.Id), ISNULL(SUM(l.Debit - l.Credit), 0)
            FROM #tm m LEFT JOIN [JournalEntryLine] l ON l.TenantId = @tenant AND l.AccountCode = m.ToCode GROUP BY m.Id;
        SET @sql = N'';
        SELECT @sql = @sql + N'
INSERT #after SELECT ' + QUOTENAME(Tbl + '.' + Col, '''') + N', m.Id, COUNT(r.' + QUOTENAME(Col) + N'), 0
FROM #tm m LEFT JOIN ' + QUOTENAME(Sch) + N'.' + QUOTENAME(Tbl) + N' r ON r.TenantId = @t AND r.' + QUOTENAME(Col) + N' = m.ToCode GROUP BY m.Id;'
        FROM #cols;
        EXEC sp_executesql @sql, N'@t uniqueidentifier', @t = @tenant;

        SELECT @problems = COUNT(*) FROM #before b FULL JOIN #after a ON a.Kind = b.Kind AND a.Id = b.Id
        WHERE a.Kind IS NULL OR b.Kind IS NULL OR a.Cnt <> b.Cnt OR a.Amount <> b.Amount;
        SET @problems = @problems
            + (SELECT COUNT(*) FROM [Account] WHERE TenantId = @tenant AND Code LIKE 'TMP%')
            + (SELECT COUNT(*) FROM (SELECT Code FROM [Account] WHERE TenantId = @tenant GROUP BY Code HAVING COUNT(*) > 1) d);

        IF @problems > 0
        BEGIN
            ROLLBACK TRANSACTION;
            INSERT #result VALUES (@tenant, @n, N'فشل التحقق (' + CAST(@problems AS nvarchar(10)) + N' فرق) — لم يُتراجع');
        END
        ELSE IF @DryRun = 1
        BEGIN
            ROLLBACK TRANSACTION;
            INSERT #result VALUES (@tenant, @n, N'تجربة تراجع ناجحة (DryRun)');
        END
        ELSE
        BEGIN
            UPDATE dbo.TreasuryRenumberLog SET RolledBackAt = SYSUTCDATETIME() WHERE Batch = @Batch AND TenantId = @tenant AND RolledBackAt IS NULL;
            COMMIT TRANSACTION;
            INSERT #result VALUES (@tenant, @n, N'تم التراجع');
        END
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        INSERT #result VALUES (@tenant, @n, N'خطأ: ' + ERROR_MESSAGE());
    END CATCH;
    FETCH NEXT FROM tenants INTO @tenant;
END
CLOSE tenants;
DEALLOCATE tenants;

SELECT r.TenantId, tn.NameAr AS TenantName, r.Accounts, r.Outcome
FROM #result r LEFT JOIN [Tenant] tn ON tn.Id = r.TenantId ORDER BY r.Outcome, TenantName;
