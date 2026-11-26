/*
  حصر التكرارات القائمة قبل هجرة فهارس منع التكرار (DuplicateEntityValidator).
  استعلام قراءة فقط: لا ينشئ ولا يعدّل شيئاً. يُشغَّل على كل قاعدة بيانات منشآت (المشتركة وأي قاعدة منشأة مستقلة).

  القواعد المعتمدة نفسها:
  - الاسم: قص الأطراف، مسافة واحدة بين الكلمات، إزالة التطويل والتشكيل (U+064B–U+0652 وU+0670)، وأحرف صغيرة.
    لا توحيد للهمزات ولا التاء المربوطة ولا الألف المقصورة. الفارغ لا يُحسب.
  - رقم هوية الموظف: بلا مسافات وبأحرف كبيرة.
  - النطاق: المنشأة دائماً، والحساب داخل أبيه، ومركز التكلفة داخل أبيه، والبنك بعملته.

  الناتج: مجموعة لكل قيمة مكررة (الكيان، المنشأة، النطاق، الحقل، المفتاح، العدد، والرموز مرتبة بالأقدم).
  مسافات C# الخاصة النادرة (غير المسافة والجدولة وأسطر النهاية والمسافة غير القابلة للكسر) لا تُوحَّد هنا.
*/
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

WITH src AS (
    SELECT N'Account' AS Entity, TenantId, Id, Code, CreatedAt, CAST(ParentCode AS nvarchar(450)) AS Scope, N'NameAr' AS Field, CAST(NameAr AS nvarchar(450)) AS Value FROM Account
    UNION ALL SELECT N'Account', TenantId, Id, Code, CreatedAt, CAST(ParentCode AS nvarchar(450)), N'NameEn', NameEn FROM Account
    UNION ALL SELECT N'Customer', TenantId, Id, Code, CreatedAt, NULL, N'NameAr', NameAr FROM Customer
    UNION ALL SELECT N'Customer', TenantId, Id, Code, CreatedAt, NULL, N'NameEn', NameEn FROM Customer
    UNION ALL SELECT N'Supplier', TenantId, Id, Code, CreatedAt, NULL, N'NameAr', NameAr FROM Supplier
    UNION ALL SELECT N'Supplier', TenantId, Id, Code, CreatedAt, NULL, N'NameEn', NameEn FROM Supplier
    UNION ALL SELECT N'BankEntity', TenantId, Id, Code, CreatedAt, UPPER(Currency), N'NameAr', NameAr FROM BankEntity
    UNION ALL SELECT N'BankEntity', TenantId, Id, Code, CreatedAt, UPPER(Currency), N'NameEn', NameEn FROM BankEntity
    UNION ALL SELECT N'PaymentMethodItem', TenantId, Id, Code, CreatedAt, NULL, N'NameAr', NameAr FROM PaymentMethodItem
    UNION ALL SELECT N'PaymentMethodItem', TenantId, Id, Code, CreatedAt, NULL, N'NameEn', NameEn FROM PaymentMethodItem
    UNION ALL SELECT N'CostCenter', TenantId, Id, Code, CreatedAt, CAST(ParentId AS nvarchar(450)), N'NameAr', NameAr FROM CostCenter
    UNION ALL SELECT N'CostCenter', TenantId, Id, Code, CreatedAt, CAST(ParentId AS nvarchar(450)), N'NameEn', NameEn FROM CostCenter
    UNION ALL SELECT N'Warehouse', TenantId, Id, Code, CreatedAt, NULL, N'NameAr', NameAr FROM Warehouse
    UNION ALL SELECT N'Warehouse', TenantId, Id, Code, CreatedAt, NULL, N'NameEn', NameEn FROM Warehouse
    UNION ALL SELECT N'Branch', TenantId, Id, Code, CreatedAt, NULL, N'NameAr', NameAr FROM Branch
    UNION ALL SELECT N'Branch', TenantId, Id, Code, CreatedAt, NULL, N'NameEn', NameEn FROM Branch
    UNION ALL SELECT N'Employee', TenantId, Id, Code, CreatedAt, NULL, N'NationalId', NationalId FROM Employee
),
cleaned AS (
    -- مقارنة ثنائية (BIN2) حتى لا تُطابق ترتيبات العربية غير الحساسة للتشكيل حروفاً ليست في القاعدة
    SELECT s.*, LOWER(
        REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
        REPLACE(REPLACE(REPLACE(REPLACE(
            s.Value COLLATE Latin1_General_BIN2,
            NCHAR(9), N' '), NCHAR(10), N' '), NCHAR(13), N' '), NCHAR(160), N' '),
            NCHAR(1600), N''),                                                            -- التطويل
            NCHAR(1611), N''), NCHAR(1612), N''), NCHAR(1613), N''), NCHAR(1614), N''),   -- التنوين والفتحة
            NCHAR(1615), N''), NCHAR(1616), N''), NCHAR(1617), N''), NCHAR(1618), N''),   -- الضمة والكسرة والشدة والسكون
            NCHAR(1648), N'')                                                             -- الألف الخنجرية
    ) AS Spaced
    FROM src s
    WHERE s.Value IS NOT NULL
),
keyed AS (
    SELECT c.Entity, c.TenantId, c.Id, c.Code, c.CreatedAt, c.Scope, c.Field, c.Value,
        CASE WHEN c.Field = N'NationalId'
             THEN UPPER(REPLACE(c.Spaced, N' ', N''))
             -- طيّ المسافات المتتالية إلى مسافة واحدة (علامتان تحكميتان لا تظهران في الأسماء)
             ELSE LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(c.Spaced, N' ', NCHAR(1) + NCHAR(2)), NCHAR(2) + NCHAR(1), N''), NCHAR(1) + NCHAR(2), N' ')))
        END COLLATE Latin1_General_BIN2 AS NameKey
    FROM cleaned c
)
SELECT k.Entity, t.NameAr AS Tenant, k.TenantId, k.Scope, k.Field, k.NameKey,
       COUNT(*) AS Records,
       STRING_AGG(CONCAT(k.Code, N' «', k.Value, N'»'), N' | ') WITHIN GROUP (ORDER BY k.CreatedAt, k.Id) AS RecordsOldestFirst
FROM keyed k
LEFT JOIN Tenant t ON t.Id = k.TenantId
WHERE k.NameKey <> N''
GROUP BY k.Entity, t.NameAr, k.TenantId, k.Scope, k.Field, k.NameKey
HAVING COUNT(*) > 1
ORDER BY k.Entity, t.NameAr, k.Field, Records DESC, k.NameKey;
