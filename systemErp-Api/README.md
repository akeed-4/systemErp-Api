# Rayah Accounting ERP - .NET 9 Clean Architecture Enterprise Backend
## نظام الراية المحاسبي المتكامل - باك إند مؤسسي مبني على .NET 9 بتقنية المعمارية النظيفة (Clean Architecture)

نظام متكامل وقابل للتوسع مصمم للشركات والمؤسسات المتوسطة والكبرى وفق أفضل المعايير الهندسية لـ .NET Core 9 و Entity Framework Core، متوافق كلياً مع متطلبات **هيئة الزكاة والضريبة والجمارك (ZATCA) - المرحلة الثانية (الربط والتكامل)**.

---

### المعمارية المؤسسية للمشروع (Clean Architecture Solution Structure)

```text
backend/
├── RayahAccounting.sln
└── src/
    ├── RayahAccounting.Domain/            # طبقة المجال وقواعد الأعمال المجردة (Enterprise Domain)
    │   ├── Common/                        # BaseEntity & ITenantEntity (Multi-Tenant Isolation)
    │   ├── Entities/                      # Tenant, Account, Customer, Supplier, Product,
    │   │                                  # Invoice, InvoiceItem, Voucher, JournalEntry,
    │   │                                  # JournalEntryLine, StockMovement, Warehouse, AuditLog
    │   └── Enums/                         # InvoiceType, VoucherType, PaymentMethod, AccountCategory,
    │                                      # StockMovementType, JournalEntryStatus, ZatcaPhase2Status
    │
    ├── RayahAccounting.Application/       # طبقة منطق التطبيق والخدمات (Use Cases & Application Logic)
    │   ├── Interfaces/                    # IApplicationDbContext, IAccountingService, IInventoryService,
    │   │                                  # IReportsService, ITenantService
    │   ├── Accounting/                    # محرك القيود المزدوجة المتوازنة وموازين المراجعة والقوائم المالية
    │   ├── Inventory/                     # نظام حركة المخزون وحساب المتوسط المرجح للتكلفة (Moving Average Cost)
    │   └── Reports/                       # إقرار ضريبة القيمة المضافة 15% المعتمد في المملكة (ZATCA VAT Return)
    │
    ├── RayahAccounting.Infrastructure/    # طبقة البنية التحتية والبيانات والربط الخارجي
    │   ├── Persistence/                   # ApplicationDbContext مع Global Multi-Tenant Query Filter
    │   ├── MultiTenancy/                  # TenantResolverMiddleware (قراءة ترويسة X-Tenant-Id وتعيين المستأجر)
    │   └── Zatca/                         # UBL 2.1 XML, SHA-256 Hashing, TLV Base64 QR Code & ECDSA Signature
    │
    └── RayahAccounting.WebApi/            # طبقة واجهات برمجة التطبيقات (RESTful Web API)
        ├── Controllers/
        │   ├── AccountsController.cs      # دليل شجرة الحسابات، الأرصدة، وكشف الحساب
        │   ├── CustomersController.cs     # إدارة سجل العملاء والائتمان والأرصدة
        │   ├── SuppliersController.cs     # إدارة سجل الموردين وفترات السداد
        │   ├── ProductsController.cs      # كتالوج المنتجات، الباركود، ومحاكي متوسط التكلفة
        │   ├── InvoicesController.cs      # فواتير المبيعات والمشتريات والربط مع هيئة الزكاة والقيود الآلية
        │   ├── VouchersController.cs      # سندات القبض والصرف، والتفقيط بالعربية
        │   ├── JournalEntriesController.cs# قيود اليومية المتوازنة وقيد العكس الآلي
        │   ├── StockMovementsController.cs# حركات المخزون والمناقلات المستودعية
        │   ├── ReportsController.cs       # ميزان المراجعة، الملخص المالي، وإقرار الضريبة 15%
        │   ├── DatabaseSyncController.cs  # المزامنة الشاملة للجداول وفحص الصحة
        │   ├── ZatcaController.cs         # محاكاة الربط مع منصة فاتورة (Fatoora Portal)
        │   └── TenantsController.cs       # إدارة الشركات والمستأجرين المتعددين
        ├── Program.cs                     # Dependency Injection, Middleware, Swagger UI
        └── appsettings.json               # إعدادات سلاسل الاتصال والخيارات
```

---

### العمليات المتاحة في الـ Web API (.NET 9 Endpoints)

| المسار | الطريقة | الوصف والعملية المحاسبية |
| :--- | :---: | :--- |
| `/api/v1/accounts` | `GET, POST` | استعراض وإضافة حسابات شجرة الحسابات المالية |
| `/api/v1/accounts/{code}/statement` | `GET` | كشف حساب تفصيلي بحركات المدين والدائن والرصيد المتحرك |
| `/api/v1/invoices` | `GET, POST` | إصدار فواتير المبيعات/المشتريات، وتوليد القيد الآلي وحركة المخزون وتشفير ZATCA |
| `/api/v1/invoices/{id}` | `DELETE` | حذف الفاتورة من قاعدة البيانات |
| `/api/v1/vouchers` | `GET, POST` | إنشاء سندات القبض والصرف مع توليد القيد المحاسبي المزدوج |
| `/api/v1/customers` | `GET, POST, PUT` | إدارة العملاء، فحص الأرقام الضريبية وتحديث الأرصدة |
| `/api/v1/suppliers` | `GET, POST, PUT` | إدارة الموردين، وتتبع المستحقات وشروط الدفع |
| `/api/v1/products` | `GET, POST, PUT` | كتالوج الأصناف والمخزون، وفحص حد إعادة الطلب |
| `/api/v1/products/{id}/simulate-cost`| `POST` | محاكي المتوسط المرجح للتكلفة (Weighted Average Cost Simulator) |
| `/api/v1/journalentries` | `GET, POST` | إدخال القيود المحاسبية اليدوية مع التحقق الإلزامي من توازن المدين والدائن |
| `/api/v1/journalentries/{id}/reverse`| `POST` | عكس القيد المحاسبي آلياً لحفظ تسلسل التدقيق |
| `/api/v1/stockmovements` | `GET, POST` | تسجيل حركات الوارد والمنصرف والمناقلات بين المستودعات |
| `/api/v1/reports/trial-balance` | `GET` | ميزان المراجعة بالمجاميع والأرصدة |
| `/api/v1/reports/vat-return` | `GET` | إقرار ضريبة القيمة المضافة 15% المعتمد في السعودية |
| `/api/v1/database/status` | `GET` | فحص حالة الاتصال وحجم الجداول وإحصائيات السجلات |
| `/api/v1/database/sync` | `POST` | مزامنة شاملة لكافة الكيانات والجداول |

---

### طريقة التشغيل مع .NET CLI

```bash
# الانتقال إلى مجلد الباك إند
cd backend

# استعادة الحزم
dotnet restore

# بناء كامل الحل ومشاريع Clean Architecture
dotnet build

# تشغيل خادم Web API
dotnet run --project src/RayahAccounting.WebApi
```

عند التشغيل يفتح الـ Swagger UI على المسار:
`https://localhost:5001/swagger` أو على المنفذ المحدد.
