# مطابقة الواجهة (Angular) ↔ الـ Backend

المصدر: `SystemErp-web/src/app/core/services/*` و`core/models/*`. **ملاحظة مهمة:** الواجهة اليوم لا تستدعي API حقيقياً إلا نقاط `/api/v1/auth/*`؛
كل ما عداه حالة `signal()` محلية. لذلك عقد الـ API الفعلي مُستخرج من طرق الخدمات (`ErpService`, `CarShowroomService`, ...) ومن DTOs الجاهزة في
`backend-api.models.ts` (`ApiResponse`, `PagedResult`, `PaginationParams`) التي طُبِّقت حرفياً.

## 1. النقاط التي تستدعيها الواجهة فعلاً

| الواجهة | Endpoint | Controller → Service | ملاحظات |
|---|---|---|---|
| `AuthService.login` | `POST /auth/login` | `AuthController` → `AuthService` | `{success,message,token,user,tenant}` مسطّحة |
| `AuthService.registerCompany` | `POST /auth/register-company` | `AuthController` → `AuthService` + `TenantProvisioningService` | ينشئ المنشأة والمدير والاشتراك وشجرة الحسابات |
| `requestPasswordResetOtp` | `POST /auth/forgot-password/request` | `AuthService` | `otpCode` يُعاد في التطوير فقط |
| `verifyResetOtp` | `POST /auth/forgot-password/verify-otp` | `AuthService` | |
| `resetPassword` | `POST /auth/forgot-password/reset` | `AuthService` | الرمز يُستخدم مرة واحدة |
| تحميل المستخدمين عند البدء | `GET /auth/users` | `AuthController` → `UserService` | `{success,data:[users]}` |

## 2. طرق `ErpService` (النظام المحاسبي العام)

| طريقة الواجهة | Endpoint | Service | Entity |
|---|---|---|---|
| `addCustomer/updateCustomer/deleteCustomer` (+حساب 112 تلقائي) | `/customers` CRUD | `CustomerService` (+`AccountService.EnsureLinkedAccountAsync`) | `Customer`, `Account` |
| `addSupplier/...` (+حساب 211) | `/suppliers` | `SupplierService` | `Supplier` |
| `addBank/...` (+حساب 111) | `/banks` | `BankService` | `BankEntity` |
| `addProduct/...` | `/products` | `ProductService` | `Product` |
| `addCategory/...` · `addUnit/...` · `addPaymentMethod/...` | `/productcategories` · `/unitsofmeasure` · `/paymentmethods` | خدمات CRUD | `ProductCategory`, `UnitOfMeasure`, `PaymentMethodItem` |
| العملات · المستودعات | `/currencies` · `/warehouses` | `CurrencyService` · `WarehouseService` | `Currency`, `Warehouse` |
| `addCostCenter` · `addFixedAsset` | `/costcenters` · `/fixedassets` | `CostCenterService` · `FixedAssetService` | `CostCenter`, `FixedAsset` |
| `addAccount/updateAccount/deleteAccount` | `/accounts` (+`/tree`, `/by-code/{code}`) | `AccountService` | `Account` |
| `addManualJournalEntry/update/delete` | `/journalentries` (+`POST {id}/reverse`) | `JournalService` → `AccountingPostingService` | `JournalEntry`, `JournalEntryLine` |
| `createVoucher/deleteVoucher` | `/vouchers` | `VoucherService` → `AccountingPostingService` | `Voucher`, `VoucherPaymentSplit` |
| `createSalesInvoice/createInvoice/createInvoiceReturn/deleteInvoice` | `POST /invoices`, `POST /invoices/returns`, `DELETE /invoices/{id}`, `POST /invoices/{id}/post` | `InvoiceService` | `Invoice`, `InvoiceItem`, `InvoicePaymentSplit` |
| `simulateZatcaSend` | `POST /invoices/{id}/submit-zatca` | `ZatcaService` | (الإرسال الفعلي غير مُنفَّذ، انظر §6) |
| `addPurchaseInvoice` | `POST /invoices` بـ `kind=purchase` | `InvoiceService` | `Invoice` |
| `addQuotation/convertQuotationToSalesInvoice/update/delete` | `/quotations` (+`convert-to-invoice`, `convert-to-order`) | `QuotationService` | `Quotation` |
| `addPurchaseRequisition/convertRequisitionToPurchaseInvoice/...` | `/materialrequisitions` (+`approve`, `convert-to-invoice`) | `MaterialRequisitionService` | `MaterialRequisition` |
| الأوامر (`orders`) · الاتفاقيات (`agreements`) | `/commercialorders` (+`convert-to-invoice`) · `/agreements` | `CommercialOrderService` · `AgreementService` | `CommercialOrder`, `Agreement` |
| `addCommercialContract/advanceCommercialContractStage/billContractMilestone/...` | `/commercialcontracts` (+`advance-stage`, `milestones/{id}/bill`) | `CommercialContractService` | `CommercialContract`, `ContractMilestone`, `ContractClause` |
| `addDeliveryNote/addDeliveryReturnNote/createMilestoneInvoiceFromDeliveryNote` | `/deliverynotes` (+`returns`, `{id}/milestone-invoice`) | `DeliveryNoteService` | `DeliveryNote`, `DeliveryReturnNote` |
| `setCostingPolicy/recalculateAllProductsCosting` | `GET/PUT /costing/policy`, `POST /costing/recalculate-all` | `CostingService` | `CostingPolicy` |
| حركات المخزون | `GET /stockmovements`, `POST /stockmovements/adjust` | `InventoryService` | `StockMovement` |
| `updateZatcaConfig/testZatcaConnection` | `PUT /company/zatca-config`, `POST /company/zatca-test` | `CompanyService` | `Tenant.ZatcaConfig` |
| `FinancialStats` (لوحة المؤشرات) | `GET /reports/financial-stats` | `AccountingReportService` | — |
| `reports-expanded`: inventoryAudit / itemMovements / itemLedger / commercialTrade | `/reports/inventory-audit`, `item-movements`, `item-ledger`, `trade-commercial` | `InventoryReportService` | — |
| التقارير المالية | `/reports/trial-balance`, `account-statement/{code}`, `financial-summary`, `vat-return` | `AccountingReportService` | — |

