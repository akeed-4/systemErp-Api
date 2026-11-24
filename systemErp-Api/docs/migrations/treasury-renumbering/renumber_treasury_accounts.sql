/* =====================================================================
   توحيد تسلسل ترقيم البنوك والصناديق — ترحيل البيانات
   ---------------------------------------------------------------------
   ليس هجرة EF ولا يُطبَّق تلقائياً. يُشغَّل يدوياً بعد مراجعة نتائج التدقيق (audit_treasury_accounts.sql) والموافقة.
   الافتراضي @DryRun = 1: ينفذ كل منشأة ويتحقق ثم يتراجع (ROLLBACK)، فلا يتغير شيء. للتنفيذ الفعلي: @DryRun = 0.
   التراجع بعد التنفيذ الفعلي: rollback_treasury_renumbering.sql (يقرأ سجل TreasuryRenumberLog).

   الخريطة (لكل منشأة مفصولة: 110 حساب نظام):
     البنوك 110:   1112 ← 1101، 1113 ← 1102، ثم بقية الأبناء المباشرين بتاريخ الإنشاء ← 1103، 1104 ... 1109، 11010 ...
     الصناديق 111: 1111 يبقى، ثم بقية الأبناء المباشرين بتاريخ الإنشاء ← 1112، 1113 ... 1119، 11110 ...

   @FixMisplacedMainCash = 1 (اختياري، بموافقة): الصندوق 1111 (حساب نظام) الموجود تحت البنوك 110 يُعاد تحت الصناديق 111 ويبقى
   برمزه 1111، ثم يُرقَّم الباقي. بدونه تُستثنى المنشأة كما في التدقيق.

   ما يتغير: Account.Code (وأب 1111 عند التصحيح) فقط — المعرّف والرصيد والقيود كما هي — وكل عمود مرجعي يحمل رمز الحساب:
     - Account.ParentCode
     - كل عمود اسمه ينتهي بـ AccountCode (JournalEntryLine، Voucher، BankEntity، Warehouse، PaymentMethodItem، الفواتير، الإعدادات...)
     - أعمدة إعدادات المعرض المنتهية بـ Account (CarShowroomSettings)
     - عدّادا المجموعتين treasury_code:110 و treasury_code:111
   لا يُعدَّل: AuditLog (سجل تاريخي يبقى كما كُتب)، ولا أعمدة تطابق الرمز صدفة (باركود/أسماء أصناف، أجهزة ZATCA).

   المنشأة المستثناة (لا يُغيَّر فيها شيء) إن وُجد فيها أي من:
     غير مفصولة، رمز مقترح مستخدم خارج الخريطة، حساب نقدية له أبناء، بنك تحت الصناديق، حساب افتراضي مفقود/في غير مكانه.

   التحقق داخل معاملة كل منشأة قبل الإتمام (أي فرق ← تراجع المنشأة وحدها):
     - رصيد كل حساب (Account.Balance بالمعرّف) قبل وبعد.
     - عدد أسطر القيود ومجموع (مدين − دائن) لكل حساب: بالرمز القديم قبل = بالرمز الجديد بعد.
     - عدد المراجع لكل حساب في كل عمود مرجعي: بالرمز القديم قبل = بالرمز الجديد بعد.
     - لا رمز مؤقت متبقٍ، ولا رمز مكرر في المنشأة.
   ===================================================================== */
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @DryRun bit = 1;                     -- 1 = تجربة مع تراجع، 0 = تنفيذ فعلي
DECLARE @OnlyTenant uniqueidentifier = NULL; -- منشأة واحدة، أو NULL للكل
DECLARE @FixMisplacedMainCash bit = 0;       -- 1 = إعادة 1111 من تحت 110 إلى 111 (المنشآت التي تحتاجه فقط)
DECLARE @Batch uniqueidentifier = NEWID();   -- معرّف هذا التشغيل في سجل الترحيل

