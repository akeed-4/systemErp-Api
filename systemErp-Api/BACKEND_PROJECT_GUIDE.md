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

## طبيعة الصنف والمكونات والتصنيع

- `Product.ItemType` (`ProductItemTypes`): `stock` بضاعة، `service` خدمي، `kit` مجمّع، `composite` مركّب، `manufactured` منتج صناعي. الرصيد والحركات للبضاعة والمنتج الصناعي فقط (`TracksStock`)؛ `InventoryService.RecordMovementAsync` و`TransferAsync` يرفضان غيرهما، والجرد لا يلتقطهما.
- المكونات في `ProductComponent` (جدول مستقل بلا ملاحة على الصنف حتى لا تثقل القوائم): صنف بكميته لكل وحدة (`item`) أو مصروف غير مباشر بمبلغه وحسابه الدائن (`overhead`، للمنتج الصناعي). التحقق والقراءة والحفظ في `ProductComponents`؛ `GET products/{id}` وحده يعيدها مع التكلفة التقديرية، والحفظ يستبدلها إن أُرسلت `Components`.
- الفاتورة: كل سطر يحفظ لقطة `ItemType`، والمجمّع/المركّب لقطة مكوناته `ComponentsJson` (`InvoiceLineComponent`). قواعد الأثر في `InvoiceLineStock`: الخدمي وبند بلا صنف بلا حركة (والخدمي تكلفته المعيارية تكلفة خدمة)، والمجمّع/المركّب تُصرف مكوناته وتُحفظ تكلفتها على اللقطة فيُعكس ويُرجع منها. المجمّع والمركّب لا يُشتريان. الطباعة تعرض مكونات المركّب بلا سعر.
- مستند التصنيع `ProductionOrder` (`IProductionOrderService` على `api/v1/production-orders`، شاشة `production-orders` ترث الجرد، ترقيم `production_order`): الأسطر من مكونات المنتج (`template`)، والترحيل يصرف المواد بتكلفتها ويدخل المنتج بمجموع المواد والخدمات والمصاريف، والقيد مدين مخزون المنتج / دائن مخزون المواد وحسابات التحميل (الافتراضي `516` تكاليف تصنيع محمّلة)، والفرق على حساب فروق التكلفة. الجرد الدوري بلا قيد. الإلغاء يعكس الحركات والقيد مع إعادة تقييم للفرق (الهجرة `ItemTypesComponentsProduction`).
- بند مكتبة المقاولات يرتبط بصنف خدمي (`WorkItem.ProductId`)؛ `POST work-items/{id}/service-item` ينشئه من البند أو يزامن اسمه وسعره وتكلفته، فيُباع البند في كل الوحدات.
- العكس: `POST work-items/from-product` (`WorkItemService.CreateFromProductAsync`) ينشئ بنداً من صنف قائم: اسمه ووحدته وسعره، ومكوناته (أو الصنف نفسه إن كان بضاعة) احتياجات لكل وحدة. المادة من المورد المربوط بالصنف أو تُنشأ مربوطة؛ المكوّن الخدمي والمصروف غير المباشر مورد «أخرى» يدوي. الصنف الخدمي يُربط بالبند (ويُرفض إن كان مربوطاً ببند آخر).
- ربط المواد بالأصناف دفعة: `GET construction-resources/link-suggestions` (مرشحون بالرمز/الباركود ثم الاسم ثم المتشابه، للأصناف ذات الرصيد فقط) و`POST construction-resources/link-products` (يتحقق من الوحدة ويتخطى غير المتوافق والمركّب، ثم يعيد تكلفة المركّبات).
- `GET work-items/product-usage/{productId}`: بنود المكتبة التي تستخدم الصنف (صنفها الخدمي، أو مادة مربوطة به مباشرة أو داخل مورد مركّب) لبطاقة الصنف.

## دورة توريد مواد المشروع