## 3. طرق `CarShowroomService`

| طريقة الواجهة | Endpoint | Service | Entity |
|---|---|---|---|
| `addBrand/updateBrand/deleteBrand` · `addModel/...` · `addTrim/...` · `addYear/...` · `addAgent` · ألوان | `/carbrands` · `/carmodels` · `/cartrims` · `/caryears` · `/caragents` · `/carcolors` | `Car*Service` | `CarBrand`, `CarModel`, `CarTrim`, `CarYearModel`, `CarAgent`, `CarColor` |
| `addVehicle/updateVehicle/deleteVehicle` | `/vehicles` (+`available`, `calculate-vat`) | `VehicleService` | `Vehicle` |
| `calculateVat` | `POST /vehicles/calculate-vat` | `CarVat` | — |
| `createProcurementOrder/advanceProcurementStage/deleteProcurementOrder` | `/carprocurementorders` (+`advance-stage`, `reject`) | `CarProcurementService` | `CarProcurementOrder`, `…Item`, `…Vin` |
| `createSalesContract/updateContractStatus/deleteSalesContract` | `/carsalescontracts` (+`advance-status`, `cancel`) | `CarSaleService` | `CarSalesContract` |
| التقارير الثمانية | `/reports/car/{sales-performance,vin-inventory,zatca-margin-tax,procurement-tracking,profit-loss,installments-receivable,suppliers-procurement,daily-monthly-sales}` | `CarShowroomReportService` | — |

## 4. طرق POS في `ErpService`

| طريقة الواجهة | Endpoint | Service |
|---|---|---|
| `openPosShift/closePosShift` | `POST /pos/shifts/open`, `POST /pos/shifts/close`, `GET /pos/shifts/active` | `PosShiftService` |
| `completePosCheckout` | `POST /pos/transactions/checkout` | `PosSaleService` → `InvoiceService` |
| `validateAndApplyCoupon` | `POST /pos/coupons/validate` (+CRUD `/pos/coupons`) | `PosCouponService` |
| `redeemLoyaltyPoints` | حقل `loyaltyPointsToRedeem` في الـ checkout · `GET /pos/loyalty` | `PosSaleService` · `PosLoyaltyService` |
| `saveHeldCart/removeHeldCart` | `/pos/held-carts` | `PosHeldCartService` |
| `updatePosInvoiceSettings` | `GET/PUT /pos/settings` | `PosSettingsService` |
| `processPosReturn` | `POST /pos/returns` | `PosSaleReturnService` → `InvoiceService` |
| `submitPosTransactionToZatca` | `POST /pos/transactions/{id}/submit-zatca` | `PosSaleService` → `ZatcaService` |
| العروض | `/pos/offers` | `PosOfferService` |