/* ---------- سجل الترحيل (للتراجع) ---------- */
IF @DryRun = 0 AND OBJECT_ID('dbo.TreasuryRenumberLog') IS NULL
    CREATE TABLE dbo.TreasuryRenumberLog (
        Id bigint IDENTITY PRIMARY KEY,
        Batch uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NOT NULL,
        AccountId uniqueidentifier NOT NULL,
        OldCode nvarchar(64) NOT NULL,
        NewCode nvarchar(64) NOT NULL,
        OldParentCode nvarchar(64) NULL,
        NewParentCode nvarchar(64) NULL,
        AppliedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
        RolledBackAt datetime2 NULL);

/* ---------- الأعمدة المرجعية ---------- */
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

PRINT N'الأعمدة المرجعية:';
SELECT Tbl + '.' + Col AS [Column] FROM #cols ORDER BY Tbl, Col;

/* ---------- الخريطة ---------- */
IF OBJECT_ID('tempdb..#map') IS NOT NULL DROP TABLE #map;
;WITH sep AS (
    SELECT DISTINCT TenantId FROM [Account] WHERE Code = '110' AND IsSystem = 1
),
src AS (
    -- الأب الفعّال: 1111 النظامي تحت 110 يُعد تحت 111 عند التصحيح
    SELECT a.*, CASE WHEN @FixMisplacedMainCash = 1 AND a.Code = '1111' AND a.IsSystem = 1 AND a.ParentCode = '110'
                       AND EXISTS (SELECT 1 FROM [Account] g WHERE g.TenantId = a.TenantId AND g.Code = '111')
                     THEN '111' ELSE a.ParentCode END AS EffParent
    FROM [Account] a
),
kids AS (
    SELECT a.TenantId, a.Id, a.Code, a.ParentCode, a.EffParent AS GroupCode, a.NameAr, a.LinkedEntityType,
           ROW_NUMBER() OVER (PARTITION BY a.TenantId, a.EffParent ORDER BY
               CASE WHEN a.EffParent = '110' AND a.Code = '1112' THEN 1
                    WHEN a.EffParent = '110' AND a.Code = '1113' THEN 2
                    WHEN a.EffParent = '111' AND a.Code = '1111' THEN 1 ELSE 9 END,
               a.CreatedAt, LEN(a.Code), a.Code) AS Seq
    FROM src a
    JOIN sep s ON s.TenantId = a.TenantId
    WHERE a.EffParent IN ('110', '111') AND (@OnlyTenant IS NULL OR a.TenantId = @OnlyTenant)
)
SELECT TenantId, Id, Code AS OldCode, GroupCode, GroupCode + CAST(Seq AS varchar(10)) AS NewCode,
       ParentCode AS OldParent, NameAr, LinkedEntityType, Seq
INTO #map FROM kids;

/* ---------- المنشآت المستثناة (مطابقة لقواعد التدقيق) ---------- */
IF OBJECT_ID('tempdb..#blocked') IS NOT NULL DROP TABLE #blocked;
CREATE TABLE #blocked (TenantId uniqueidentifier, Reason nvarchar(400));

