/* =====================================================================
   تدقيق ترقيم البنوك والصناديق — قراءة فقط (SELECT وجداول مؤقتة #). لا يعدّل أي بيانات.
   القرارات المعتمدة:
     - البنوك تحت 110: 1101، 1102، ... 1109، 11010، 11011 ... (الافتراضي 1112 ← الأول، البوابة 1113 ← الثاني)
     - الصناديق تحت 111: 1111، 1112، ... 1119، 11110 ...           (الرئيسي 1111 ← الأول)
     - الباقي بتاريخ الإنشاء ثم الرمز.
     - المنشأة ذات التعارض تُستثنى (Blocked) وتُعرض للمراجعة اليدوية.
   الأقسام:
     0) ملخص المنشآت وحالتها        1) الخريطة الحالية/المقترحة
     2) التعارضات بالتفصيل            3) المراجع لكل حساب (كل عمود نصي)
     4) مراجع داخل نصوص طويلة/JSON   5) العدّادات
   ===================================================================== */
SET NOCOUNT ON;

IF OBJECT_ID('tempdb..#map') IS NOT NULL DROP TABLE #map;
IF OBJECT_ID('tempdb..#issues') IS NOT NULL DROP TABLE #issues;
IF OBJECT_ID('tempdb..#refs') IS NOT NULL DROP TABLE #refs;
IF OBJECT_ID('tempdb..#textrefs') IS NOT NULL DROP TABLE #textrefs;

CREATE TABLE #issues (TenantId uniqueidentifier, Kind nvarchar(60), Code varchar(32) NULL, Detail nvarchar(400));

/* ---------- الخريطة: الأبناء المباشرون لـ 110 (نظام) و111 ---------- */
;WITH sep AS (
    SELECT DISTINCT TenantId FROM [Account] WHERE Code = '110' AND IsSystem = 1
),
kids AS (
    SELECT a.TenantId, a.Id, a.Code, a.ParentCode AS GroupCode, a.NameAr, a.IsSystem, a.IsGroup,
           a.LinkedEntityType, a.Balance, a.CreatedAt,
           ROW_NUMBER() OVER (PARTITION BY a.TenantId, a.ParentCode ORDER BY
               CASE WHEN a.ParentCode = '110' AND a.Code = '1112' THEN 1
                    WHEN a.ParentCode = '110' AND a.Code = '1113' THEN 2
                    WHEN a.ParentCode = '111' AND a.Code = '1111' THEN 1 ELSE 9 END,
               a.CreatedAt, LEN(a.Code), a.Code) AS Seq
    FROM [Account] a
    JOIN sep s ON s.TenantId = a.TenantId
    WHERE a.ParentCode IN ('110', '111')
)
SELECT TenantId, Id, Code AS OldCode, GroupCode, GroupCode + CAST(Seq AS varchar(10)) AS NewCode,
       NameAr, IsSystem, IsGroup, LinkedEntityType, Balance, CreatedAt, Seq
INTO #map FROM kids;

/* ---------- التعارضات ---------- */
-- أ) منشأة غير مفصولة: 111 موجود و110 ليس حساب نظام (حساب للمستخدم أو غير موجود)
INSERT #issues
SELECT a.TenantId, N'غير مفصولة (110 ليس حساب نظام)', '110',
       ISNULL((SELECT TOP 1 N'110 = ' + z.NameAr FROM [Account] z WHERE z.TenantId = a.TenantId AND z.Code = '110'), N'لا يوجد 110')
FROM [Account] a
WHERE a.Code = '111'
  AND NOT EXISTS (SELECT 1 FROM [Account] g WHERE g.TenantId = a.TenantId AND g.Code = '110' AND g.IsSystem = 1);

-- ب) رمز مقترح يستخدمه حساب خارج الخريطة
INSERT #issues
SELECT m.TenantId, N'رمز مقترح مستخدم خارج الخريطة', m.NewCode,
       m.OldCode + N' ← ' + m.NewCode + N' محجوز للحساب ' + x.Code + N' (' + x.NameAr + N'، الأب ' + ISNULL(x.ParentCode, '-') + N')'
FROM #map m
JOIN [Account] x ON x.TenantId = m.TenantId AND x.Code = m.NewCode
WHERE NOT EXISTS (SELECT 1 FROM #map y WHERE y.TenantId = m.TenantId AND y.Id = x.Id);

-- ج) حساب نقدية له أبناء (يتطلب إعادة ترقيم أعمق)
INSERT #issues
SELECT m.TenantId, N'حساب نقدية له أبناء', m.OldCode, N'الابن ' + c.Code + N' - ' + c.NameAr
FROM #map m JOIN [Account] c ON c.TenantId = m.TenantId AND c.ParentCode = m.OldCode;

