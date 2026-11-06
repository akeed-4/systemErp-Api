# خريطة مشروع الخادم (.NET ERP)

هذه نقطة بداية مختصرة للمطور. الكود هو المرجع التنفيذي؛ عند اختلاف الشرح عنه اتبع الكود وحدّث الدليل إذا تغيرت البنية أو القواعد العامة.

## التقنية والتشغيل

- الحل `ERP.sln` مبني على ASP.NET Core وEF Core 9 وSQL Server.
- `ERP.Api/Erp.Api.csproj` هو تطبيق HTTP؛ الإعدادات والأسرار تأتي من إعدادات البيئة/مخزن الأسرار.
- راجع `README.md` لتعليمات الإعداد والتشغيل. لا تنسخ أسرار اتصال أو JWT إلى ملفات التوثيق أو المستودع.
- الهجرات في `ERP.Service/Data/Migrations`. لا تنشئ migration إلا عندما يتغير نموذج قاعدة البيانات.

## المعمارية واتجاه الاعتماد

```text
ERP.Api → ERP.Service → ERP.Core
```

| المشروع | المسؤولية |
|---|---|
| `ERP.Core` | الكيانات والنماذج، DTOs، وعقود الخدمات؛ لا يعتمد على EF أو ASP.NET. |
| `ERP.Service` | تنفيذ قواعد العمل، الخدمات، EF Core و`ErpDbContext` والهجرات. |
| `ERP.Api` | Controllers وHTTP وتهيئة التطبيق والوسيطات والأخطاء. |
| `tests/ERP.Tests` | اختبارات تكامل وقواعد معمارية. |

## دورة الطلب

```text
Angular → Controller (ERP.Api) → Contract (ERP.Core) → Service (ERP.Service)
        → DbContext/transaction/domain services → DTO → HTTP response
```

- Controller يتعامل مع البروتوكول والصلاحيات الأولية ويربط الطلب بالخدمة.
- DTO يعرّف شكل الإدخال/الإخراج، والعقد يحدد عمليات الخدمة.
- الخدمة هي موضع التحقق وقواعد المجال. العمليات المالية متعددة الخطوات تستخدم معاملة.
- DbContext والكيانات تحدد التخزين والعلاقات. عند تعديل التخزين افحص إعدادات EF والهجرات.
- استجابات API غالباً مغلفة، لكن بعض endpoints مثل تحميل الجداول والتقارير قد تعيد شكلاً خاصاً؛ راجع المستهلك.

## خريطة المجلدات

- `ERP.Api/Controllers/`: نقاط النهاية حسب المجال.
- `ERP.Api/Infrastructure/`: معالجة HTTP المشتركة، صلاحيات وأخطاء.
- `ERP.Core/Contracts/`: واجهات الخدمات.
- `ERP.Core/DTOs/`: عقود البيانات المتبادلة.
- `ERP.Core/Models/`: الكيانات والنماذج.
- `ERP.Service/Services/`: قواعد العمل حسب المجال.
- `ERP.Service/Data/ErpDbContext.cs`: إعداد مجموعات الكيانات والعلاقات.
- `ERP.Service/Data/Migrations/`: تغييرات مخطط قاعدة البيانات.
- `ERP.Service/ServiceCollectionExtensions.cs`: تسجيل الخدمات واعتماداتها.
- `tests/ERP.Tests/`: الاختبارات.

## فواتير كمثال، والتمييز المهم

توجد فواتير تجارة عامة وفواتير معرض سيارات، وتشارك بعض المنطق عبر `TradeInvoiceEngine` لكن أنواع المستندات والجداول والمسارات مختلفة:

- التجارة العامة: `TradeDocumentsControllers.cs` ومسارات مثل `api/v1/sales-invoices` و`purchase-invoices` و`sales-returns` و`purchase-returns`؛ التنفيذ عبر `ISalesInvoiceService` ونظائرها.
- معرض السيارات: `ERP.Api/Controllers/Accounting/Sales/InvoicesController.cs` على `api/v1/invoices`، والعقد `IInvoiceService` والتنفيذ `ERP.Service/Services/Accounting/Sales/InvoiceService.cs`.
- المحرك المشترك: `ERP.Service/Services/Accounting/Sales/TradeInvoiceEngine.cs`؛ يستقبل نوع الجدول المناسب، ويتعامل مع التحقق والتسعير والترحيل والمرتجعات.
- الأرقام النهائية والضريبة وآثار المخزون والقيد تحسب في الخادم. لا تعتمد على أرقام الواجهة كمرجع موثوق.

## التشغيلات والصلاحية (Lot/Batch)

