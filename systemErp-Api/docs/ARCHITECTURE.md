# المعمارية

ثلاثة مشاريع فقط، والاعتماد باتجاه واحد:

```
ERP.Api ──▶ ERP.Service ──▶ ERP.Core
```

| المشروع | المحتوى | يحوي |
|---|---|---|
| **ERP.Core** | لا يعتمد على شيء (لا EF ولا ASP.NET) | `Models/{Shared,Accounting,CarShowroom,POS}` · `DTOs/…` · `Contracts/…` (واجهات الخدمات) |
| **ERP.Service** | يطبّق العقود | `Services/{Shared,Accounting,CarShowroom,POS}` · `Data/ErpDbContext` + `Data/Migrations` · `ServiceCollectionExtensions` (تسجيل تلقائي لكل خدمة) |
| **ERP.Api** | HTTP فقط | `Controllers/{Shared,Accounting,CarShowroom,POS}` · `Infrastructure` (أخطاء موحّدة، middleware، `[RequireScreen]`) |

`tests/ERP.Tests` اختبارات تكامل على SQL Server + اختبارات قواعد المعمارية (لا يُحسب ضمن المشاريع الثلاثة).

## الأنظمة الثلاثة وما هو مشترك

```
                 ┌──────────────── Shared ────────────────┐
                 │ Tenant(=Company) · User · Permissions   │
                 │ Customer · Supplier · Bank · PaymentMethod · Currency │
                 │ Product/Category/Unit/Warehouse/StockMovement (المخزون) │
                 │ Approvals · Notifications · Costing · NumberSequence   │
                 └────────────────────┬───────────────────┘
        ┌──────────────────┬──────────┴────────┬───────────────────┐
   Accounting            CarShowroom            POS
   الحسابات والقيود      المركبات (VIN)         الورديات، البيع،
   السندات، الفواتير     دورة الشراء (7)        الكوبونات، الولاء،
   العقود، التسليم       دورة البيع (5)         المرتجعات
   التقارير
```

- **لا تكرار للكيانات المشتركة**: عميل واحد يُستخدم في المحاسبة والسيارات وPOS، وكذلك المورد والبنك وطريقة الدفع.
- **السيارة ليست صنفاً**: `Vehicle` كيان مستقل بهوية VIN ولا يمرّ عبر `Product`.
- المخزون (`Product`…) وضعناه في Shared لأن فواتير المحاسبة وPOS يستخدمانه معاً.

## الترحيل المحاسبي المركزي

الوحدات التشغيلية **لا تبني قيوداً**؛ تصف الأثر المالي فقط لـ `IAccountingPostingService`:

```
POS checkout ─┐
Car sale     ─┼─▶ IInvoiceService ─▶ (IInventoryService: حركة مخزون وتكلفة)
Car purchase ─┤                    └▶ IAccountingPostingService ─▶ JournalEntry + أرصدة الحسابات/العملاء/الموردين/البنوك
Contracts    ─┘
Vouchers ─────────────────────────────▶ IAccountingPostingService
```

- قواعد الحسابات (112 عملاء، 211 موردون، 213 ضريبة مخرجات، 411/412 إيراد، 511/512 تكلفة، 1141/1142 مخزون…) في مكان واحد: `DefaultAccounts`.
- كل عملية متعددة الخطوات داخل معاملة واحدة (`ITransactionRunner`): إن فشلت خطوة يُتراجع عن الكل (مغطّى باختبار).
- التسعير والضريبة والإجماليات **تُحسب في الخادم دائماً** (`DocumentPricing`)؛ أرقام العميل تُتجاهل.

## العزل بين المنشآت (Multi-tenancy)

- المنشأة تُؤخذ من مطالبة `tenant_id` في الـ JWT فقط (لا ترويسة قابلة للتزوير).
- مرشّح استعلام عالمي على كل كيان (`TenantId == CurrentTenantId`) + فهرس على `TenantId` + فهارس فريدة على مستوى المنشأة (الأكواد وأرقام المستندات).
- `SaveChanges` يختم `TenantId` ويرفض حفظ سجل من منشأة أخرى. مغطّى باختبارات العزل.

## الأمان