INSERT #blocked SELECT m.TenantId, N'رمز مقترح ' + m.NewCode + N' مستخدم للحساب ' + x.Code
FROM #map m JOIN [Account] x ON x.TenantId = m.TenantId AND x.Code = m.NewCode
WHERE NOT EXISTS (SELECT 1 FROM #map y WHERE y.TenantId = m.TenantId AND y.Id = x.Id);

INSERT #blocked SELECT m.TenantId, N'حساب نقدية له أبناء: ' + m.OldCode
FROM #map m WHERE EXISTS (SELECT 1 FROM [Account] c WHERE c.TenantId = m.TenantId AND c.ParentCode = m.OldCode);

INSERT #blocked SELECT m.TenantId, N'بنك تحت الصناديق: ' + m.OldCode
FROM #map m WHERE m.GroupCode = '111'
  AND (m.LinkedEntityType = 'Bank' OR EXISTS (SELECT 1 FROM [BankEntity] b WHERE b.TenantId = m.TenantId AND b.AccountCode = m.OldCode));

-- الافتراضيات بأبيها الفعّال في الخريطة (بعد التصحيح الاختياري)
INSERT #blocked SELECT t.TenantId, N'حساب افتراضي مفقود أو في غير مكانه: ' + d.Code
FROM (SELECT DISTINCT TenantId FROM #map) t
CROSS JOIN (VALUES ('1111', '111'), ('1112', '110'), ('1113', '110')) d(Code, Parent)
WHERE NOT EXISTS (SELECT 1 FROM #map m WHERE m.TenantId = t.TenantId AND m.OldCode = d.Code AND m.GroupCode = d.Parent);

PRINT N'المنشآت المستثناة:';
SELECT b.TenantId, tn.NameAr AS TenantName, b.Reason FROM #blocked b LEFT JOIN [Tenant] tn ON tn.Id = b.TenantId ORDER BY b.TenantId;

DELETE m FROM #map m WHERE EXISTS (SELECT 1 FROM #blocked b WHERE b.TenantId = m.TenantId);

/* ---------- نتائج ---------- */
IF OBJECT_ID('tempdb..#result') IS NOT NULL DROP TABLE #result;
CREATE TABLE #result (TenantId uniqueidentifier, Renumbered int, Outcome nvarchar(400));
IF OBJECT_ID('tempdb..#compare') IS NOT NULL DROP TABLE #compare;
CREATE TABLE #compare (TenantId uniqueidentifier, AccountId uniqueidentifier, OldCode nvarchar(64), NewCode nvarchar(64),
    LinesBefore bigint, LinesAfter bigint, NetBefore decimal(38, 6), NetAfter decimal(38, 6), BalanceBefore decimal(38, 6), BalanceAfter decimal(38, 6),
    RefsBefore bigint, RefsAfter bigint);

/* ---------- لكل منشأة: معاملة واحدة ---------- */
DECLARE @tenant uniqueidentifier, @sql nvarchar(max), @n int, @problems int, @lock int;
DECLARE tenants CURSOR LOCAL FAST_FORWARD FOR
    SELECT DISTINCT TenantId FROM #map WHERE OldCode <> NewCode OR OldParent <> GroupCode;
OPEN tenants;
FETCH NEXT FROM tenants INTO @tenant;
WHILE @@FETCH_STATUS = 0
BEGIN
    BEGIN TRY
        IF OBJECT_ID('tempdb..#tm') IS NOT NULL DROP TABLE #tm;
        SELECT Id, OldCode, NewCode, GroupCode, OldParent, Seq INTO #tm
        FROM #map WHERE TenantId = @tenant AND (OldCode <> NewCode OR OldParent <> GroupCode);
        SELECT @n = COUNT(*) FROM #tm;

        BEGIN TRANSACTION;

        -- ترحيل واحد في كل مرة (لا تشغيلان متزامنان)
        EXEC @lock = sp_getapplock @Resource = 'treasury_renumbering', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000;
        IF @lock < 0 THROW 51001, 'treasury renumbering lock timeout', 1;
        -- لا إنشاء حسابات أو قيود متزامنة في المنشأة أثناء ترحيلها
        SELECT @n = @n FROM [Account] WITH (TABLOCKX, HOLDLOCK) WHERE 1 = 0;

        /* --- لقطة قبل --- */
        IF OBJECT_ID('tempdb..#before') IS NOT NULL DROP TABLE #before;
        CREATE TABLE #before (Kind varchar(140), Id uniqueidentifier, Cnt bigint, Amount decimal(38, 6));
        INSERT #before SELECT 'balance', a.Id, 0, a.Balance FROM [Account] a JOIN #tm m ON m.Id = a.Id;
        INSERT #before SELECT 'lines', m.Id, COUNT(l.Id), ISNULL(SUM(l.Debit - l.Credit), 0)
            FROM #tm m LEFT JOIN [JournalEntryLine] l ON l.TenantId = @tenant AND l.AccountCode = m.OldCode GROUP BY m.Id;
        SET @sql = N'';
        SELECT @sql = @sql + N'
INSERT #before SELECT ' + QUOTENAME(Tbl + '.' + Col, '''') + N', m.Id, COUNT(r.' + QUOTENAME(Col) + N'), 0
FROM #tm m LEFT JOIN ' + QUOTENAME(Sch) + N'.' + QUOTENAME(Tbl) + N' r ON r.TenantId = @t AND r.' + QUOTENAME(Col) + N' = m.OldCode GROUP BY m.Id;'
        FROM #cols;
        EXEC sp_executesql @sql, N'@t uniqueidentifier', @t = @tenant;

        /* --- 0) تصحيح الأب (1111 من 110 إلى 111) — المستوى نفسه، الرمز كما هو --- */
        UPDATE a SET a.ParentCode = m.GroupCode, a.UpdatedAt = SYSUTCDATETIME()
        FROM [Account] a JOIN #tm m ON m.Id = a.Id WHERE a.TenantId = @tenant AND a.ParentCode <> m.GroupCode;

        /* --- 1) الرموز إلى رموز مؤقتة ثم النهائية (لا تصادم مع الفهرس الفريد أثناء التبادل 1112/1113) --- */
        UPDATE a SET a.Code = 'TMP' + m.NewCode, a.UpdatedAt = SYSUTCDATETIME()
        FROM [Account] a JOIN #tm m ON m.Id = a.Id WHERE a.TenantId = @tenant AND m.OldCode <> m.NewCode;
        UPDATE a SET a.Code = m.NewCode
        FROM [Account] a JOIN #tm m ON m.Id = a.Id WHERE a.TenantId = @tenant AND m.OldCode <> m.NewCode;

        /* --- 2) المراجع: تحديث واحد لكل عمود من القديم إلى الجديد (يُقيَّم على القيم السابقة، فالتبادل آمن) --- */
        SET @sql = N'';
        SELECT @sql = @sql + N'
UPDATE r SET r.' + QUOTENAME(Col) + N' = m.NewCode
FROM ' + QUOTENAME(Sch) + N'.' + QUOTENAME(Tbl) + N' r JOIN #tm m ON r.' + QUOTENAME(Col) + N' = m.OldCode AND m.OldCode <> m.NewCode
WHERE r.TenantId = @t;'
        FROM #cols;
        EXEC sp_executesql @sql, N'@t uniqueidentifier', @t = @tenant;

        /* --- 3) العدّادات: آخر تسلسل لكل مجموعة (لا يُعاد رقم ولا يتكرر) --- */
        MERGE [NumberSequence] AS s
        USING (
            SELECT 'treasury_code:' + GroupCode AS [Key], MAX(Seq) AS LastValue
            FROM #map WHERE TenantId = @tenant GROUP BY GroupCode
        ) AS v ON s.TenantId = @tenant AND s.[Key] = v.[Key]
        WHEN MATCHED AND s.LastValue < v.LastValue THEN UPDATE SET s.LastValue = v.LastValue, s.Version = NEWID(), s.UpdatedAt = SYSUTCDATETIME()
        WHEN NOT MATCHED THEN INSERT (Id, TenantId, [Key], Prefix, LastValue, Padding, Version, CreatedAt)
            VALUES (NEWID(), @tenant, v.[Key], '', v.LastValue, 0, NEWID(), SYSUTCDATETIME());

        /* --- 4) التحقق --- */
        IF OBJECT_ID('tempdb..#after') IS NOT NULL DROP TABLE #after;
        CREATE TABLE #after (Kind varchar(140), Id uniqueidentifier, Cnt bigint, Amount decimal(38, 6));
        INSERT #after SELECT 'balance', a.Id, 0, a.Balance FROM [Account] a JOIN #tm m ON m.Id = a.Id;
        INSERT #after SELECT 'lines', m.Id, COUNT(l.Id), ISNULL(SUM(l.Debit - l.Credit), 0)
            FROM #tm m LEFT JOIN [JournalEntryLine] l ON l.TenantId = @tenant AND l.AccountCode = m.NewCode GROUP BY m.Id;
        SET @sql = N'';
        SELECT @sql = @sql + N'
INSERT #after SELECT ' + QUOTENAME(Tbl + '.' + Col, '''') + N', m.Id, COUNT(r.' + QUOTENAME(Col) + N'), 0
FROM #tm m LEFT JOIN ' + QUOTENAME(Sch) + N'.' + QUOTENAME(Tbl) + N' r ON r.TenantId = @t AND r.' + QUOTENAME(Col) + N' = m.NewCode GROUP BY m.Id;'
        FROM #cols;
        EXEC sp_executesql @sql, N'@t uniqueidentifier', @t = @tenant;

        SELECT @problems = COUNT(*) FROM #before b
        FULL JOIN #after a ON a.Kind = b.Kind AND a.Id = b.Id
        WHERE a.Kind IS NULL OR b.Kind IS NULL OR a.Cnt <> b.Cnt OR a.Amount <> b.Amount;
        SET @problems = @problems
            + (SELECT COUNT(*) FROM [Account] WHERE TenantId = @tenant AND Code LIKE 'TMP%')
            + (SELECT COUNT(*) FROM (SELECT Code FROM [Account] WHERE TenantId = @tenant GROUP BY Code HAVING COUNT(*) > 1) d)
            + (SELECT COUNT(*) FROM #tm m JOIN [Account] a ON a.Id = m.Id WHERE a.Code <> m.NewCode OR a.ParentCode <> m.GroupCode);

        -- تقرير المقارنة لكل حساب (يُعرض في النهاية)
        INSERT #compare
        SELECT @tenant, m.Id, m.OldCode, m.NewCode,
               lb.Cnt, la.Cnt, lb.Amount, la.Amount, bb.Amount, ba.Amount,
               (SELECT SUM(Cnt) FROM #before x WHERE x.Id = m.Id AND x.Kind NOT IN ('balance', 'lines')),
               (SELECT SUM(Cnt) FROM #after x WHERE x.Id = m.Id AND x.Kind NOT IN ('balance', 'lines'))
        FROM #tm m
        JOIN #before lb ON lb.Id = m.Id AND lb.Kind = 'lines' JOIN #after la ON la.Id = m.Id AND la.Kind = 'lines'
        JOIN #before bb ON bb.Id = m.Id AND bb.Kind = 'balance' JOIN #after ba ON ba.Id = m.Id AND ba.Kind = 'balance';

        IF @problems > 0
        BEGIN
            SELECT @tenant AS TenantId, COALESCE(b.Kind, a.Kind) AS Kind, COALESCE(b.Id, a.Id) AS AccountId,
                   b.Cnt AS CntBefore, a.Cnt AS CntAfter, b.Amount AS AmountBefore, a.Amount AS AmountAfter
            FROM #before b FULL JOIN #after a ON a.Kind = b.Kind AND a.Id = b.Id
            WHERE a.Kind IS NULL OR b.Kind IS NULL OR a.Cnt <> b.Cnt OR a.Amount <> b.Amount;
            ROLLBACK TRANSACTION;
            INSERT #result VALUES (@tenant, @n, N'فشل التحقق (' + CAST(@problems AS nvarchar(10)) + N' فرق) — تراجع');
        END
        ELSE IF @DryRun = 1
        BEGIN
            ROLLBACK TRANSACTION;
            INSERT #result VALUES (@tenant, @n, N'تجربة ناجحة — تراجع (DryRun)');
        END
        ELSE
        BEGIN
            EXEC sp_executesql N'INSERT dbo.TreasuryRenumberLog (Batch, TenantId, AccountId, OldCode, NewCode, OldParentCode, NewParentCode)
                SELECT @b, @t, Id, OldCode, NewCode, OldParent, GroupCode FROM #tm;', N'@b uniqueidentifier, @t uniqueidentifier', @b = @Batch, @t = @tenant;
            COMMIT TRANSACTION;
            INSERT #result VALUES (@tenant, @n, N'تم');
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

PRINT N'النتيجة (Batch):';
SELECT @Batch AS Batch, @DryRun AS DryRun;
SELECT r.TenantId, tn.NameAr AS TenantName, r.Renumbered, r.Outcome
FROM #result r LEFT JOIN [Tenant] tn ON tn.Id = r.TenantId ORDER BY r.Outcome, TenantName;

PRINT N'مقارنة قبل/بعد لكل حساب:';
SELECT c.TenantId, c.OldCode, c.NewCode, c.LinesBefore, c.LinesAfter, c.NetBefore, c.NetAfter, c.BalanceBefore, c.BalanceAfter,
       c.RefsBefore, c.RefsAfter,
       CASE WHEN c.LinesBefore = c.LinesAfter AND c.NetBefore = c.NetAfter AND c.BalanceBefore = c.BalanceAfter AND c.RefsBefore = c.RefsAfter
            THEN N'مطابق' ELSE N'فرق' END AS [Check]
FROM #compare c ORDER BY c.TenantId, c.NewCode;