- دراسة الجدوى = جدول كميات عقد المقاولات: كل سطر بسعر البيع و`EstimatedUnitCost` (المجموعة من فروعها)، و`CommercialContract.EstimatedCost` مجموعها؛ ملخص المشروع يعرض الهامش المخطط. الكميات المطلوبة من المواد = الاحتياجات المعتمدة (`ProjectMaterialPlan`) محوّلة لوحدة الصنف.
- `IProjectSupplyService` (`ProjectSupplyController` على `commercialcontracts/{id}`): `material-tracking` لكل صنف (المطلوب، المطلوب شراؤه من طلبات المشروع غير المرفوضة، المشترى من فواتير الشراء المرحّلة المحوّلة منها، المسلَّم بالأذونات المرحّلة، المتبقي للطلب وللتسليم)، و`material-tracking/boq` لكل بند وصنف (المخطط من الاحتياجات المعتمدة على سطر القياس `SubItemId ?? BoqItemId`، المسلَّم وتكلفته، المختار في المستخلصات والمفوتر، المتبقي تحت التنفيذ، وتنبيه تجاوز المخطط)، و`material-issues` إذن تسليم المواد للمشروع (`ProjectMaterialIssue`، شاشة `project-material-issues` ترث المشاريع، ترقيم `project_material_issue`).
- قائمة الأذونات لكل المشاريع: `GET project-material-issues/load` (`ProjectMaterialIssuesIndexController`، `LoadIssuesAsync` عبر `EntityLoader`، صفوف `ProjectMaterialIssueRowDto`)، والعقد من مجموعته العامة فيسري نطاق الوصول إلى العقود (`ScopedRecord`). المستند (`GET .../material-issues/{id}`) يحمل رقم العقد واسم المشروع وموقعه والعميل واسم المستودع للطباعة.
- طلب الشراء (`MaterialRequisition`) يحمل `ContractId` و`BoqItemId` لكل سطر (سطر قياس من العقد). مع المشروع: الأصناف من الاحتياجات المعتمدة، والكمية ≤ المطلوب − ما طُلب في طلباته الأخرى (`EnsureRequisitionWithinPlanAsync`)، ولا يُخصم رصيد المستودع العام. `DeliveryLocation` يُفرض `warehouse` (`project_site` قيمة قديمة وصفية للعرض).
- عزل مشتريات المشروع (الهجرة `ProjectPurchaseIsolation`): طلب المشروع لا يُحوَّل لفاتورة شراء للمستودع (`ProjectRequisitionReceivedByGrn` في التحويل و`ClaimSourceAsync`). يُستلم بسند الاستلام نفسه (`DeliveryNote` بنوع `purchase_delivery` و`RequisitionId`، القواعد في `ProjectReceipts`: طلب معتمد للمشروع، والمورد والبند والسعر من الطلب، والمستلم الصافي ≤ الطلب − سنداته الأخرى). السند يُنشئ عهدة `ProjectMaterialIssue` بنوع `purchase_receipt` (`DeliveryNoteId`، `CustodyIssueId` على السند) بسعر الشراء وقيد مدين المواد تحت التنفيذ بمركز المشروع / دائن `ProjectGoodsReceivedNotInvoicedAccountCode`، بلا حركة مخزون ولا تغيير متوسط تكلفة الصنف. مرتجع السند (`purchase_delivery_return`) قبل الفوترة يسحب من العهدة بنوع `receipt_return` (مدين الاستحقاق / دائن تحت التنفيذ). فاتورة المورد من السند أسطر بلا مخزون على حساب الاستحقاق (`InventoryAccountCode`) فتسوّيه، ولا تُضاف لفوترة العميل. السند المرحّل لا يُعدَّل، وحذفه يعكس عهدته ما لم تُستخدم، والعهدة لا تُرجع للمستودع العام (هدر أو مستخلص أو إرجاع للمورد). المتابعة: المشترى والمسلَّم يشملان صافي الاستلام، والمعلّق باقي كل طلب. الصرف من المستودع العام للمشروع يبقى إذناً صريحاً.
- الإذن: جهة استلام (`ReceiverType` واسمها)، وكل سطر على بند قياس من العقد نفسه (`BoqItemId` إلزامي، لا بند رئيسي ذو فروع)، والتسليم الحالي للصنف ≤ المطلوب − المسلَّم سابقاً (يُعاد الفحص عند الترحيل). الترحيل يصرف بالتكلفة الفعلية من المستودع وقيده مدين «مواد مشاريع تحت التنفيذ» (`ProjectMaterialsWipAccountCode`، أصل، بمركز تكلفة العقد) / دائن مخزون المستودع، ويُحفظ الحساب المدين على الإذن (`DebitAccountCode`؛ فارغ = إذن قديم رُحِّل مصروفاً على `517` أو جرد دوري بلا قيد). الإلغاء يعكس الحركة والقيد، ويُمنع ما دامت كمياته مختارة في مستخلص غير ملغى (و`RowVersion` على السطر يكشف التزامن).
- أرشيف بنود المستخلص إلزامي: كل بند بكمية حالية غير صفرية يلزمه مستند في `ContractCertificateItemAttachment` (`ContractCertificateService.EnsureProgressDocumentedAsync`، رسالة `CertificateItemAttachmentRequired` بالبنود وقائمة أخطاء لكل بند). يُفحص عند حفظ المستخلص القائم وتقديمه (بعد قواعد الحالة والفترة) واعتماده، والإصدار المباشر يمر بهما؛ الإنشاء الأول بلا فحص لأن المستندات تُرفع بعد أن يأخذ المستخلص معرّفه.
- المستخلص (عقد العميل) يختار كميات من الأذونات المرحّلة (`ContractCertificateMaterial`، `certificates/material-options`): البند مقيس حتى المستخلص، والكمية ≤ المسلَّم − المختار في مستخلصات غير ملغاة (يُعاد الحساب عند كل حفظ وتقديم واعتماد وفوترة؛ ومستخلص مفتوح واحد لكل عقد)، والتكلفة = الكمية × تكلفة الصرف والجزء الأخير بباقيها (`CertifiedMaterials`). الفوترة تضيف قيداً: مدين «تكلفة إيرادات العقود» (`ContractCostOfRevenueAccountCode`) / دائن حساب تحت التنفيذ نفسه (`CostJournalEntryId`)؛ الإيراد والضريبة والاستقطاعات بلا تغيير، ولا يُقيَّد المخزون مرة ثانية. الحسابان أدوار في `ContractAccountsSetup` (تُهيَّأ بلا أرقام ثابتة).
- إلغاء فاتورة المستخلص (`certificates/{id}/cancel-invoice`، `CancelCertificateInvoiceAsync`): مرتجع كامل بمنطق المرتجعات، وعكس قيدي الاسترداد والتكلفة، والمستخلص يصير ملغى فتعود كمياته ومواده؛ سجل `ContractInvoice` بنوع `credit_note` (فهرس فريد لكل مستخلص) يُطرح ضمانه وإجماليه من موقف العقد. لآخر مستخلص غير ملغى بلا تحصيل ولا إفراج ضمان. المرتجع الحر لفاتورة مستخلص مرفوض (`TradeInvoiceEngine`). (الهجرتان `ProjectSupplyCycle` و`ProjectMaterialsWipCostRecognition`).
- موقع العقد `WarehouseId`: يُختم من موقع الجلسة عند الإنشاء، ويُرسل في `CreateCommercialContractDto` مع «كل المواقع»، والتعديل بلا موقع يُبقيه.

