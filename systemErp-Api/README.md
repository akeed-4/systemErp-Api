# ERP Backend — ASP.NET Core 9 / EF Core 9 / SQL Server

Backend لثلاثة أنظمة تعمل فوق أساس مشترك: **المحاسبة العامة** و**معرض السيارات** و**نقاط البيع (POS)**،
مبني حول واجهة Angular الموجودة (`SystemErp-web`) التي هي مصدر الحقيقة للمتطلبات.

```
src/
├── ERP.Api/         Controllers/<System>/<Feature>/XController.cs  (ملف لكل controller)
├── ERP.Service/     Services/<System>/<Feature>/XService.cs · Data/ (DbContext + Migrations)
└── ERP.Core/        Models · DTOs · Contracts  ← كلٌّ منها: <System>/<Feature>/<Type>.cs (ملف لكل نوع)
tests/ERP.Tests/     Shared · Accounting · CarShowroom · POS · Architecture
docs/                ARCHITECTURE · FRONTEND_MAPPING · API_ENDPOINTS
```

مثال: بيانات العملاء الأساسية = `Controllers/Shared/MasterData/CustomersController.cs` + `Services/Shared/MasterData/CustomerService.cs` + `Contracts/Shared/MasterData/ICustomerService.cs` + `DTOs/Shared/MasterData/{CustomerDto,CreateCustomerDto,UpdateCustomerDto}.cs` + `Models/Shared/MasterData/Customer.cs`. الأنظمة: Shared · Accounting · CarShowroom · POS.

## التشغيل

```bash
# 1) الإعدادات (أسرار خارج المستودع)
dotnet user-secrets init --project src/ERP.Api
dotnet user-secrets set "Jwt:Key" "<32+ chars>" --project src/ERP.Api
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<sql server>" --project src/ERP.Api

# 2) قاعدة البيانات
dotnet ef database update --project src/ERP.Service

# 3) التشغيل (Development يفعّل Swagger والترحيل التلقائي و CORS لـ http://localhost:4200)
dotnet run --project src/ERP.Api
```

في Development يوجد مفتاح JWT تجريبي في `appsettings.Development.json` — **لا تستخدمه خارج جهازك**.

## الاختبارات

```bash
dotnet test        # 85 اختباراً؛ تحتاج SQL Server LocalDB أو ERP_TEST_CONNECTION
```

كل اختبار يسجّل منشأة معزولة على قاعدة مؤقتة تُنشأ بالـ migrations وتُحذف بعد الانتهاء.

## ما يغطيه

- **مشترك:** المصادقة (JWT، تسجيل منشأة، استعادة كلمة المرور OTP)، الصلاحيات حسب الشاشة، العملاء/الموردون/البنوك (بحساب أستاذ تلقائي)، الأصناف والمخزون بأربع طرق تكلفة، الموافقات، الإشعارات، الاشتراكات.
- **المحاسبة:** شجرة الحسابات، قيود يدوية/آلية مع العكس، السندات، فواتير البيع/الشراء/المرتجعات (ZATCA QR)، عروض الأسعار، طلبات الشراء، الأوامر، الاتفاقيات، العقود بمراحلها والمستخلصات، بيانات التسليم، ميزان المراجعة وكشف الحساب والإقرار الضريبي وتقارير المخزون.
- **معرض السيارات:** بيانات الماركات→الموديلات→الفئات→السنوات، المركبات (VIN)، دورة الشراء (7 مراحل) بإنشاء المركبات وترحيل التكاليف المحمّلة، دورة البيع (5 مراحل) بحجز المركبة وضريبة هامش الربح، 8 تقارير.
- **POS:** الورديات وفرق الصندوق، البيع بتسعير وعروض وكوبونات في الخادم، الدفع المقسّم، الولاء، السلال المعلّقة، المرتجعات، الإعدادات — والبيع فاتورة محاسبية حقيقية (مصدر واحد للحقيقة).

التفاصيل: [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) · [`docs/FRONTEND_MAPPING.md`](docs/FRONTEND_MAPPING.md) (يتضمن قائمة غير المنفَّذ) · [`docs/API_ENDPOINTS.md`](docs/API_ENDPOINTS.md).