JWT (HS256، مفتاح من الإعدادات فقط) · PBKDF2 عبر `PasswordHasher` · صلاحيات شاشات `[RequireScreen(screen, action)]` (تطابق منطق الواجهة) ·
CORS بقائمة أصول صريحة · أخطاء موحّدة بلا stack trace خارج التطوير · لا كلمات مرور/رموز/أسرار ZATCA في أي استجابة.

## قاعدة البيانات

SQL Server + EF Core 9. الـ migrations في `ERP.Service/Data/Migrations`. **لا ترحيل تلقائي** إلا بـ `Database:AutoMigrate=true` (مفعّل في Development فقط).

```bash
dotnet ef migrations add <Name> --project src/ERP.Service --output-dir Data/Migrations
dotnet ef database update --project src/ERP.Service     # ERP_CONNECTION يحدّد قاعدة التصميم
```

## أرقام المستندات

`NumberSequence` لكل منشأة ومفتاح (فاتورة، قيد، سند، …) بتزامن متفائل وإعادة محاولة → لا أرقام مكررة تحت الضغط.

## التقارير وخيارات DevExtreme

كل تقرير قائمي (ميزان المراجعة، حركات كشف الحساب، جرد/حركة/كارت الصنف، التجارة، وتقارير السيارات الثمانية) يقبل خيارات
`DataSourceLoadOptions` من الـ query string (`filter`, `sort`, `skip`, `take`, `group`, `totalSummary`, `groupSummary`, `select`,
`requireTotalCount`, `requireGroupCount`) بالإضافة إلى مرشّحات التقرير (`dateFrom`/`dateTo`/`itemId`...) ويُعيد `LoadResult`
(`{ data, totalCount, summary, groupCount }`) **بدون** مغلّف `ApiResponse`، وهو ما يتوقعه `CustomStore.load`.
التطبيق في `ReportLoader` (Service) والربط في `DataSourceLoadOptionsBinder` (Api). خيار غير صالح (حقل غير موجود) → 400.
التقارير المفردة (`financial-stats`, `financial-summary`, `vat-return`, رأس كشف الحساب) تبقى بمغلّف `ApiResponse`.
ملاحظة: التقارير تُحسب في الذاكرة ثم تُطبَّق عليها الخيارات؛ للأحجام الكبيرة جداً تُنقل المرشّحات إلى استعلام قاعدة البيانات.

## تصحيح المستندات المالية (تعديل/حذف/إلغاء)

كل المستندات المالية قابلة للتصحيح، والأثر المحاسبي يمرّ دائماً عبر محرك واحد بمعاملة واحدة:

| المستند | التعديل | الحذف / الإلغاء |
|---|---|---|
| فاتورة مرحّلة | `PUT /invoices/{id}`: يُعكس القيد والمخزون ثم تُرحَّل من جديد بنفس الرقم (`status=draft` تعيدها مسودة) | `DELETE`: يُعكس القيد والمخزون ثم تُحذف. يُرفض ما أُرسل لهيئة الزكاة أو له مرتجعات، وما تملكه وحدة أخرى (POS، سيارات، عقود) |
| قيد آلي | `PUT /journalentries/{id}` (يُوسَم "مُعدَّل يدوياً") | `DELETE` يفكّ ارتباطه بالفاتورة/السند؛ قيد العكس نفسه لا يُعدَّل |
| حركة مخزون | `PUT /stockmovements/{id}` للتسويات اليدوية فقط، ثم إعادة احتساب التكلفة (Replay) | `DELETE` بنفس القيد؛ يُرفض ما يجعل الرصيد سالباً وفق سياسة المخزون |
| وردية POS | `PUT /pos/shifts/{id}` (الجهاز، الافتتاحي، جرد الإغلاق، ويُعاد حساب الفرق) | `DELETE ?cascade=true` يحذف معاملاتها ومرتجعاتها بعكس أثرها (أدوار إدارية) |
| معاملة POS | `PUT /pos/transactions/{id}` بيانات العميل فقط | `POST /{id}/void` أو `DELETE`: يعكس الفاتورة والمخزون والوردية والكوبون والولاء؛ يُرفض إن وُجدت مرتجعات |
| مرتجع POS | `PUT /pos/returns/{id}` السبب فقط | `DELETE`: يحذف إشعاره الدائن ويعيد الوردية والولاء وحالة المعاملة |

تصحيح مستند في وردية مغلقة أو لكاشير آخر يتطلب دوراً إدارياً (owner/admin/general_manager/chief_accountant).