- كل حركة كمية تمر من `InventoryService.RecordMovementAsync` (والتحويل `TransferAsync`)؛ للصنف المتتبَّع (`Product.TrackBatches`) تُوزَّع الحركة على التشغيلات عبر `Services/Shared/Inventory/ItemBatches.cs`.
- الحركة تبقى سطراً واحداً بتكلفة الصنف (التكلفة وإعادة البناء على مستوى الصنف)، وتوزيعها في `StockMovementBatch`، والرصيد لكل تشغيلة في كل مستودع في `BatchStock`.
- الوارد: رقم التشغيلة (تُنشأ/تُطابَق) وتاريخ الانتهاء الإلزامي مع `TrackExpiry`. الصادر: المختارة أو الأقرب انتهاءً (FEFO) من غير المنتهية؛ البيع من منتهية مرفوض دائماً.
- المرتجع يعود لتشغيلات أصله، وعكس الفاتورة (تعديل/حذف) يعود لتشغيلاتها ثم يُعلَّم توزيعها `IsReversed`. ما خُصِّص فعلاً يُكتب على سطر المستند (`BatchNumber` / `ExpiryDate`) للعرض والطباعة.
- اختبارات الدورة: `tests/ERP.Tests/Shared/ItemBatchTests.cs`.

## سياسات الاعتماد (الموافقات)

- فهرس المستندات والعمليات القابلة للاعتماد: `ERP.Core/Models/Shared/Workflow/ApprovalDocumentCatalog.cs` (يعرضه `GET api/v1/approvals/document-types`). السياسة الواحدة تغطي عدة مستندات (`DocumentTypes`)، ويرفض التحقق أي مستند/عملية خارج الفهرس.
- الفواتير والمرتجعات تبقى مسودة حتى اعتمادها (`TradeInvoiceEngine`)، والجرد يمر بمسار اعتماده.
- بقية المستندات تُحجز عمليتها: المتحكم يستدعي `HoldForApprovalAsync(handler, action, id, payload)` بعد فحص الصلاحية (أو `ApprovalHandler` في `CrudController`)؛ فتُجرَّب داخل معاملة تُلغى للتحقق وبناء ملخص المعتمِد، وتُحفظ بطلب اعتماد (202) ولا تُنفَّذ إلا عند الاعتماد النهائي في `ApprovalDecisionService` عبر `IApprovalGate.ExecuteAsync`.
- لإضافة مستند جديد: أضفه للفهرس، وسجّل معالجه في `ERP.Service/Services/Shared/Workflow/ApprovalRegistry.cs` (خدمته وعملياته)، واستدعِ البوابة في إجراءات متحكمه. العمليات التي تُرسل للهيئة بعد معاملتها تُسجَّل بـ `dryRun: false`.

## تتبع تغيير منظم

1. حدد endpoint من خدمة الواجهة أو `docs/API_ENDPOINTS.md`.
2. افتح Controller المقابل وحدد الصلاحيات والشكل المتوقع للطلب.
3. اتبع DTO إلى عقد الخدمة ثم التنفيذ في `ERP.Service`.
4. افحص الكيانات و`ErpDbContext` إذا كان التغيير يؤثر على التخزين.
5. راجع المعاملة والخدمات الجانبية (المخزون، القيود، الموافقات، التدقيق) عند تعديل مستند مالي.
6. حدّث الواجهة إذا تغير عقد الطلب/الاستجابة. مشروعها المجاور: `D:\SystemErp`، ودليله `D:\SystemErp\FRONTEND_PROJECT_GUIDE.md`.

## قواعد عامة لا ينبغي كسرها

- عزل المنشآت مصدره هوية المستخدم/المطالبة المعتمدة، وتُطبّق قيود العزل في الاستعلامات والحفظ.
- فحوص الصلاحية على الخادم إلزامية حتى لو أخفت الواجهة الأزرار.
- العمليات التي تغيّر عدة سجلات مترابطة يجب أن تحافظ على ذرية المعاملة.
- لا تنفذ حسابات الفواتير المالية النهائية في العميل.
- `Vehicle` كيان مستقل عن `Product`؛ مسارات المعرض ليست مرادفاً لفواتير الأصناف العامة.

## وثائق تفصيلية

- `docs/ARCHITECTURE.md`: قرارات المعمارية والعزل والترحيل والتقارير.
- `docs/API_ENDPOINTS.md`: فهرس نقاط النهاية.
- `docs/FRONTEND_MAPPING.md`: ربط المسارات بشاشات الواجهة.
- `docs/SYSTEM_CATALOG.md` و`docs/POS.md`: فهارس وتفاصيل إضافية.

## لتقليل وقت القراءة

ابدأ بهذا الملف. بعد ذلك ابحث عن اسم endpoint أو DTO أو الكيان باستخدام `rg -n`، واقرأ الملفات المتصلة بالسلسلة المطلوبة فقط. لا تعِد قراءة `ARCHITECTURE.md` بالكامل إلا إذا كان التغيير معمارياً أو يمس القواعد العامة.