## سياسات الاعتماد (الموافقات)

- فهرس المستندات والعمليات القابلة للاعتماد: `ERP.Core/Models/Shared/Workflow/ApprovalDocumentCatalog.cs` (يعرضه `GET api/v1/approvals/document-types`). السياسة الواحدة تغطي عدة مستندات (`DocumentTypes`)، ويرفض التحقق أي مستند/عملية خارج الفهرس.
- الفواتير والمرتجعات تبقى مسودة حتى اعتمادها (`TradeInvoiceEngine`)، والجرد يمر بمسار اعتماده.
- بقية المستندات تُحجز عمليتها: المتحكم يستدعي `HoldForApprovalAsync(handler, action, id, payload)` بعد فحص الصلاحية (أو `ApprovalHandler` في `CrudController`)؛ فتُجرَّب داخل معاملة تُلغى للتحقق وبناء ملخص المعتمِد، وتُحفظ بطلب اعتماد (202) ولا تُنفَّذ إلا عند الاعتماد النهائي في `ApprovalDecisionService` عبر `IApprovalGate.ExecuteAsync`.
- لإضافة مستند جديد: أضفه للفهرس، وسجّل معالجه في `ERP.Service/Services/Shared/Workflow/ApprovalRegistry.cs` (خدمته وعملياته)، واستدعِ البوابة في إجراءات متحكمه. العمليات التي تُرسل للهيئة بعد معاملتها تُسجَّل بـ `dryRun: false`.

## مكتبة المقاولات

- المقاولات إضافة مرخّصة `PlatformModules.Construction` فوق المحاسبة، سعرها باتفاق مع العميل (`PricedByAgreement`): لا تُضاف لباقة، ويمنحها مدير المنصة من «الوحدات»، وتنتقل للاشتراك الجديد عند التجديد أو تغيير الباقة (`SubscriptionCatalog.CarryAgreementAddOns`). متحكمات المقاولات بـ `RequireModule(Construction)`، ومسارات جدول الكميات في متحكم العقود بسمة على الإجراء، وحفظ عقد بجدول كميات يتحقق منها في `CommercialContractService`. الهجرة `ConstructionAddOn` منحتها لمن لديه بيانات مقاولات.
- مكتبة تكاليف المقاولات (الهجرة `ConstructionCostLibrary`): الكتالوج نفسه `ConstructionResource` على مستوى المنشأة (لا كتالوج ثانٍ)، بالفئة `Category` (نص حر؛ المهندس فئة داخل العمالة) والمورد المرتبط `SupplierId` وملكية المعدة `EquipmentOwnership` (`owned`/`rented`).
  - السجل `ConstructionResourceCost`: سطر لكل تغيّر بتاريخ سريانه وعملة المنشأة ومصدره ووحدته وتفصيله ومن أدخله. يُضاف إليه فقط: `ErpDbContext.GuardResourceCostHistory` يرفض تعديل السطر وحذفه (إلا مع حذف مورده غير المستخدم)، ويضيف سطراً «system» لأي تغيّر في `StandardCost` لم يسجّله مساره. `StandardCost` و`CostEffectiveFrom` نسخة مخزّنة من السطر الساري اليوم.
  - القواعد في `ResourceCostRules`: معادلة تكلفة الساعة الواحدة (`HourlyCost`، يستعملها مورد المشروع أيضاً)، والمصدر `breakdown` للعمالة والمعدات (السعر بأساسه ساعة/يوم/شهر + وقود وصيانة وإهلاك ومشغّل وأخرى بالساعة، × `HoursPerUnit`)، و`CostAtAsync` التكلفة السارية في تاريخ (أحدث سطر ≤ التاريخ)، و`RollForwardAsync` للسعر المجدول عند «تحديث التكاليف».
  - المسارات على `construction-resources`: `cost-summary`، و`{id}/costs` (السجل، وإضافة سعر ماضٍ أو مستقبلي لليدوي والتفصيل)، و`costs-at?date=&ids=`. نموذج المورد يقبل `effectiveFrom` حتى اليوم ولا يسبق آخر سطر ساري.
  - إدخال القيمة اليدوية والتفصيل والأسعار (ومن محرر البنود) يتطلب تعديل شاشة `construction-cost-library` (ترث `construction-library`). المكتبة لا تُنشئ موظفين ولا أصولاً ولا موارد مشروع.