-- د) بنك مرتبط تحت الصناديق 111 (سيُرقَّم صندوقاً)
INSERT #issues
SELECT m.TenantId, N'بنك تحت الصناديق', m.OldCode, m.NameAr
FROM #map m
WHERE m.GroupCode = '111'
  AND (m.LinkedEntityType = 'Bank'
       OR EXISTS (SELECT 1 FROM [BankEntity] b WHERE b.TenantId = m.TenantId AND b.AccountCode = m.OldCode));

-- هـ) الافتراضيات مفقودة أو في غير مكانها
INSERT #issues
SELECT t.TenantId, N'حساب افتراضي في غير مكانه', d.Code,
       ISNULL(N'الأب الحالي ' + a.ParentCode, N'غير موجود')
FROM (SELECT DISTINCT TenantId FROM #map) t
CROSS JOIN (VALUES ('1111', '111'), ('1112', '110'), ('1113', '110')) d(Code, Parent)
LEFT JOIN [Account] a ON a.TenantId = t.TenantId AND a.Code = d.Code
WHERE a.Id IS NULL OR ISNULL(a.ParentCode, '') <> d.Parent;

/* ---------- 0) ملخص المنشآت ---------- */
PRINT '=== 0) ملخص المنشآت ===';
SELECT x.TenantId, tn.NameAr AS TenantName,
       (SELECT COUNT(*) FROM #map m WHERE m.TenantId = x.TenantId AND m.GroupCode = '110') AS Banks,
       (SELECT COUNT(*) FROM #map m WHERE m.TenantId = x.TenantId AND m.GroupCode = '111') AS CashBoxes,
       (SELECT COUNT(*) FROM #map m WHERE m.TenantId = x.TenantId AND m.OldCode <> m.NewCode) AS ToRenumber,
       (SELECT COUNT(*) FROM #issues i WHERE i.TenantId = x.TenantId) AS Issues,
       CASE WHEN EXISTS (SELECT 1 FROM #issues i WHERE i.TenantId = x.TenantId) THEN N'Blocked - مراجعة يدوية'
            ELSE N'Eligible - ترحيل تلقائي' END AS [Status]
FROM (SELECT DISTINCT TenantId FROM [Account] WHERE Code IN ('110', '111')) x
LEFT JOIN [Tenant] tn ON tn.Id = x.TenantId
ORDER BY [Status], TenantName;

/* ---------- 1) الخريطة ---------- */
PRINT '=== 1) الخريطة الحالية والمقترحة ===';
SELECT m.TenantId, m.GroupCode, m.Seq, m.OldCode, m.NewCode,
       CASE WHEN m.OldCode = m.NewCode THEN N'بلا تغيير' ELSE N'يتغير' END AS [Change],
       m.NameAr, m.IsSystem, m.IsGroup, m.LinkedEntityType, m.Balance, m.CreatedAt
FROM #map m ORDER BY m.TenantId, m.GroupCode, m.Seq;

/* ---------- 2) التعارضات ---------- */
PRINT '=== 2) التعارضات والحالات غير القابلة للترحيل التلقائي ===';
SELECT i.TenantId, tn.NameAr AS TenantName, i.Kind, i.Code, i.Detail
FROM #issues i LEFT JOIN [Tenant] tn ON tn.Id = i.TenantId
ORDER BY TenantName, i.Kind, i.Code;

/* ---------- 3) المراجع لكل حساب (تطابق تام في كل عمود نصي) ---------- */
PRINT '=== 3) المراجع لكل حساب يتغير رمزه ===';
CREATE TABLE #refs (TableName sysname, ColumnName sysname, TenantId uniqueidentifier NULL, OldCode varchar(32), Cnt int);

DECLARE @sql nvarchar(max) = N'';
SELECT @sql = @sql + N'
INSERT #refs SELECT ' + QUOTENAME(c.TABLE_NAME, '''') + N', ' + QUOTENAME(c.COLUMN_NAME, '''') + N', '
    + CASE WHEN t.COLUMN_NAME IS NOT NULL THEN N'r.TenantId' ELSE N'NULL' END + N', m.OldCode, COUNT(*)
FROM ' + QUOTENAME(c.TABLE_SCHEMA) + N'.' + QUOTENAME(c.TABLE_NAME) + N' r
JOIN #map m ON m.OldCode = r.' + QUOTENAME(c.COLUMN_NAME) + N' COLLATE DATABASE_DEFAULT AND m.OldCode <> m.NewCode'
    + CASE WHEN t.COLUMN_NAME IS NOT NULL THEN N' AND m.TenantId = r.TenantId' ELSE N'' END + N'
GROUP BY ' + CASE WHEN t.COLUMN_NAME IS NOT NULL THEN N'r.TenantId, ' ELSE N'' END + N'm.OldCode;'
FROM INFORMATION_SCHEMA.COLUMNS c
JOIN INFORMATION_SCHEMA.TABLES tb ON tb.TABLE_SCHEMA = c.TABLE_SCHEMA AND tb.TABLE_NAME = c.TABLE_NAME AND tb.TABLE_TYPE = 'BASE TABLE'
LEFT JOIN INFORMATION_SCHEMA.COLUMNS t ON t.TABLE_SCHEMA = c.TABLE_SCHEMA AND t.TABLE_NAME = c.TABLE_NAME AND t.COLUMN_NAME = 'TenantId'
WHERE c.DATA_TYPE IN ('nvarchar', 'varchar', 'nchar', 'char')
  AND (c.CHARACTER_MAXIMUM_LENGTH = -1 OR c.CHARACTER_MAXIMUM_LENGTH >= 4)
  AND c.TABLE_NAME NOT LIKE '\_\_%' ESCAPE '\'
  AND NOT (c.TABLE_NAME = 'Account' AND c.COLUMN_NAME = 'Code');
EXEC sp_executesql @sql;

-- تفصيل: منشأة × حساب × جدول.عمود
SELECT r.TenantId, r.OldCode, m.NewCode, m.NameAr, r.TableName + '.' + r.ColumnName AS [Column], r.Cnt AS Refs
FROM #refs r
LEFT JOIN #map m ON m.TenantId = r.TenantId AND m.OldCode = r.OldCode
ORDER BY r.TenantId, r.OldCode, r.Cnt DESC;

-- إجمالي حسب العمود (لمعرفة الأعمدة التي يجب أن تحدّثها الهجرة)
SELECT TableName + '.' + ColumnName AS [Column], SUM(Cnt) AS Refs, COUNT(DISTINCT TenantId) AS Tenants
FROM #refs GROUP BY TableName, ColumnName ORDER BY Refs DESC;

/* ---------- 4) ظهور الرموز داخل نصوص طويلة / JSON (تطابق جزئي، للمراجعة اليدوية) ---------- */
PRINT '=== 4) رموز داخل نصوص nvarchar(max) — تطابق جزئي ===';
CREATE TABLE #textrefs (TableName sysname, ColumnName sysname, OldCode varchar(32), Cnt int);
SET @sql = N'';
SELECT @sql = @sql + N'
INSERT #textrefs SELECT ' + QUOTENAME(c.TABLE_NAME, '''') + N', ' + QUOTENAME(c.COLUMN_NAME, '''') + N', m.OldCode, COUNT(*)
FROM ' + QUOTENAME(c.TABLE_SCHEMA) + N'.' + QUOTENAME(c.TABLE_NAME) + N' r
JOIN #map m ON m.OldCode <> m.NewCode
  AND r.' + QUOTENAME(c.COLUMN_NAME) + N' LIKE ''%"'' + m.OldCode + ''"%''
GROUP BY m.OldCode;'
FROM INFORMATION_SCHEMA.COLUMNS c
JOIN INFORMATION_SCHEMA.TABLES tb ON tb.TABLE_SCHEMA = c.TABLE_SCHEMA AND tb.TABLE_NAME = c.TABLE_NAME AND tb.TABLE_TYPE = 'BASE TABLE'
WHERE c.DATA_TYPE IN ('nvarchar', 'varchar') AND c.CHARACTER_MAXIMUM_LENGTH = -1
  AND c.TABLE_NAME NOT LIKE '\_\_%' ESCAPE '\';
EXEC sp_executesql @sql;
SELECT TableName + '.' + ColumnName AS [Column], OldCode, SUM(Cnt) AS RowsContaining
FROM #textrefs GROUP BY TableName, ColumnName, OldCode ORDER BY RowsContaining DESC;

/* ---------- 5) العدّادات ---------- */
PRINT '=== 5) العدّادات الحالية ===';
SELECT TenantId, [Key], LastValue FROM [NumberSequence]
WHERE [Key] LIKE 'account_child_code:110:%' OR [Key] LIKE 'account_child_code:111:%'
ORDER BY TenantId, [Key];