## 5. خدمات مشتركة أخرى

| الواجهة | Endpoint | Service |
|---|---|---|
| `PermissionService.get/savePermissionsForUserOrRole/hasPermission` | `GET/POST /permissions`, `GET /permissions/me`, `GET /permissions/screens`؛ والتحقق في كل controller عبر `[RequireScreen]` | `PermissionService` |
| `ApprovalService.savePolicy/deletePolicy/togglePolicyActive` | `/approval-policies` (+`PATCH {id}/active`) | `ApprovalPolicyService` |
| `checkAndCreateApprovalRequest/approveRequest/rejectRequest` | `POST /approvals/check`, `GET /approvals`, `POST /approvals/{id}/approve|reject` | `ApprovalService` |
| `NotificationService.notify/markAsRead/markAllAsRead/clearAll` | `/notifications` | `NotificationService` |
| `AuthService.upgradeSubscription` | `GET /subscriptions/plans|current`, `POST /subscriptions/upgrade` | `SubscriptionService` |
| `AuthService.updateUserProfile` | `PUT /auth/profile` | `UserService` |
| إدارة المستخدمين (شاشة الصلاحيات) | `/users` | `UserService` |

## 6. غير مُنفَّذ عمداً / غير مثبت من الواجهة (UNCLEAR / NOT IMPLEMENTED BY ASSUMPTION)

| البند | السبب |
|---|---|
| **إرسال ZATCA الفعلي (Phase 2)**: توقيع ECDSA، UBL 2.1، CSID، سلسلة الهاش | يتطلب شهادات تسجيل حقيقية للمنشأة. مُنفَّذ فقط: QR بترميز TLV (المرحلة 1). `submit-zatca` يُعيد نتيجة صريحة "غير مُفعَّل" ولا يزوّر حالة `cleared`. `testZatcaConnection` يتحقق من اكتمال البيانات فقط. |
| مزوّد إرسال OTP (SMS/Email) | لا مزوّد محدّد في الواجهة؛ الرمز لا يُعاد إلا مع `Auth:ExposeOtpInResponse=true` (تطوير). |
| بوابة الدفع للاشتراكات | لا يوجد مزوّد؛ الترقية تُفعَّل مباشرة مع مرجع عملية للمطابقة اليدوية. |
| كيان **Branch** ونظام الفروع | لا يوجد في نماذج الواجهة (المنشأة = Tenant؛ `salesBranch` نص حر). |
| كيان **POS Terminal / Cash Register** | الواجهة تخزّن `posTerminalName` نصاً فقط. |
| شاشة **الدعم الفني (support)** و**القاموس (dictionary)** و**backend-architecture** | لا نموذج بيانات لها في الواجهة أو غير موجّهة في `app.routes.ts`. |
| جدول أقساط/سداد لعقود التقسيط | الواجهة لا تنمذج الأقساط المدفوعة؛ تقرير الأقساط يعرض المتبقي = الإجمالي − الدفعة المقدمة فقط. |
| نمط `margin_scheme` للسيارات | الواجهة تعامله كالمعفى (بلا ضريبة إضافية)؛ طُبِّق كما هو. |
| استلام جزئي للشواسيهات في دورة الشراء | الواجهة تولّد القائمة كاملة؛ الخادم يشترط تطابق عدد الشواسيهات مع الكمية. |
| تقارير POS مستقلة | لا تقرير POS في الواجهة؛ بيانات الوردية والمعاملات متاحة عبر `/pos/shifts` و`/pos/transactions`. |
| عمولات البطاقات (`PaymentMethodItem.commissionPercent`) | تُخزَّن ولا تُرحَّل محاسبياً (لم تحدَّد قاعدة القيد). |
| ربط الموافقات بالمستندات إجبارياً | الواجهة تستدعي `check` قبل الحفظ؛ الخادم يوفّر المحرّك لكنه لا يمنع إنشاء مستند بدون طلب اعتماد. |
| تشفير أسرار `ZatcaConfig` في القاعدة | لا تُعاد أبداً في API، لكنها مخزّنة نصّاً؛ يلزم مخزن أسرار/تشفير عمود قبل الإنتاج. |
| ايدي الكيانات | الواجهة تولّد معرّفات نصية (`inv-123`)؛ الخادم يستخدم `Guid` ويُرجعها نصوصاً في JSON — يلزم تكييف الواجهة عند التوصيل. |
| قفل الحساب/تحديد معدل محاولات الدخول | غير مطبّق بعد. |