- شاشة صلاحيات `construction-library` (ترث العقود). بند العقد `CommercialContractItem` يحمل الكمية والسعر ووصف الوحدة، وعليه يُقاس المستخلص.
- مسار المقاولات من الدراسة إلى العقد (سجل واحد `CommercialContract`، بلا مستند موازٍ؛ المناقصات خارجه):
  - دراسة الجدوى: `CommercialContract.FeasibilityStatus` (`study` ← `plan_approved` ← `contracted`؛ فارغة = عقد مباشر كما كان) و`FeasibilityNumber` (ترقيم `feasibility_study`، FS-سنة-). تُنشأ بنموذج العقد مع `IsFeasibilityStudy`، و`ContractNumber` يحمل رقمها حتى «إنشاء العقد»، ولها ملف المشروع بلا مركز تكلفة. خارج قوائم العقود (`ApplyFilters`) والمشاريع، ولا تخرج من المسودة (`ContractWorkflowService.BlockersAsync`). السجل `GET feasibility-studies` (الهجرة `FeasibilityStudies`).
  - تحليل التكلفة المجمّد `ContractItemResourceCost` (الهجرة `ContractItemCostAnalysis`): لكل سطر موارده بعد فك المركّب من `ProjectMaterialAnalysis` (`LineResult.Resources` لكل الأنواع؛ المواد كما هي) بتكلفة سطر سجل المكتبة في تاريخ التسعير ومدخلاته. `IContractCostAnalysisService` على `commercialcontracts/{id}/cost-analysis`: القراءة، و`preview` بالفروق، و`apply` صريح (المعدّل يدوياً يبقى)، و`PUT {rowId}` تعديل بسبب (صلاحية تعديل مكتبة التكاليف). حفظ العقد يأخذ تكلفة السطر المسعّر من تحليله (`ApplyCostAnalysisAsync`) ويسقطه إن تغيّر ربط السطر؛ والإجماليات في `CommercialContractService.RecomputeEstimate`. التعديل في المسودة قبل اعتماد الخطة.
  - خطة التنفيذ `ProjectExecutionPlan` (واحدة لكل عقد، الهجرة `ExecutionPlan`) و`ProjectResourceRequirement` (العمالة والمعدات وغيرها) والمراحل (`ProjectPhase.Source`/`DatesSuggested`/`PredecessorIds`/`SupervisorUserId`/`PlannedCost`) والمواد (`ProjectMaterialRequirement.PhaseId`/`PlannedUnitCost`/`QuantityPerUnit`). `IExecutionPlanService` على `commercialcontracts/{id}/execution-plan`: `generate` (مرحلة لكل باب، الوزن من التكلفة، التواريخ بتوزيع مدة الدراسة على الساعات أو تنبيه بلا اختراع، المشتق يُستبدل واليدوي والمعدّل يبقيان، الميزانية تُملأ إن كانت صفراً)، و`resources`، و`submit` ← `approve` (سياسة الاعتماد `contract-execution-plan`؛ يعتمد احتياجات المواد معه، ويجمّد نسختي `ProjectSnapshot` للدراسة وأساس الخطة، والدراسة تصير `plan_approved`) أو `return` بسبب، و`revise` (مسودة برقم أعلى؛ قبل العقد تعود الدراسة والمواد مسودة)، و`comparison` (الدراسة ↔ الأساس ↔ الحالي ↔ الفعلي من الأذونات المرحّلة والتقارير المعتمدة). التنبيهات رموز `Warnings`. صلاحيات الإجراءات بسمات الشاشة (لا مصفوفة مراحل العقد) ونطاق السجل بـ `ScopedRecord`. خطة مقدَّمة أو معتمدة تقفل حفظ المراحل (`EnsurePlanEditableAsync`)، وللمشروع الذي له خطة لا تُحفظ احتياجات المواد ولا تُقدَّم ولا تُعتمد من مسارها (`EnsureNotManagedByPlanAsync`)؛ والسطر المشتق من الدراسة في احتياجات العمالة والمعدات تبقى كميته وتكلفته منها (المدير يغيّر المرحلة والعدد والملكية).
  - «إنشاء العقد»: `POST feasibility-studies/{id}/create-contract` (صلاحية إنشاء العقود) لدراسة بخطة معتمدة: رقم من ترقيم العقود، ومركز تكلفة المشروع، و`contracted`؛ ثم دورة العقد كالمعتاد.
  - الموافقة المالية الآلية (`FeasibilityFinancialApproval`): «إنشاء العقد» يضيف `ContractApproval` مالياً معتمداً في الجولة الحالية باسم معتمِد الخطة (`ApprovedByUserId`) وتاريخ اعتمادها ومرجع الدراسة (`Source = feasibility_study`، `SourceReference`، `SourceId` نسخة الدراسة المجمّدة، `ApprovedValue`)، بلا صلاحية المراجعة المالية، إن طابقت قيمة العقد قيمة الدراسة. تُسقط (`InvalidatedAt`/`InvalidationReason`، والسجل باقٍ) عند الرجوع للمسودة أو تغيّر القيمة في التعديل، و`RoundApprovalsAsync` لا يحتسبها إن اختلفت القيمة. الأحداث في `AuditLog` (الهجرة `ContractApprovalFeasibilitySource`).
- الضمان البنكي: نوع يتطلبه (`RequiresGuarantee`) على المسار الكامل = إلزامي (`ContractTypeCodes.GuaranteeMandatory`)؛ الإنشاء والتعديل يفرضانه مهما أرسل الطلب، وشروط الانتقال تفحصه من النوع والمسار أيضاً. غيره اختيار لكل عقد.
  - قيد التنفيذ `ExecutionPlanService.EnsureExecutionAllowedAsync` (التقرير اليومي حفظاً، وإذن الصرف إنشاءً وتعديلاً وترحيلاً، وطلب شراء المشروع): للسجل الآتي من دراسة فقط — عقد سارٍ (أو استلام ابتدائي) وخطة معتمدة. العقود المباشرة والقديمة على قواعدها.
