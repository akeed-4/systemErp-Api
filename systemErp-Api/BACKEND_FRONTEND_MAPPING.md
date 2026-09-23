# Backend ↔ Frontend Mapping

Source documents: `SystemErp-web/.backend-analysis/frontend-inventory.md` (Angular survey) and
`SystemErp-Api/systemErp-Api/.backend-analysis/backend-inventory.md` (existing .NET survey).

Status legend:
- ✅ Exists on both sides and is compatible (or trivially so)
- ⚠️ Exists on the backend but is **broken/mismatched** (wrong field names, won't compile, duplicate/conflicting implementations, or not wired into DI/routing)
- ❌ **Missing entirely** on the backend — needs a new entity/DTO/service/controller
- 🗑️ Frontend feature is **orphaned** (not routed, or a legacy duplicate) — do not build backend support until product confirms it's wanted

> Field-level detail lives in the two inventory documents above; this file is the routing table between them, plus a per-area status and note on what's actually broken.

---

## 0. Foundation (blocks everything below)

| Concern | Frontend signal | Backend today | Status |
|---|---|---|---|
| Base entity (Id/CreatedAt/UpdatedAt/TenantId) | every model has `id`, most have `tenantId` | `BaseEntity` referenced by all 27 entity files but **defined nowhere** | ❌ Solution doesn't compile |
| Multi-tenancy | `ErpService.activeTenant`/`switchTenant`, every entity has `tenantId: string` | Two incompatible half-built mechanisms: `ICurrentTenantService` (undefined, `Guid`-flavored) and `ITenantService`/`TenantService` (real, `string`-based, header-driven) | ⚠️ Type mismatch, one undefined |
| Auth | `auth.service.ts`, `auth.guard.ts`, JWT-shaped `Authorization: Bearer` header already sent by `auth.interceptor.ts` | `AuthController.Login` returns a hardcoded dummy token; no ASP.NET Identity, no JWT validation, no `[Authorize]` anywhere | ❌ Not real |
| RBAC / screen permissions | `permission.service.ts` — `ScreenPermission{screenId, canView/Create/Edit/Delete/Approve}` per role | Nothing — no permission model in Domain at all | ❌ Missing |
| Command/business-op pattern | n/a (frontend has no concept of this) | MediatR used in `Features/CarShowroom/Commands/*` but package never added to `.csproj` | ❌ Won't compile |

---

## 1. Master Data

| Frontend screen | Frontend service | Frontend model | Endpoint | Backend controller | Entity | Status |
|---|---|---|---|---|---|---|
| master-data/customers | `ErpService.addCustomer/updateCustomer/deleteCustomer` (+ auto-creates linked GL account under `112`) | `Customer` | `api/v1/customers` | `CustomersController` (Base CRUD) | `Customer` | ✅ shape matches; ⚠️ auto-GL-account-on-create business rule not implemented backend-side |
| master-data/suppliers | `ErpService.addSupplier/...` (+ auto-creates GL account under `211`) | `Supplier` | `api/v1/suppliers` | `SuppliersController` | `Supplier` | ✅ / ⚠️ same auto-GL gap |
| master-data/banks | `ErpService.addBank/...` (+ auto-creates GL account) | `BankEntity` | `api/v1/banks` | `BanksController` | `BankEntity` | ✅ / ⚠️ same auto-GL gap |
| master-data/items | `ErpService.addProduct/updateProduct/deleteProduct` | `ProductItem` | `api/v1/products` | `ProductsController` | `Product` | ⚠️ field names differ: FE `currentStock/averageCost/sellingPrice/vatRate` vs BE `CurrentStock/AverageCost/SellingPrice/VatRate` (fine) but **`Infrastructure.InventoryService` uses `StockQuantity/CostPrice/SalePrice/ReorderLevel`, which don't exist on the real entity** — must be rewritten |
| master-data/payment-methods | `ErpService.addPaymentMethod/...` | `PaymentMethodItem` | `api/v1/paymentmethods` | `PaymentMethodsController` | `PaymentMethodItem` | ✅ |
| master-data/categories | `ErpService.addCategory/...` | `ProductCategory` | `api/v1/productcategories` | `ProductCategoriesController` | `ProductCategory` | ✅ |
| master-data/units | `ErpService.addUnit/...` | `UnitOfMeasure` | `api/v1/unitsofmeasure` | `UnitsOfMeasureController` | `UnitOfMeasure` | ✅ |
| master-data/car-colors | `CarShowroomService.addExteriorColor/addInteriorColor` (plain string lists, no id) | n/a (string[]) | none | none | none | ❌ no entity — needs a simple `CarColor{Id,NameAr,Hex}` table (model exists in FE: `CarColor`) |
| (currencies, implicit) | `ErpService.currencies` signal | `Currency` | `api/v1/currencies` | `CurrenciesController` | `Currency` | ✅ |
| (cost centers) | `ErpService.addCostCenter` | `CostCenter` | `api/v1/costcenters` | `CostCentersController` | `CostCenter` | ✅ |
| (warehouses) | implicit in `ProductItem`/`StockMovement`/`MaterialRequisition` | `Warehouse` | `api/v1/warehouses` | `WarehousesController` | `Warehouse` | ✅ |
| fixed-assets | `ErpService.addFixedAsset` | `FixedAsset` | `api/v1/fixedassets` | `FixedAssetsController` | `FixedAsset` | ✅ (no depreciation-run endpoint on either side — future work) |

---

## 2. Accounting Core

| Frontend screen | Frontend service | Frontend model | Endpoint | Backend controller | Entity/Service | Status |
|---|---|---|---|---|---|---|
| accounts-tree | `ErpService.addAccount/updateAccount/deleteAccount`, `isEntityAccountEstablished`, `createAccountFor{Bank,Customer,Supplier}` | `Account` | `api/v1/accounts` | `AccountsController` | `Account` | ✅ CRUD shape; ❌ the auto-linked-account business rule (create GL account when a customer/supplier/bank is created) has no backend equivalent anywhere |
| accounts-tree (journal entries) | `ErpService.addManualJournalEntry/updateManualJournalEntry/deleteJournalEntry` | `JournalEntry`, `JournalEntryLine` | `api/v1/journalentries` | `JournalEntriesController` | `JournalEntry`/`JournalEntryLine` | ✅ CRUD; ❌ no `/reverse` endpoint despite `JournalEntryStatus.Reversed` existing; ⚠️ no real posting engine wired — see below |
| vouchers | `ErpService.createVoucher/deleteVoucher` | `Voucher` | `api/v1/vouchers` + `POST {id}/post` | `VouchersController` | `Voucher` | ✅ CRUD + post route exists; ⚠️ posting logic depends on the broken `IAccountingService` |
| **Accounting engine (cross-cutting)** | `createInvoice`, `createVoucher`, `billContractMilestone`, `completePosCheckout` **each build their own JournalEntry inline** (hardcoded account codes `112`/`211`/`213`/`411`) rather than calling one shared engine | — | — | `IAccountingService` (**two incompatible implementations**, neither matches real `Account`/`JournalEntry`/`Product` field names) | — | ⚠️ **highest-priority fix** — must become one real centralized engine so every module posts consistently, per the user's own architecture requirement |
| costing | `ErpService.setCostingPolicy`, `recalculateAllProductsCosting` | `CostingPolicyConfig` | `api/v1/costing`, `POST recalculate-all`, `POST policy` | `CostingController` | — | ❌ both actions are no-ops (`recalculate-all` multiplies cost by 1.0; `policy` doesn't persist); `MovingAverageCalculator.CalculateNewAverageCost` exists and is correct but unused |
| reports (VAT return) | (implicit — no dedicated FE screen found, but `FinancialStats.outputVat/inputVat/netVatPayable`) | — | `GET api/v1/reports/vat-return` | `ReportsController` | uses `IReportsService` | ✅ real, if simplistic (zero-rated/exempt always 0) |

---

## 3. Sales

| Frontend screen | Frontend service | Frontend model | Endpoint (needed) | Backend controller | Entity | Status |
|---|---|---|---|---|---|---|
| sales/invoices | `ErpService.createSalesInvoice/createInvoice/createInvoiceReturn/deleteInvoice`, `simulateZatcaSend` | `Invoice`, `InvoiceItem` | `api/v1/invoices` + `POST {id}/post` + needed: `POST {id}/submit-zatca` | `InvoicesController` | `Invoice`/`InvoiceItem` | ✅ CRUD + suggestions + post; ❌ no ZATCA submission endpoint (service exists but unregistered — see §7) |
| sales/quotations | `ErpService.addQuotation/convertQuotationToSalesInvoice/updateQuotation/deleteQuotation` | `Quotation`, `QuotationItem` | `api/v1/quotations` + needed: `POST {id}/convert-to-invoice` | `QuotationsController` | `Quotation` | ✅ CRUD; ❌ no convert-to-invoice endpoint (business op only exists client-side) |
| sales/returns | reuses `createInvoiceReturn` (Invoice with `isReturn=true`) | `Invoice` (return flavor) | same as invoices | `InvoicesController` | `Invoice` | ✅ modelable on existing entity, no extra work needed beyond the Invoice engine |
| orders | (uses `CommercialOrder`, no dedicated FE service method found beyond generic CRUD) | `CommercialOrder` | `api/v1/commercialorders` | `CommercialOrdersController` | `CommercialOrder` | ✅ CRUD + Include(Items) |
| agreements | `agreements.component.ts` (framework agreements, item lines) | not in `erp.models.ts` inventory — needs confirmation of exact shape | none | none | none | ❌ no matching entity found on either side — needs its own small entity (`Agreement` + `AgreementItem`) unless it's meant to reuse `CommercialOrder`/`Quotation` — **flag for product confirmation** |

---

## 4. Purchases

| Frontend screen | Frontend service | Frontend model | Endpoint | Backend controller | Entity | Status |
|---|---|---|---|---|---|---|
| purchases/standard (invoices) | `ErpService.addPurchaseInvoice` | `Invoice` (kind=purchase) | `api/v1/invoices` | `InvoicesController` | `Invoice` | ✅ same entity as sales, discriminated by `InvoiceType` |
| purchases/standard/returns | reuses purchase Invoice + `isReturn` | `Invoice` | same | `InvoicesController` | `Invoice` | ✅ |
| purchases/requisitions | `ErpService.addPurchaseRequisition/updatePurchaseRequisition/convertRequisitionToPurchaseInvoice/deletePurchaseRequisition` | `MaterialRequisition`, `MaterialRequisitionItem` | `api/v1/materialrequisitions` + needed: `POST {id}/convert-to-invoice` | `MaterialRequisitionsController` | `MaterialRequisition` | ✅ CRUD + Include(Items); ❌ no convert endpoint |
| purchases/cars (invoices/returns) | `CarShowroomService.createProcurementOrder`, `advanceProcurementStage` | `CarProcurementOrder` | none | none | **missing entity** | ❌ see §6 Car Showroom |

---

## 5. Contracts

| Frontend screen | Frontend service | Frontend model | Endpoint | Backend controller | Entity | Status |
|---|---|---|---|---|---|---|
| contracts (7-stage + 4-stage sub-cycle) | `ErpService.addCommercialContract/updateCommercialContract/advanceCommercialContractStage/billContractMilestone/deleteCommercialContract` | `CommercialContract`, `ContractMilestone`, `ContractClause` | none | none | **missing entity** | ❌ no `CommercialContract`/`ContractMilestone` entity anywhere in `ERP.Domain` — this is a real gap, not just a rename; `billContractMilestone`'s journal-posting logic (Dr receivable 112 / Cr revenue 411 / Cr output VAT 213) needs to move into the accounting engine once this entity exists |
| commercial-contracts (alt UI) | same service, overlapping purpose with `contracts` | same models | same | same | same | ⚠️ product should confirm whether `contracts` and `commercial-contracts` are meant to converge on one screen — build the backend once, against whichever wins |
| delivery-notes | `ErpService.addDeliveryNote/addDeliveryReturnNote/createMilestoneInvoiceFromDeliveryNote` | `DeliveryNote`, `DeliveryReturnNote` | none | none | **missing entity** | ❌ no `DeliveryNote` entity anywhere — needed for contract/agreement fulfilment tracking |
| approvals | `ApprovalService.checkAndCreateApprovalRequest/approveRequest/rejectRequest/savePolicy/deletePolicy` | `ApprovalPolicy`, `ApprovalRequest`, `ApprovalHistoryItem` | `api/v1/approvalrequests` (requests only) | `ApprovalRequestsController` | `ApprovalRequest` | ⚠️ `ApprovalRequest`/`ApprovalHistoryItem` entities exist and CRUD works, but **`ApprovalPolicy` (the rule definitions) doesn't exist as an entity at all** — the policy-evaluation business logic (`checkAndCreateApprovalRequest`) has nothing to read from on the backend |

---

## 6. Car Showroom (procurement 7-stage + sales contract 5-stage)

**Everything in this section is the single largest missing chunk.** All 7 entities (`CarAgent`, `CarBrand`, `CarModel`, `CarTrim`, `CarProcurementOrder`(+Item), `CarSalesContract`, `Vehicle`) plus ~12 enums (`ProcurementStage`, `ProcurementOrderStatus`, `CarSalesCycleType`, `BuyerType`, `SalesContractStatus`, `VehicleCondition`, `FuelType`, `TransmissionType`, `VatMode`, `VehicleStatus`) are referenced throughout `ERP.Application`/`ERP.Infrastructure`/`ERP.Api` but **do not exist in `ERP.Domain`** — they're dead references left over from an orphaned `RayahAccounting.Domain` project that no longer exists on disk.

| Frontend screen | Frontend service | Frontend model | Endpoint (referenced, non-compiling) | Backend controller | Entity | Status |
|---|---|---|---|---|---|---|
| showroom/vehicles | `CarShowroomService.addVehicle/updateVehicle/deleteVehicle` | `Vehicle` | none real | (`DatabaseSyncController` references it) | **missing** | ❌ |
| showroom/procurement (7 stages) | `CarShowroomService.createProcurementOrder/advanceProcurementStage` | `CarProcurementOrder`, `ProcurementOrderItem` | none real | `Features/CarShowroom/Commands/*` (won't compile) | **missing** | ❌ the fullest picture of intended behavior lives in `ERP.Infrastructure/Services/CarShowroomService.cs` (stage machine, VIN auto-create, invoice+journal generation) — reusable as a spec once entities exist |
| showroom/sales (5-stage contracts) | `CarShowroomService.createSalesContract/updateContractStatus/deleteSalesContract` | `CarSalesContract` | none real | same | **missing** | ❌ |
| master data: brands/models/trims/years/agents | `CarShowroomService.addBrand/addModel/addTrim/addYear/addAgent` (+ update/delete for most) | `CarBrand`, `CarModel`, `CarTrim`, `CarYearModel`, `CarAgent` | none | none | **missing** | ❌ |
| showroom/invoices | reuses general `Invoice` (kind=car-related) once `CarProcurementOrder`/`CarSalesContract` exist | `Invoice` | `api/v1/invoices` | `InvoicesController` | `Invoice` | ✅ entity exists, just needs the car-showroom side to generate them |
| showroom/reports (8 car report types) | `reports-expanded.service.ts` computed signals | `CarSalesPerformanceRow`, `CarVinInventoryRow`, etc. | `ReportsController`'s `car-*` actions (won't compile — reference missing types) | `ReportsController` | — | ❌ blocked entirely until the entities above exist |

---

## 7. POS

| Frontend screen | Frontend service | Frontend model | Endpoint | Backend controller | Entity | Status |
|---|---|---|---|---|---|---|
| pos | `ErpService.openPosShift/closePosShift/validateAndApplyCoupon/redeemLoyaltyPoints/saveHeldCart/removeHeldCart/completePosCheckout/updatePosInvoiceSettings/submitPosTransactionToZatca/processPosReturn` | `PosShift`, `PosTransaction`(+Item), `PosCoupon`, `PosOffer`, `CustomerLoyalty`, `PosHeldCart`(+Item), `PosInvoiceSettings`, `PosSalesReturn`(+Item) | none | none | **missing entirely** | ❌ zero POS entities/controllers exist on the backend — this whole module needs to be built from scratch in Phase 9, reusing the (fixed) accounting + inventory engines rather than duplicating posting logic, per the user's architecture requirement |

---

## 8. Reports

| Frontend screen | Frontend service | Backend endpoint | Status |
|---|---|---|---|
| reports (general) | `reports-expanded.service.ts`: `inventoryAuditData`, `itemMovementsSummaryData`, `detailedItemLedgerData`, `commercialTradeData` | `GET api/v1/reports/{inventory-audit, item-movements, item-ledger-detail, trade-commercial}` | ⚠️ endpoints exist but `item-ledger-detail`/`inventory-audit` **synthesize plausible-looking fake numbers** rather than reading real `StockMovement` history — needs a real implementation once purchases/sales actually write stock movements |
| reports (car-showroom, 8 types) | `carSalesPerformanceData`, `carVinInventoryData`, `carZatcaMarginTaxData`, `carProcurementTrackingData`, `carProfitLossData`, `carInstallmentsReceivableData`, `carSuppliersProcurementData`, `carDailyMonthlySalesData` | `GET api/v1/reports/car-*` | ❌ won't compile — blocked on §6 |
| dashboard | `dashboard.component.ts` | `GET api/v1/dashboard/stats`, `GET api/v1/dashboard/recent-activity` | ✅ exists, real aggregation |

---

## 9. Notifications & Permissions

| Frontend screen | Frontend service | Frontend model | Endpoint | Backend controller | Entity | Status |
|---|---|---|---|---|---|---|
| (bell icon, global) | `NotificationService.notify/markAsRead/markAllAsRead/clearAll` | `AppNotification` | `api/v1/notifications` | `NotificationsController` | `AppNotification` | ✅ CRUD exists |
| permissions | `PermissionService.getDefaultPermissionsForRole/getPermissionsForUserOrRole/savePermissionsForUserOrRole/hasPermission` | `ScreenPermission`, `UserRolePermission` | none | none | **missing** | ❌ no permission entity on the backend at all — needed to make `[Authorize]`/policy checks real (see §0) |

---

## 10. ZATCA / e-Invoicing

| Frontend screen | Frontend service | Backend | Status |
|---|---|---|---|
| zatca-integration | `ErpService.updateZatcaConfig/testZatcaConnection` | `ZatcaConfig` (owned by `Tenant`) exists; no controller exposes it directly (only via `TenantsController`?) | ⚠️ needs a dedicated endpoint to update config + test connection |
| (invoice submission) | `ErpService.simulateZatcaSend`, `submitPosTransactionToZatca` | `ZatcaPhase2Service` (`IZatcaPhase2Service`) exists with real TLV/QR logic and a hand-built UBL XML generator, but is **not registered in DI and not called from any controller** — `SubmitInvoiceToZatcaAsync` is a pure mock (always succeeds, no real HTTP call) | ❌ needs to be wired up; real cryptographic signing (CSR/CSID/ECDSA) needs actual ZATCA onboarding credentials from the business before it can be made real — **flagging as a decision only the user can unblock**, not something inferable from code |

---

## Orphaned Frontend Code — do not build backend support yet

| Feature | Why orphaned |
|---|---|
| `auth/login`, `auth/register-company` | Not in `app.routes.ts` — real login flow apparently lives elsewhere or isn't finished client-side either |
| `item-master` | Legacy, targets the unused `src/app/services/erp.service.ts` (not `core/services`) |
| `dictionary` | Not routed; i18n browsing tool, not core business |
| `subscriptions` | Not routed; `SubscriptionsController` backend exists but is 100% hardcoded anyway |
| `backend-architecture` | Not routed; presumably a docs/diagram page |
| `src/app/models/erp.model.ts` + `src/app/services/erp.service.ts` (outside `core/`) | Entirely separate, unused legacy model/service pair — ignore for mapping purposes |
| `firebase.service.ts` | Wraps real Firestore/Auth but is never invoked from `ErpService` — likely an abandoned earlier real-backend attempt; **confirm with user whether to decommission once the .NET API is live** |

---

## Summary counts

- Backend entities that match frontend needs and are otherwise fine: **~20** (Account, Customer, Supplier, Product, Bank, Currency, CostCenter, Warehouse, UnitOfMeasure, PaymentMethodItem, ProductCategory, FixedAsset, Invoice/InvoiceItem, JournalEntry/Line, Voucher, Quotation/Item, MaterialRequisition/Item, CommercialOrder, AppNotification, ApprovalRequest/History, Tenant, User)
- Backend entities that exist but need real rework (field-name drift, missing business logic): **Account/Product/Invoice/JournalEntry linkage in the accounting+inventory engines**, **Subscription** (duplicate enums)
- Entirely missing entities needed for what the frontend already assumes: **Vehicle, CarBrand, CarModel, CarTrim, CarAgent, CarColor, CarProcurementOrder(+Item), CarSalesContract, CommercialContract(+Milestone+Clause), DeliveryNote(+ReturnNote), ApprovalPolicy, ScreenPermission/UserRolePermission, PosShift, PosTransaction(+Item), PosCoupon, PosOffer, CustomerLoyalty, PosHeldCart(+Item), PosInvoiceSettings, PosSalesReturn(+Item), Agreement(+Item, pending product confirmation)** — roughly **25 new entities**