- البنود الفرعية في جدول الكميات: العميل يرسل `IsSubItem` والترتيب، و`ApplyBoq`/`LinkSubItems` يربط كل فرعي بأقرب سطر رئيسي قبله (`ParentItemId`، بلا قيد) ويعلّم الرئيسي ذا الفروع `IsGroup`. المجموعة سعرها = مجموع فروعها ÷ كميتها وإجماليها مجموعها، ولا تدخل `ContractValue` ولا المستخلصات ولا فحص الاكتمال؛ الفروع هي التي تُسعَّر وتُقاس (الهجرة `ContractItemSubItems`).
- احتياجات مواد المشروع (العقد هو المشروع، `ContractId`، بلا جدول مشاريع): `ProjectMaterialPlan` واحدة لكل عقد بجدول كميات، وأسطرها `ProjectMaterialRequirement`.
  - السطر: البند `BoqItemId` والفرعي `SubItemId` (أسطر في العقد)، ومادة مكتبة المقاولات `ResourceId` (مورد من نوع مادة، إلزامية عند الحفظ؛ الصنف المخزني المباشر مرفوض)، والكمية الأساسية بوحدة المادة.
  - `ProductId` اختياري يُنسخ من ربط المادة، والعرض يأخذ الربط الحالي: الكمية المحوّلة لوحدة الصنف ورصيده. المادة غير المربوطة احتياج صحيح لا يُصرف ولا يُشترى حتى تُربط.
  - التحليل في `ProjectMaterialAnalysis`، وهو المصدر الواحد للاشتقاق والاختيار اليدوي ومواد التقرير اليومي: كميات العقد × معامل الاستهلاك × (1 + الهدر)، مع فك المورد المركّب. السطر الفرعي يأخذ احتياجات بنده الفرعي في المكتبة عبر `CommercialContractItem.WorkSubItemId`.
  - `IProjectMaterialService` على `commercialcontracts/{id}/material-requirements`. الاشتقاق (`derive`، معاينة لا تُحفظ) يشمل المادة غير المربوطة، ويعيد تشخيص كل سطر (`ProjectMaterialDiagnostics`): اشتُق، أو بتنبيه (غير مربوطة، وحدة لا تتحول)، أو تُخطي بسببه (غير مرتبط بالمكتبة، فرعي بلا مصدر، رئيسيه غير مرتبط، بند المكتبة محذوف، لا مواد في التحليل).
  - `candidates?lineId=&all=` مواد تحليل البند بكمية مقترحة ومنشئها، أو مواد المكتبة عامة لسطر غير مرتبط أو مع `all`.
  - مادة عليها احتياج أو استهلاك موقع لا تُحذف (الهجرة `LibraryMaterialRequirements`). الدورة مسودة ← مقدَّمة ← معتمدة (أو إعادة بسبب)؛ الإعداد بصلاحية الاستلام كالمستخلصات، والاعتماد بصلاحية الاعتماد ويمر بسياسة الاعتماد (`project_material_plan` / `contract-material-requirements`). المعتمدة ثابتة ولا تتبع المكتبة، و`RowVersion` على الاحتياج لتزامن اعتماد طلبات الشراء (الهجرة `ProjectMaterialRequirements`).
- إعداد مشروع المقاولات (`IConstructionProjectService` على `commercialcontracts/{id}/project`، شاشة `construction-projects` ترث العقود): `ConstructionProjectProfile` واحد لكل عقد (الميزانية التقديرية وساعات العمل بالدقائق والاستراحة؛ فترات الإنجاز ساعة لكل فترة تُحسب منها)، و`ProjectPhase`/`ProjectPhaseItem` (التواريخ المخططة والوزن ≤ 100، والبند في مرحلة واحدة، ومرجع سطر العقد بلا قيد؛ الإنجاز المخطط = Σ الوزن × نسبة أيام المرحلة)، و`ProjectMember` (المستخدم بدوره في المشروع). إنشاء عقد بجدول كميات ينشئ ملفه ومركز تكلفة `PRJ-رقم العقد` إن لم يُختر مركز؛ العقد القديم يُستكمل بأول حفظ للملف. الجداول تُحذف مع العقد (الهجرة `ConstructionProjectSetup`).
- قائمة المشاريع وملخصها: `GET construction-projects` (`ConstructionProjectsIndexController`، شاشة `construction-projects` ومسارها `construction/projects`) و`commercialcontracts/{id}/project/summary`. الحساب في `ConstructionProjectService.SummariesAsync` من البيانات المحفوظة:
  - المدة المنقضية، والإنجاز المخطط من المراحل، والمعتمد من آخر قيمة تراكمية لمستخلص معتمد أو مفوتر.
  - الميزانية، والتكلفة التشغيلية من التقارير المعتمدة بتكلفتها المثبّتة.
  - حالة الاحتياجات وغير المربوط منها بربطه الحالي، والمستخلصات المفتوحة، والتقارير بانتظار الاعتماد، والفريق.
  - تنبيهات `ConstructionProjectAlerts` (التأخر عن المخطط بأكثر من 10 نقاط).
  - العقود من استعلام عام فيسري نطاق الوصول على القائمة.
- نطاق «المسندة إليه» على العقود يشمل العضوية: `ApplyContractScopedFilter` في `ErpDbContext` للقوائم، و`RecordFacts.MemberUserIds` لفحص السجل. نطاق «الكل» والصلاحية الكاملة لا يتقيّدان. أدوار المقاولات الجاهزة (`POST construction-projects/role-templates`) أدوار مخصّصة عادية تُنشأ عبر `RoleService` و`PermissionService`.
- الإنجاز اليومي (المرحلة 1):
  - `ProjectResource` مورد المشروع بتكلفة ساعته. أنواعه: آلية مملوكة، آلية مستأجرة بمؤجرها، مهندس، عمالة. تكلفة الساعة = السعر ÷ ساعات أساسه (ساعة، أو يوم = ساعات اليوم، أو شهر = ساعات اليوم × أيام الشهر) + الوقود والصيانة والمشغّل والتشغيل الإضافي، والإضافية بمعامل. تُحسب في `ConstructionProjectService.HourlyCost`.
  - `ProjectDailyPlan` المستهدف بالساعة لكل بند، ساريةً من تاريخها.
  - `SiteDailyReport` تقرير واحد لكل مشروع في اليوم، بأسطره `SiteHourEntry` و`SiteResourceUsage` و`SiteMaterialUsage`. تكلفة الساعة وتكلفة وحدة المادة تُثبَّتان على السطر عند الحفظ.
  - المادة المستهلكة مادة مكتبة (`ResourceId`) بوحدتها وتكلفتها، والصنف من ربطها. خياراتها `site-reports/material-options?itemId=` من تحليل البند (`ProjectMaterialAnalysis`).
  - الخدمة `ISiteDailyReportService` على `commercialcontracts/{id}/site-reports`، شاشة `site-daily-reports`. الدورة: مسودة ← مقدَّم ← معتمد، أو إعادة بسبب. القالب من الفترات والخطة.
  - أرشيف مستندات بنود التقرير (`ISiteReportAttachmentService`، `SiteReportItemAttachment`، على `site-reports/{reportId}/attachments` و`items/{itemId}/attachments`): آلية أرشيف بنود المستخلص وتصنيفاته، بحد 2 ميغابايت للملف (`SiteReportAttachmentService.MaxBytes`) و20 مستنداً للبند. الإضافة بصلاحية الإضافة أو التعديل على شاشة التقرير في أي حالة، والحذف في المسودة لمن يُعدّ وبعدها بصلاحية الحذف؛ يُحذف مع مسودة التقرير والعقد (الهجرة `SiteReportItemAttachments`).
  - الحسابات في `SiteDailyReportService.Summarize`: التوقف أطول توقف في الفترة، والتحقيق بقيمة البند، والمورد بلا بند يُوزَّع بقيمة المنفَّذ.
  - التكلفة هنا تشغيلية لا تُرحَّل ولا تُجمع مع القيود (الهجرة `SiteDailyReports`).
- حذف العقد للمسودة وحدها: `OnDeletingAsync` يحذف تابعيه صراحة (سجل الحالات، المراجعات، المرفقات، الاحتياجات، إعداد المشروع، الموارد، التقارير اليومية)، ومركز تكلفة المشروع التلقائي إن لم يُستخدم. قيود قاعدة البيانات على العقد تبقى «منع الحذف» بالاتفاقية العامة، فإعداد `OnDelete(Cascade)` على علاقة مع العقد لا يسري.
- أوامر التغيير (`ContractVariation`/`ContractVariationLine`، `IContractVariationService` على `commercialcontracts/{id}/variations`، ترقيم `contract_variation`، الهجرة `ContractVariations`): على عقد جدول الكميات الساري (`active`) وحده، للعميل ومقاول الباطن.
  - الدورة: مسودة ← مقدَّم ← معتمد، أو إعادة/رفض/إلغاء بسبب. الإعداد بصلاحية الاستلام، والاعتماد بصلاحية الاعتماد ويمر بسياسة الاعتماد (`contract_variation` / `contract-variations`، المبلغ |أثر القيمة|).
  - التسجيل لا يغيّر العقد. الاعتماد وحده: فرق البند القائم يُضاف إلى `RevisedQuantity` (الكمية الأصلية `Quantity` ثابتة)، والبند الجديد سطر عقد في آخر الجدول بكمية أصلية صفر و`VariationId`، وتتعدل `ContractValue` وضريبتها والمتبقي و`EstimatedCost` و`EndDate`، ويُثبَّت ما قبلها وبعدها على الأمر.
  - الكمية بعد النقص لا تقل عن المقاس في المستخلصات غير الملغاة. قيمة العقد الأصلية = الحالية − مجموع المعتمد.
  - المستخلص والتحليل (`ProjectMaterialAnalysis`) يقيسان على `RevisedQuantity`؛ الاحتياجات المعتمدة لا تتبع الأمر تلقائياً.
- تكلفة المشروع وربحيته (الهجرة `ProjectCostAndSiteMaterials`):
  - `IProjectCostService` على `commercialcontracts/{id}/project/cost-report` يقرأ القيود المرحّلة والمعكوسة مع عكسها على مركز تكلفة المشروع، ولا يخزن شيئاً.
  - الفعلي = حسابات المصروف + رصيد المواد تحت التنفيذ. التصنيف بمصدر القيد ثم الحساب: مواد، وباطن (مراجع عقوده المرتبطة)، وعمالة، ومعدات، وأخرى. غرامات العميل وخصوماته خصم من الإيراد.
  - المخطط: المواد من الاحتياجات المعتمدة، والباطن من عقوده، والباقي تنفيذ مجتمع.
  - القيمة المكتسبة = الميزانية × الإنجاز المعتمد، ومنها مؤشر الأداء والتوقع عند الاكتمال. الربح الدفتري على أساس الفوترة، والربح بنسبة الإنجاز (تكلفة إلى تكلفة) للعرض.
- تحميل العمالة والمعدات: اعتماد التقرير اليومي (`SiteDailyReportService.PostCostAsync`) يقيد مدين `ProjectLaborCostAccountCode`/`ProjectEquipmentCostAccountCode` بمركز المشروع / دائن `ProjectAbsorbedCostAccountCode` بلا مركز.
  - التكلفة بتكلفة الساعة المثبّتة على الأسطر (`SiteDailyReport.CostJournalEntryId`). المعدات المستأجرة لا تُحمَّل (تكلفتها من فاتورة الإيجار).
  - التقارير المعتمدة قبل التفعيل تُرحَّل بـ `POST .../site-reports/post-costs`.
- مستند مواد المشروع بنوعه `ProjectMaterialIssue.Kind` (`ProjectMaterialIssueKinds`):
  - تسليم، أو إرجاع للمستودع (وارد بتكلفة التسليم، ومدين المخزون / دائن تحت التنفيذ)، أو هدر في الموقع (بلا حركة، ومدين تكلفة إيرادات العقود / دائن تحت التنفيذ).
  - الإرجاع والهدر يسحبان من سطر تسليم مرحّل (`SourceLineId`). المتاح = كميته − المسحوب − المختار في المستخلصات (`CertifiedMaterials.WithdrawnByLineAsync`)، فلا تُعترف التكلفة مرتين. لا يُلغى تسليم سُحب منه.
  - المتابعة تعرض المسلَّم الصافي والمُرجع والهدر والمستهلك (التقارير المعتمدة) وعهدة الموقع.
- المستخلص يعرض لكل بند المنفذ في التقارير المعتمدة حتى نهاية فترته (`SiteExecutedQuantity`/`ExceedsSite`) تنبيهاً بلا منع.
- المناقصة = مسودة عقد المقاولات (لعميل) قبل الترسية، بلا مستند موازٍ (الهجرة `ContractTenders`):
  - `CommercialContract.TenderStatus` (`ContractTenderStatuses`: جديدة ← دراسة ← تسعير ← مقدَّمة ← مرساة/خاسرة؛ فارغة = عقد مباشر) وبياناتها (`TenderNumber`، `TenderIssueDate`، `TenderDeadline`، `TenderRequirements`) ونتيجتها.
  - الحالة تُقبل عند الإنشاء (`new`) وحده؛ بعده من `IContractTenderService.MoveAsync` (`POST commercialcontracts/{id}/tender/move`، صلاحية التعديل، في المسودة وحدها). السجل `GET tenders`.
  - التقديم يحفظ نسخة العرض `ContractTenderRevision` (القيم وأسطر جدول الكميات JSON)؛ والمقدَّمة/الخاسرة لا تُعدَّل حتى تعود للتسعير.
  - الترسية تنشئ ملف المشروع ومركز تكلفته (لا يُنشآن للمناقصة عند الإنشاء)، والخسارة بسبب إلزامي.
  - `ContractWorkflowService.BlockersAsync` يمنع خروج المسودة (إلا الإلغاء) ما لم تُرسَ، وقائمة المشاريع تستبعد غير المرساة.
- عقد مقاول الباطن = عقد مورد بجدول كميات (الهجرة `SubcontractProjectLink`):
  - `CommercialContract.ParentContractId` يربطه بعقد العميل (المشروع؛ عقد عميل بجدول كميات غير مغلق ولا ملغى وليس باطناً)، و`CommercialContractItem.MainContractItemId` يربط سطره ببند المشروع (غير مجموعة، بلا قيد).
  - التحقق في `CommercialContractService.ValidateParentAsync`. مع الربط يُفرض مركز تكلفة المشروع (`ApplyParentAsync`) فتُحمَّل فواتير مستخلصاته عليه.
  - عقد المورد لا يُنشئ ملف مشروع ولا مركز `PRJ-`، ولا يظهر في قائمة المشاريع. لا يُحذف مشروع له عقود باطن.
  - `GET commercialcontracts/{id}/project/subcontracts` (`IProjectSubcontractService`): عقود الباطن بقيمتها والمعتمد من مستخلصاتها، وتغطية بنود المشروع (المسند وسعره مقابل سعر البيع، والتجاوز).
- سطر العقد يحفظ مصدره في المكتبة `CommercialContractItem.WorkItemId` (نسخة مستقلة بلا قيد، مفهرس للتقارير)؛ التحقق يشترط وجود البند حين يُرسل المصدر لأول مرة.
- الموارد `ConstructionResource` على `api/v1/construction-resources` (رمز فريد اختياري يُولَّد بنوعه، وحدة معرّفة، ومصدر التكلفة `CostSource`: يدوي، أو الصنف المخزني للمادة بتحويل الوحدة، أو شؤون الموظفين للعمالة عبر `HrLaborCost`: متوسط التكلفة المحمّلة لموظفي المسمى الوظيفي الفعّالين ÷ 208 ساعة × ساعات الوحدة، ويشترط صلاحية `hr`). إعادة الحساب بـ `refresh-costs`، والمهن والأقسام (أعداد فقط) بـ `hr-labor-options`. المورد المركّب (`composite`) مكوناته `ConstructionResourceComponent`، تكلفته تُعاد بعد كل حفظ وتحديث بترتيب الاعتماد، ويُمنع التكرار والدورة وحذف مورد داخل مركّب أو بند.
- البنود `IWorkItemService` على `api/v1/work-items`: `WorkItem` (سعر يدوي أو تكلفة + نسبة) و`WorkItemSubItem` و`WorkItemComponent` (احتياج من مورد لكل وحدة من مالكه). التكلفة والسعر والهامش تُحسب عند القراءة من تكاليف الموارد الحالية. حفظ البند يقبل `UnitCost` على الاحتياج لتعديل تكلفة المورد اليدوي نفسه (ثم إعادة المركّبات عبر `ResourceCostRules`). الواجهة لا تعرض شاشة موارد؛ الموارد تُنشأ من محرر البند. الأنواع في `ConstructionLibraryTypes.cs`، ومساعدات الوحدات والأصناف في `ConstructionUnits.cs`.

## الحضور الميداني بالموقع (الهجرة `FieldGpsAttendance`)

- لا جدول حضور ثانٍ: `AttendanceRecord` (سجل واحد للموظف في اليوم، فهرس فريد) يحمل مصدره `Source` (`AttendanceSources`: يدوي، استيراد، `mobile_gps`) ومشروعه `ContractId` ولحظتي `CheckInAt`/`CheckOutAt` (UTC) و`WorkedMinutes`؛ ودليل الموقع `AttendanceGpsPunch` (حضور وانصراف واحد للسجل: الإحداثيات والدقة والبعد ونطاق المشروع وقتها). المسير والكشف والملخص تقرأ السجل كما هو.
- `IFieldAttendanceService` على `api/v1/me/attendance` (`EmployeeSelfService`): الموظف من الجلسة (`EmployeePortalService.CurrentEmployeeAsync`)، والمشروع من إسناده (`ProjectResource.EmployeeId` نشط، عمالة أو مهندس) في التنفيذ (ساري أو استلام ابتدائي، ومع الدراسة `EnsureExecutionAllowedAsync`). الخادم يرفض الدقة الأضعف من حد المشروع والموقع خارج نصف القطر (`GeoDistance` haversine)، ثم يكتب عبر `IAttendanceService.RecordFieldCheckInAsync/CheckOutAsync` (قفل الشهر المرحّل، يوم سجّلته شؤون الموظفين لا يُسجَّل ميدانياً). التأخير من بداية دوام المشروع، والعمل الفعلي بعد خصم استراحته؛ الإضافي يبقى لشؤون الموظفين.
- النطاق على `ConstructionProjectProfile` (`SiteLatitude`/`SiteLongitude`/`GeofenceRadiusMeters`/`MaxGpsAccuracyMeters`/`GpsAttendanceEnabled`، الحدود في `ProjectGeofence`) عبر `IProjectGeofenceService` على `commercialcontracts/{id}/project/geofence`.
- خارج النطاق (الهجرة `FieldAttendanceExceptions`): سياسة المشروع `OutOfRangePolicy` (`reject` الافتراضي، أو `exception`). الاستثناء يلزمه سبب، ويُحفظ التسجيل `OutsideGeofence` بمراجعة `pending`، وحالة السجل `GpsStatus` (`GpsAttendanceStatuses`) من تسجيلاته. القرار عبر `IAttendanceService.ReviewFieldExceptionAsync` (الشهر المرحّل مقفل، ولا يراجع الموظف استثناءه، والرفض بملاحظة): رفض الحضور = غياب، ورفض الانصراف يُسقط `WorkedMinutes`. الدقة الضعيفة تُرفض دائماً.
- لوحة الفريق `IProjectAttendanceService` على `commercialcontracts/{id}/project/attendance` (اليوم، و`exceptions` المعلّقة، و`punches/{punchId}/approve|reject`) بشاشة مستقلة `project-attendance` (عرض واعتماد) لا ترث غيرها لأنها بيانات موقع؛ تمنحها قوالب مدير المشروع والمهندس المشرف (عرض واعتماد) ومشرف الموقع (عرض). الإحداثيات تُعرض للاستثناء وحده، وسجل الموظف على مشروع آخر بلا تفاصيل. الساعات المعتمدة = `WorkedMinutes` لسجل `valid` أو `exception_approved`.
- الكشف والاستيراد مع اليوم الميداني: لا يُحذف (`AttendanceGpsRecordCannotBeCleared`)، ويُعدَّل منه الحالة والتأخير والإضافي والملاحظة فقط؛ الأوقات والمشروع والموقع والمصدر تبقى. غيره يأخذ مصدر كاتبه (`manual`/`import`)، وكشف اليوم يعرض المصدر وحالة التحقق وساعات العمل.
- اليوم والوقت المحليان من `Tenant.TimeZoneId` (`TenantTimeZone`، الافتراضي `Asia/Riyadh`، يُعدَّل من `PUT company`)، والساعة `TimeProvider` (تُثبَّت في الاختبارات بـ `TestClock`).

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
