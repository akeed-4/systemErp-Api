# Backend Inventory (Existing State)

> Scope: `ERP.sln` solution only (`ERP.Domain`, `ERP.Application`, `ERP.Infrastructure`, `ERP.Api`).
> A stray, unreferenced folder `RayahAccounting.Application` also exists at the repo root (not in `ERP.sln`, and its `RayahAccounting.Domain` project it depends on does not exist on disk at all). It appears to be a leftover from an earlier/parallel generation of this codebase and is noted only where relevant to explain drift in the current code.

## ⚠️ HEADLINE FINDING: The solution does not compile

Running `dotnet build ERP.sln` fails with **30 errors**, all `CS0246: The type or namespace name 'BaseEntity' could not be found`. Every single entity in `ERP.Domain/Entities/**` inherits from `BaseEntity`, but no file anywhere in the solution defines that class (confirmed via full-repo grep — only usages exist, no declaration). `ERP.Domain/Common/ITenantEntity.cs` exists, but `BaseEntity` itself was never committed.

This is **not the only break**. Beyond the missing `BaseEntity`, there is pervasive, deep drift between `ERP.Domain` (the real entities) and `ERP.Application`/`ERP.Infrastructure`/`ERP.Api` (which are written against a *different, much richer, and never-materialized* domain model). See "Gaps / Unfinished Work" for the full breakdown — it is more extensive than a typical "a few TODOs" gap list and materially changes the Phase 2 plan (large parts of Application/Infrastructure/Api will need to be rewritten, not just extended, once Domain is fixed).

---

## Domain Entities

All entities live in `ERP.Domain/Entities/*.cs`, namespace `ERP.Domain.Entities`, and declare `: BaseEntity` (undefined — see above) or `: BaseEntity, ITenantEntity`. `ITenantEntity` (`ERP.Domain/Common/ITenantEntity.cs`) adds one member: `string TenantId { get; set; }`, meant to flag tenant-scoped entities for automatic EF Core query-filter isolation. `BaseEntity` is referenced everywhere (via `entry.Entity.Id`, `.TenantId`, `.CreatedAt`, `.UpdatedAt` usage in `ApplicationDbContext`) but its actual member list cannot be confirmed because the class doesn't exist in the repo.

- **Account**: Code, NameAr, NameEn, Type (AccountCategory), ParentCode?, Level, Balance, IsDebitNature, IsSystem, Currency?, LinkedEntityType?, LinkedEntityId (Guid?), Notes?, Children (ICollection<Account>)
- **AppNotification**: RecipientUserId (Guid), RecipientRole?, Title, Body, Type ("system_alert" etc.), RelatedDocType?, RelatedDocId (Guid?), RelatedDocNumber?, ApprovalRequestId (Guid?), IsRead
- **ApprovalRequest**: PolicyId (Guid), PolicyNameAr, DocumentType, DocumentId (Guid), DocumentNumber, ActionType, DocumentAmount, RequesterUserId (Guid), RequesterName, CurrentLevel, TotalLevels, Status ("pending"/"approved"/"rejected"), RequiredApproverRole?, RequiredApproverUserId (Guid?), RejectionReason?, History (ICollection<ApprovalHistoryItem>)
  - **ApprovalHistoryItem** (same file): ApprovalRequestId (Guid), Level, ApproverUserId (Guid), ApproverName, Status, Comment?, Timestamp, ApprovalRequest (nav)
- **AuditLog** (`: BaseEntity, ITenantEntity`): TenantId (string), Action, EntityName, EntityId?, PerformedBy, Details, Timestamp, ClientIpAddress?
- **BankEntity**: Code, NameAr, NameEn, AccountNumber, Iban, SwiftCode, Branch, Currency, OpeningBalance, CurrentBalance, AccountCode?, Status, Notes?
- **CommercialOrder**: OrderNumber, Type ("sales_order"/"purchase_order"), PartyName, PartyPhone?, PartyVatNumber?, OrderDate, ExpectedDeliveryDate, PaymentTerms?, Subtotal, VatTotal, GrandTotal, Status, ConvertedInvoiceId (Guid?), Notes?, Items (ICollection<QuotationItem> — reused type)
- **CostCenter**: Code, NameAr, NameEn, Description?, IsActive
- **Currency**: Code, NameAr, NameEn, Symbol, IsBaseCurrency, ExchangeRate, DecimalPlaces, LastUpdated, Status
- **Customer**: Code, NameAr, NameEn, VatNumber?, CrNumber?, Phone, Email?, ContactPerson?, City, District?, Street?, BuildingNo?, PostalCode?, AdditionalNo?, CreditLimit, CreditPeriodDays, OpeningBalance, CurrentBalance, AccountCode, Currency?, Status, Notes?
- **FixedAsset**: AssetCode, NameAr, NameEn, PurchaseDate, PurchaseCost, CurrentBookValue, AccumulatedDepreciation?, DepreciationRate, AssetAccountId (Guid), AccumulatedDepreciationAccountId (Guid)
- **Invoice**: InvoiceNumber, Uuid (Guid), IssueDate, IssueTime, InvoiceType (enum), PartyName, PartyVatNumber?, PartyCrNumber?, PartyAddress?, PaymentMethod (enum), IsSplitPayment, CurrencyCode, ExchangeRate, Subtotal, ItemsDiscountTotal, InvoiceDiscount, DiscountTotal, VatTotal, GrandTotal, TotalCost, GrossProfit, Status, ZatcaStatus (ZatcaSubmissionStatus), ZatcaHash?, ZatcaQrCode?, ZatcaUblXml?, ZatcaPih?, JournalEntryId (Guid?), Notes?, IsReturn, OriginalInvoiceId (Guid?), OriginalInvoiceNumber?, ReturnReason?, Items (ICollection<InvoiceItem>)
- **InvoiceItem**: InvoiceId (Guid), ItemId (Guid), ItemName, Sku, Unit, Quantity, UnitPrice, UnitCost, Discount, VatRate, VatAmount, TotalBeforeVat, TotalAfterVat, CostCenterId (Guid?), Invoice (nav)
- **JournalEntry**: EntryNumber, Date, Description, ReferenceType?, ReferenceId (Guid?), ReferenceNumber?, TotalDebit, TotalCredit, IsBalanced (computed), Status (JournalEntryStatus), SourceType?, SourceReferenceId (Guid?), Lines (ICollection<JournalEntryLine>)
- **JournalEntryLine**: JournalEntryId (Guid), AccountCode, AccountName, Debit, Credit, Notes?, CostCenterId (Guid?), JournalEntry (nav)
- **MaterialRequisition**: RequisitionNumber, RequestDate, RequiredDate, Department, RequestedBy, Priority, SupplierId (Guid?), SupplierName?, WarehouseId (Guid?), WarehouseName?, TotalEstimatedCost, Status, ApprovedBy?, ApprovalDate?, ConvertedInvoiceId (Guid?), Notes?, Items (ICollection<MaterialRequisitionItem>)
  - **MaterialRequisitionItem** (same file): RequisitionId (Guid), ItemId (Guid), ItemName, Sku, Unit, RequestedQuantity, ApprovedQuantity?, EstimatedCost?, Notes?, Requisition (nav)
- **PaymentMethodItem**: Code, NameAr, NameEn, Type ("cash"/"card"/etc.), LinkedAccountCode, LinkedAccountName, Icon, CommissionPercent?, RequiresReference, Status
- **Product**: Sku, Barcode?, NameAr, NameEn, Category, Unit, CurrentStock, AverageCost, LastPurchaseCost, StandardCost?, SellingPrice, VatRate, MinStockLevel, Notes?
- **ProductCategory**: Code, NameAr, NameEn, ItemCount, Description?
- **Quotation**: QuotationNumber, Type, PartyName, PartyPhone?, PartyEmail?, PartyVatNumber?, Date, ValidUntil, PaymentTerms?, Subtotal, DiscountTotal, VatTotal, GrandTotal, Status, ConvertedInvoiceId (Guid?), Notes?, TermsAndConditions?, Items (ICollection<QuotationItem>)
  - **QuotationItem** (same file): QuotationId (Guid), ItemId (Guid), ItemName, Sku, Unit, Quantity, UnitPrice, Discount, VatRate, VatAmount, TotalBeforeVat, TotalAfterVat, Quotation (nav)
- **StockMovement**: ItemId (Guid), ItemName, Date, Type (StockMovementType), Quantity, UnitCost, UnitPrice?, ReferenceNumber, RemainingStock
- **Subscription** (file also declares enums `SubscriptionPlanType`, `BillingCycle`, `SubscriptionStatus` — duplicating/conflicting with `ERP.Domain.Enums.AppEnums.cs`'s own `SubscriptionPlanId`/`SubscriptionStatus`): TenantId (Guid — note: `Guid`, not `string` as `ITenantEntity` expects), PlanType, PlanNameAr, PlanNameEn, BillingCycle, Price, VatAmount, TotalAmount, StartDate, ExpiryDate, Status, PaymentMethod, TransactionReference, AutoRenew, MaxUsers, MaxBranches, ZatcaPhase2Enabled
- **Supplier**: Code, NameAr, NameEn, VatNumber?, CrNumber?, Phone?, Email?, ContactPerson?, City?, Address?, BankName?, Iban?, SwiftCode?, PaymentTermsDays, OpeningBalance, CurrentBalance, AccountCode, Currency?, Status, Notes?
- **Tenant**: Code, NameAr, NameEn, VatNumber, CrNumber, Address, City, Country, Phone, Email, Currency, LogoUrl?, FinancialYearStart, FinancialYearEnd, IsActive, ZatcaConfig (owned entity)
- **UnitOfMeasure**: Code, NameAr, NameEn, Symbol, IsBaseUnit, BaseUnitCode?, ConversionFactor, Status
- **User**: Name, Email, Phone, PasswordHash?, Role (UserRole), AvatarInitials?, AvatarUrl?, JobTitle?, Department?, IsActive, LastLoginAt?
- **Voucher**: VoucherNumber, Type (VoucherType), Date, Amount, AmountInWordsAr, PartyName, PartyAccountCode, TreasuryAccountCode, PaymentMethod, IsSplitPayment, ReferenceNumber?, Notes, JournalEntryId (Guid?), ReceivedOrPaidBy
- **Warehouse**: Code, NameAr, NameEn, Location, ManagerName?, Phone?, IsDefault, Status
- **ZatcaConfig** (plain class, owned by Tenant, not a `BaseEntity`): Environment (ZatcaEnvironment), ComplianceStatus, Csid?, BinarySecurityToken?, SecretKey?, SolutionName, SolutionVersion, RegisteredDevice?, AutoSendInvoices, ApiKey?, ApiSecret?, CertificatePem?, PrivateKeyPem?, Otp?, CustomEndpointUrl?, LastTestDate?, LastTestStatus?, LastTestLatencyMs?, LastTestMessage?

**Total: 27 entity files, ~30 classes** (counting nested item/history classes).

### Entities referenced elsewhere but that DO NOT EXIST in ERP.Domain (critical gap)
`ERP.Application/Interfaces/IApplicationDbContext.cs`, `ERP.Infrastructure/Persistence/ApplicationDbContext.cs`, the `Features/CarShowroom` commands, `CarShowroomService.cs`, and `ReportsController.cs` all reference these types as if they exist — **none of them are defined anywhere in the current codebase**:
`CarAgent`, `CarBrand`, `CarModel`, `CarProcurementOrder` (+ nested `CarProcurementOrderItem`), `CarSalesContract`, `CarTrim`, `Vehicle`.
A now-deleted sibling project (`RayahAccounting.Domain`, referenced by the orphaned `RayahAccounting.Application` folder but absent from disk) is almost certainly where these once lived — they need to be rebuilt from scratch in `ERP.Domain` for the car-showroom module described in Phase 1's scope.

## Enums

All in `ERP.Domain/Enums/AppEnums.cs` (single file), namespace `ERP.Domain.Enums`:
- `InvoiceType`: StandardTaxInvoice, SimplifiedTaxInvoice, DebitNote, CreditNote, PurchaseInvoice
- `VoucherType`: Receipt, Payment
- `PaymentMethod`: Cash, BankTransfer, Mada, CreditCard, Cheque, ApplePay
- `AccountCategory`: Asset, Liability, Equity, Revenue, Expense
- `LinkedEntityType`: General, Customer, Supplier, Bank
- `ZatcaEnvironment`: Sandbox, Simulation, Production
- `ComplianceStatus`: NotEnrolled, InProgress, Compliant
- `ZatcaSubmissionStatus`: NotSubmitted, Cleared, Reported, Rejected, Warning
- `CostingMethod`: MovingAverage, FIFO, LastPurchase, Standard
- `StockMovementType`: InPurchase, OutSales, AdjustmentIn, AdjustmentOut
- `JournalEntryStatus`: Draft, Posted, Reversed
- `SubscriptionPlanId`: Starter, Professional, Enterprise
- `SubscriptionStatus`: Active, Trial, Expired
- `UserRole`: Owner, Admin, GeneralManager, ChiefAccountant, SalesOrder

Additionally, `ERP.Domain/Entities/Subscription.cs` locally declares its **own, conflicting** `SubscriptionPlanType`, `BillingCycle`, and a second `SubscriptionStatus` (Active, Trial, Expired, **Suspended**) inside the `ERP.Domain.Entities` namespace — a duplicate/near-duplicate of the enums above with a different member set, which is itself a code-quality gap.

Enums referenced by Application/Infrastructure code but **not defined anywhere**: `ProcurementStage`, `ProcurementOrderStatus`, `CarSalesCycleType`, `BuyerType`, `SalesContractStatus`, `ZatcaPhase2Status`, `VehicleCondition`, `FuelType`, `TransmissionType`, `VatMode`, `VehicleStatus`, `InvoiceKind`.

---

## Application Layer

`ERP.Application` references only `ERP.Domain` and `Microsoft.EntityFrameworkCore` (no MediatR package, despite MediatR being used — see Gaps).

**Interfaces** (`ERP.Application/Interfaces/`):
- `IApplicationDbContext.cs` — the EF Core context abstraction; declares `DbSet<T>` for every entity **plus** the 7 non-existent car-showroom entities (`CarAgent`, `CarBrand`, `CarModel`, `CarProcurementOrder`, `CarSalesContract`, `CarTrim`, `Vehicle`) and `SaveChangesAsync`.
- `IAccountingService.cs` — also defines DTOs `JournalEntryLineDto`, `CreateJournalEntryCommand`, `TrialBalanceItemDto`, `FinancialReportSummaryDto`, `AccountLedgerEntryDto`. Interface: `CreateJournalEntryAsync`, `GetTrialBalanceAsync`, `GetFinancialSummaryAsync`, `GetAccountStatementAsync`, `PostAutomaticInvoiceJournalAsync`, `PostAutomaticVoucherJournalAsync`. **This is the centralized accounting-engine abstraction** (journal entry creation + posting + trial balance + account statement) — the intended single source of truth for double-entry bookkeeping.
- `IInventoryService.cs` — DTOs `StockMovementDto`, `MovingAverageSimulationResult`. Interface: `RecordMovementAsync`, `SimulateWeightedAverageCostAsync`, `ApplyMovingAverageCostAsync`, `GetLowStockAlertsAsync`. **This is the intended centralized inventory/stock abstraction** (stock in/out + moving-average costing), meant to be reused rather than duplicated per module.
- `IAccountSuggestionService.cs` — `GetInvoiceSuggestions(Invoice)`, `GetVoucherSuggestions(Voucher)`; returns suggested Dr/Cr account codes (UI helper, not itself a posting engine).
- `IReportsService.cs` — `GetVatReturnReportAsync(year, quarter)` → `VatDeclarationReportDto`.
- `ITenantService.cs` — `CurrentTenantId` (string, get), `SetCurrentTenant(string)`.
- `ICarShowroomService.cs` — `ProcessProcurementStageTransitionAsync`, `SyncVehiclesFromProcurementOrderAsync`, `ProcessSalesContractStatusTransitionAsync`, `GenerateInvoiceFromContractAsync`. All reference the missing car-showroom entities.

**Accounting** (`ERP.Application/Accounting/AccountingService.cs`): one implementation of `IAccountingService`. Builds journal entries, validates debit=credit balance, computes trial balance from `Account.DebitBalance`/`CreditBalance` (properties that don't exist on the real `Account` entity — it only has `Balance`), and has hardcoded automatic-journal templates for sales/purchase invoices and receipt/payment vouchers (Arabic account names hardcoded, e.g. "1141 مخزون البضاعة"). **Note: there is a second, different `IAccountingService` implementation in `ERP.Infrastructure/Services/AccountingService.cs` — see Infrastructure section — the two disagree on the `Account`/`JournalEntry`/`Invoice` shape.**

**Costing** (`ERP.Application/Costing/MovingAverageCalculator.cs`): static helper `CalculateNewAverageCost(currentStock, currentAverageCost, incomingQty, incomingUnitPrice)` implementing the moving-weighted-average formula. Pure, stateless, reusable — a good centralization candidate, but currently unused by `InventoryService` (which reimplements the same formula inline).

**Inventory** (`ERP.Application/Inventory/InventoryService.cs`): one implementation of `IInventoryService`, reads/writes `Product.CurrentStock`/`WeightedAverageCost` (property that doesn't exist on the real `Product` entity — it has `AverageCost`) and `StockMovementType.InPurchase/InTransfer/InitialStock/OutSale/OutTransfer` (only `InPurchase`/`OutSales`/`AdjustmentIn`/`AdjustmentOut` exist on the real enum). **A second, different `IInventoryService` implementation exists in `ERP.Infrastructure/Services/InventoryService.cs`.**

**Invoices** (`ERP.Application/Invoices/InvoiceDtos.cs`): `InvoiceResponseDto`, `CreateInvoiceRequestDto`, `CreateInvoiceItemRequestDto` — references `ZatcaPhase2Status` (undefined enum) and doesn't match the real `Invoice` entity's field names. Not wired to any controller (no invoice creation endpoint currently uses these DTOs — `InvoicesController` uses raw `Invoice` entity binding via `BaseCrudController`).

**Reports** (`ERP.Application/Reports/ReportsService.cs`): one implementation of `IReportsService.GetVatReturnReportAsync`, computes a real (if simplistic — zero-rated/exempt always 0) VAT return from `Invoice.Subtotal`/`VatTotal` grouped by quarter.

**Features/CarShowroom/Commands** (MediatR-pattern command+handler pairs, all reference the missing car-showroom entities and enums):
- `CreateCarProcurementOrderCommand` / Handler — creates a `CarProcurementOrder` with items, optionally advances its stage.
- `AdvanceProcurementStageCommand` / Handler — validates order exists for tenant, then calls `ICarShowroomService.ProcessProcurementStageTransitionAsync`.
- `CreateCarSalesContractCommand` / Handler — creates a `CarSalesContract`, optionally transitions its status.
- `UpdateCarSalesContractStatusCommand` / Handler — updates handover info and transitions status.

None of these commands compile today (missing entities/enums, missing MediatR package reference in the `.csproj`).

---

## Infrastructure Layer

`ERP.Infrastructure` references `ERP.Application`, `ERP.Domain`, `Microsoft.EntityFrameworkCore` + `Microsoft.EntityFrameworkCore.SqlServer` + `Microsoft.AspNetCore.Http.Abstractions`.

**DbContext** (`ERP.Infrastructure/Persistence/ApplicationDbContext.cs`): `ApplicationDbContext : DbContext, IApplicationDbContext`. Exposes all 34 `DbSet<T>` properties declared on `IApplicationDbContext` (28 for real entities + 6 for the missing car-showroom types, i.e. it will not compile). `OnModelCreating` configures: `Tenant.ZatcaConfig` as owned entity; one-to-many for `Invoice→Items`, `JournalEntry→Lines`, `Quotation→Items`, `MaterialRequisition→Items`, `CommercialOrder→Items` (shadow FK), `ApprovalRequest→History`, `CarProcurementOrder→Items`; a **global multi-tenant query filter** applied reflectively to every entity assignable to `BaseEntity` filtering on `TenantId == currentTenantId` (a `Guid`, not the `string` used by `ITenantEntity`/`ITenantService` — another type mismatch); decimal precision configured for `Account.Balance`, `Invoice.TotalAmount` (doesn't exist — real property is `GrandTotal`), `JournalEntryLine.Debit`/`Credit`. `SaveChangesAsync` override stamps `TenantId`/`CreatedAt`/`UpdatedAt` on `BaseEntity` entries via `ChangeTracker`.

**Migrations**: **None.** No `Migrations` folder exists anywhere in the solution — no EF migration has ever been generated, so nothing has been "applied" to any real database. Combined with `Program.cs` using `UseInMemoryDatabase`, no schema has ever actually been created against SQL Server.

**Repositories**: No repository pattern — controllers and services talk to `ApplicationDbContext`/`IApplicationDbContext` directly (this is fine as an architectural choice, just noting there's no repository abstraction layer to inventory).

**Identity / Auth**: No ASP.NET Identity, no JWT issuance/validation package, no `[Authorize]` attribute anywhere in the codebase (confirmed via repo-wide grep). `ERP.Infrastructure/MultiTenancy/TenantResolverMiddleware.cs` defines `TenantService : ITenantService` (in-memory, defaults `CurrentTenantId = "tenant-1"`) and `TenantResolverMiddleware`, which reads the `X-Tenant-Id` header and calls `tenantService.SetCurrentTenant(...)`. That's the entire "auth" surface at the Infrastructure level — `AuthController.Login` (API layer) returns a hardcoded dummy JWT string with no real validation.

**Services** (`ERP.Infrastructure/Services/`) — these are a **second, parallel, and incompatible implementation** of the same interfaces implemented in `ERP.Application` (both get registered/compiled against the same interface names, but only one set is wired in `Program.cs` — the Infrastructure ones):
- `AccountSuggestionService.cs` — implements `IAccountSuggestionService`, hardcoded Arabic account-code suggestions per invoice/voucher type. Self-consistent with the real entities.
- `AccountingService.cs` — implements `IAccountingService` again, but uses property names that don't exist on the real entities (`JournalEntry.EntryDate`/`DescriptionAr`/`Status` as string, `JournalEntryLine.CostCenterCode`, `Invoice.InvoiceType`/`Kind`/`InvoiceHashSha256`, `Product.StockQuantity`/`CostPrice`, etc.) — internally consistent with the "missing" richer model, not with `ERP.Domain` as it exists.
- `CarShowroomService.cs` — implements `ICarShowroomService`; the fullest picture of the intended car-showroom workflow (7-stage procurement: Requisition → RequisitionApproved → RFQ → RFQApproved → PurchaseOrder → VinReceived → Invoiced; sales contract lifecycle: Draft → Approved → Allocated → Delivered → Invoiced/Cancelled), auto-creates `Vehicle` records from VINs, auto-generates purchase/sales invoices and posts journal entries via `IAccountingService`. Entirely dependent on the non-existent entities/enums.
- `InventoryService.cs` — implements `IInventoryService` again, using `Product.StockQuantity`/`CostPrice`/`SalePrice`/`ReorderLevel` (none exist on real `Product`) and `StockMovementType.Purchase`/`SalesReturn` (don't exist on real enum).

**Zatca** (`ERP.Infrastructure/Zatca/ZatcaPhase2Service.cs`): `IZatcaPhase2Service` + impl. Generates SHA-256 invoice hash, a hand-built UBL 2.1 XML string (not schema-validated, references `invoice.Kind`/`InvoiceKind.Sales` which don't exist), a simulated RSA-based "signature" (not real ZATCA CSID/ECDSA cryptographic-stamp flow), and a TLV Base64 QR code with only 6 of the 9 ZATCA-mandated tags. `SubmitInvoiceToZatcaAsync` is a **pure mock** — always returns success with canned validation messages; there is no actual HTTP call to any ZATCA endpoint despite `appsettings.json` having real ZATCA simulation URLs configured. This service is also not referenced/injected anywhere (not registered in `Program.cs`, not called by any controller).

---

## API Layer

`ERP.Api` (root namespace `ERP.WebApi`) references all three other projects; packages: `Swashbuckle.AspNetCore`, `Microsoft.EntityFrameworkCore.Design`.

**Program.cs**: `AddControllers`, Swagger/OpenAPI with a Bearer-token security definition (cosmetic only — nothing validates a token), `AddDbContext<ApplicationDbContext>` using **`UseInMemoryDatabase("ErpDb")`** (comment says "Use SQL Server/PostgreSQL in production" — never switched), `AddMediatR` (package not referenced — won't compile), `AddHttpContextAccessor`, DI registrations for `ICurrentTenantService`/`IAccountingService`/`IInventoryService`/`IAccountSuggestionService`/`ICarShowroomService` all pointed at the **Infrastructure** implementations (`ICurrentTenantService` is itself undefined anywhere in the repo — third compile-breaking gap), CORS policy `"AllowAll"` (any origin/method/header — not production-safe), `UseSwagger/UseSwaggerUI` (dev only), `UseCors`, `UseMiddleware<TenantResolverMiddleware>`, `UseAuthorization` (called with **no** matching `UseAuthentication` and no auth scheme configured — a no-op), `MapControllers`. **No global exception handling middleware** (no `UseExceptionHandler`, no custom exception filter).

Most controllers are trivial subclasses of `ERP.Api/Controllers/Common/BaseCrudController<T>` (generic `GET /`, `GET /{id}`, `POST /`, `PUT /{id}`, `DELETE /{id}`, operating on `ApplicationDbContext.Set<T>()`), several override `GetAll`/`GetById` to add `.Include()` for child collections.

| Controller | Routes (verb + path) | Purpose |
|---|---|---|
| `AccountsController` | `api/v1/accounts` — full CRUD (Base) | Chart of accounts CRUD |
| `ApprovalRequestsController` | `api/v1/approvalrequests` — full CRUD + custom `GetAll`/`GetById` with `.Include(History)` | Approval workflow requests |
| `AuthController` (`ERP.WebApi.Controllers`) | `POST api/v1/auth/login` (returns hardcoded dummy token, no real validation), `POST api/v1/auth/register-company` (validates VAT format, builds `Tenant`+`Subscription` in memory but **never saves to DB** — no `_context.Add`/`SaveChanges` call) | "Auth" — not a real auth system |
| `BanksController` | `api/v1/banks` — full CRUD (Base) | Bank accounts CRUD |
| `CommercialOrdersController` | `api/v1/commercialorders` — full CRUD + `Include(Items)` overrides | Sales/purchase orders |
| `CostCentersController` | `api/v1/costcenters` — full CRUD (Base) | Cost centers CRUD |
| `CostingController` (`ERP.WebApi.Controllers`) | `GET api/v1/costing`, `POST api/v1/costing/recalculate-all` (no-op: multiplies cost by 1.0), `POST api/v1/costing/policy` (doesn't persist) | Costing summary / stub recalculation |
| `CurrenciesController` | `api/v1/currencies` — full CRUD (Base) | Currency master data |
| `CustomersController` | `api/v1/customers` — full CRUD (Base) | Customer master data |
| `DashboardController` | `GET api/v1/dashboard/stats`, `GET api/v1/dashboard/recent-activity` | KPI aggregates (sales, collections, low stock, pending approvals, net profit) |
| `DatabaseSyncController` (`ERP.WebApi.Controllers`) | `GET api/v1/database/status`, `POST api/v1/database/sync` | Table row counts + bulk upsert sync endpoint for offline-first frontend sync; references the 7 missing car-showroom entities |
| `FixedAssetsController` | `api/v1/fixedassets` — full CRUD (Base) | Fixed asset register |
| `InvoicesController` | `api/v1/invoices` — full CRUD + `GET {id}/suggestions`, `POST {id}/post` | Invoices + account suggestions + manual posting trigger |
| `JournalEntriesController` | `api/v1/journalentries` — full CRUD + `Include(Lines)` overrides | Manual journal entries (no reversal endpoint despite README claiming one) |
| `LookupController` | `GET api/v1/lookup/accounts,customers,suppliers,products,warehouses,cost-centers,currencies` | Lightweight dropdown/lookup projections |
| `MaterialRequisitionsController` | `api/v1/materialrequisitions` — full CRUD + `Include(Items)` overrides | Material requisitions |
| `NotificationsController` | `api/v1/notifications` — full CRUD (Base) | In-app notifications |
| `PaymentMethodsController` | `api/v1/paymentmethods` — full CRUD (Base) | Payment method master data |
| `ProductCategoriesController` | `api/v1/productcategories` — full CRUD (Base) | Product category master data |
| `ProductsController` | `api/v1/products` — full CRUD (Base) | Product catalog (no stock/cost simulate endpoint wired despite `IInventoryService` having one) |
| `QuotationsController` | `api/v1/quotations` — full CRUD + `Include(Items)` overrides | Sales/purchase quotations |
| `ReportsController` (`ERP.WebApi.Controllers`) | `GET trial-balance`, `GET summary`, `GET vat-return`, `GET inventory-audit`, `GET item-movements`, `GET item-ledger-detail`, `GET trade-commercial`, `GET car-sales-performance`, `GET car-vin-inventory`, `GET car-zatca-margin`, `GET car-procurement-track` (all under `api/v1/reports`) | The richest controller; several report actions reference the non-existent car-showroom entities/properties and won't compile |
| `StockMovementsController` | `api/v1/stockmovements` — full CRUD (Base) + `POST record` (calls `IInventoryService.RecordMovementAsync`) | Stock movement ledger |
| `SubscriptionsController` (`ERP.WebApi.Controllers`) | `GET plans`, `GET current`, `POST upgrade` (all under `api/v1/subscriptions`) | Fully hardcoded/mocked — no DB reads for plans/current/upgrade despite injecting `ApplicationDbContext` |
| `SuppliersController` | `api/v1/suppliers` — full CRUD (Base) | Supplier master data |
| `TenantsController` (`ERP.WebApi.Controllers`) | `api/v1/tenants` — full custom CRUD (not using `BaseCrudController`) | Tenant management |
| `UnitsOfMeasureController` | `api/v1/unitsofmeasure` — full CRUD (Base) | UoM master data |
| `VouchersController` | `api/v1/vouchers` — full CRUD (Base) + `POST {id}/post` | Receipt/payment vouchers + posting trigger |
| `WarehousesController` | `api/v1/warehouses` — full CRUD (Base) | Warehouse master data |

**Total: 28 controllers** (27 `Controllers/*.cs` + `Common/BaseCrudController.cs` base class).

**Auth/CORS/Swagger/Middleware summary**: Swagger UI configured (dev-only) with a cosmetic Bearer scheme; CORS is wide-open (`AllowAnyOrigin/Method/Header`); no authentication scheme is registered or enforced anywhere (`UseAuthorization()` has nothing to authorize against); no global exception handler/middleware; tenant resolution is via a custom `X-Tenant-Id` header middleware only (no validation that the tenant exists or that the caller is entitled to it).

---

## Config & Tooling

- **DB provider**: `Microsoft.EntityFrameworkCore.SqlServer` package is referenced in `ERP.Infrastructure`, and `appsettings.json` has a SQL Server `DefaultConnection` connection string (`Server=localhost;Database=ERPDb;User Id=sa;...`). However, `Program.cs` currently wires up `UseInMemoryDatabase("ErpDb")` instead — **the SQL Server connection string is present but unused**, and nothing has ever actually run against a real database (no migrations exist either).
- **appsettings.json** (`ERP.Api/appsettings.json`): sections present — `Logging`, `AllowedHosts`, `ConnectionStrings:DefaultConnection`, `ZatcaSettings` (Environment, PortalBaseUrl, ComplianceCsidUrl, InvoicesClearanceUrl, InvoicesReportingUrl — all pointing at ZATCA's public simulation gateway). No `appsettings.Development.json` / per-environment files found. No JWT/Identity/secrets sections.
- **Directory.Build.props**: none found anywhere in the repo.
- **Docker**: no `Dockerfile` or `docker-compose*` files found anywhere in the repo.
- **Tests**: no test projects found (no `*.Tests.csproj` or similar) — zero automated test coverage.
- **README.md** (repo root) describes an *aspirational* architecture (`RayahAccounting.sln`, `src/RayahAccounting.Domain|Application|Infrastructure|WebApi`, with a richer entity/enum list) that **does not match the actual `ERP.sln` / `ERP.*` project layout on disk** — it's stale documentation from the earlier "RayahAccounting"-named iteration and should not be trusted as a guide to current structure.
- **Stray project**: `RayahAccounting.Application/` at the repo root (Accounting, Costing, Interfaces, Inventory subfolders) is not referenced by `ERP.sln`, and its sibling `RayahAccounting.Domain` (which it depends on for entities like `CarBrand`, `Vehicle`, `CarProcurementOrder`, etc.) does not exist on disk. It's dead code but is the likely origin of the "phantom" car-showroom types referenced throughout `ERP.Application`/`ERP.Infrastructure`/`ERP.Api`.

---

## Gaps / Unfinished Work

This section is unusually large because the repository is less "90% done, a few gaps" and more "extensive scaffolding across 4 layers that was never made internally consistent." In priority order:

1. **`BaseEntity` is not defined anywhere.** Every entity in `ERP.Domain` fails to compile (30 `CS0246` errors from `dotnet build`). This alone means the *entire solution* is currently unbuildable. Must be created first (likely needs `Id` (Guid or string — needs a decision, entities like `Infrastructure/Services/InventoryService.cs` treat IDs as parseable `Guid` strings while `ERP.Application/Inventory/InventoryService.cs` treats `Product.Id` as a raw `string`), `TenantId`, `CreatedAt`, `UpdatedAt` based on how it's consumed in `ApplicationDbContext`).
2. **`ICurrentTenantService` is not defined anywhere**, yet it's required by `ApplicationDbContext`'s constructor and registered/consumed in `Program.cs` (`AddScoped<ICurrentTenantService, ERP.Infrastructure.Services.CurrentTenantService>()` — `CurrentTenantService` class also doesn't exist). Second compile-breaking gap. Note it's a *different* abstraction from the also-present `ITenantService`/`TenantService` (string-based, header-driven) — the codebase currently has two incompatible, half-built tenant-context mechanisms (one `Guid`-based via `ICurrentTenantService`, one `string`-based via `ITenantService`) and neither is complete.
3. **Seven car-showroom entities referenced but never created**: `CarAgent`, `CarBrand`, `CarModel`, `CarProcurementOrder`, `CarSalesContract`, `CarTrim`, `Vehicle` (plus `CarProcurementOrderItem`). Used throughout `IApplicationDbContext`, `ApplicationDbContext`, `CarShowroomService`, the `Features/CarShowroom` MediatR commands, `DatabaseSyncController`, and half of `ReportsController`. None will compile. This is the single largest missing chunk needed for the car-showroom module the frontend expects.
4. **A dozen+ enums referenced but never defined**: `ProcurementStage`, `ProcurementOrderStatus`, `CarSalesCycleType`, `BuyerType`, `SalesContractStatus`, `ZatcaPhase2Status`, `VehicleCondition`, `FuelType`, `TransmissionType`, `VatMode`, `VehicleStatus`, `InvoiceKind`.
5. **MediatR is used but never referenced as a NuGet package** in any `.csproj` (`ERP.Application/ERP.Application.csproj` has no `MediatR` `PackageReference`, yet `IRequest`/`IRequestHandler` are used in `Features/CarShowroom/Commands/*`, and `Program.cs` calls `AddMediatR`). Third/fourth compile-breaking package gap.
6. **Duplicate, incompatible service implementations.** `IAccountingService` and `IInventoryService` are each implemented *twice* — once in `ERP.Application` (`Accounting/AccountingService.cs`, `Inventory/InventoryService.cs`) and once in `ERP.Infrastructure/Services/` — and the two implementations disagree on the shape of `Account`, `JournalEntry`, `JournalEntryLine`, `Product`, `Invoice` (different, non-existent property names on each side, e.g. `Account.DebitBalance`/`CreditBalance` vs. `Account.Balance`; `Product.WeightedAverageCost` vs `Product.CostPrice`/`StockQuantity` vs the real `Product.AverageCost`/`CurrentStock`). Only the Infrastructure versions are wired up in `Program.cs`; the Application-layer versions appear to be dead/orphaned code. **Neither is consistent with the real `ERP.Domain` entities**, so once `BaseEntity` is fixed, both will still fail to compile against the real entity shapes and need to be reconciled/rewritten against the actual `Account`/`Product`/`Invoice`/`JournalEntry` fields.
7. **No real authentication/authorization.** `AuthController.Login` returns a hardcoded dummy JWT string regardless of credentials; `register-company` never persists the new tenant/subscription to the database; there is no ASP.NET Identity, no JWT bearer validation package/config, no `[Authorize]` attribute anywhere, and `Program.cs` calls `UseAuthorization()` with no authentication scheme registered (a no-op). All 28 controllers are effectively anonymous/unprotected.
8. **No real ZATCA integration.** `ZatcaPhase2Service` builds a hand-rolled UBL XML (referencing the non-existent `InvoiceKind` enum), a simulated RSA "signature" (not ZATCA's actual cryptographic-stamp/CSID flow), and a QR code with only 6 of 9 mandated TLV tags. `SubmitInvoiceToZatcaAsync` always returns a canned success — no HTTP call is made to the ZATCA simulation endpoints configured in `appsettings.json`. The service also isn't registered in DI or called from any controller, so it's currently unreachable from the API surface entirely.
9. **No database has ever actually been created.** `Program.cs` uses `UseInMemoryDatabase`; there is no `Migrations` folder; the SQL Server connection string in `appsettings.json` is unused. Nothing here reflects real, applied schema.
10. **No tests, no Docker, no Directory.Build.props, no CI config found.**
11. **No global exception handling middleware** — unhandled exceptions will produce raw ASP.NET default error responses (or leak stack traces in dev).
12. **Many "mocked" or non-persisting endpoints presented as if real**: `SubscriptionsController` (`plans`/`current`/`upgrade`) is 100% hardcoded despite injecting `ApplicationDbContext`; `AuthController.register-company` builds domain objects but never saves them; `CostingController.recalculate-all` is a no-op (`product.CostPrice *= 1.0m`) and `SetPolicy` doesn't persist; `ReportsController`'s `item-ledger-detail`/`inventory-audit` synthesize plausible-looking numbers (e.g. `physicalQuantity = sysQty` always, fake opening-balance rows) rather than reading real historical stock-movement data — there's no real stock-movement-based ledger/audit trail despite `StockMovement` existing as an entity.
13. **`Subscription` entity duplicates enums** already defined in `AppEnums.cs` (`SubscriptionPlanType`/`BillingCycle`/second `SubscriptionStatus`) with a different member set — will cause ambiguity/confusion once the Application layer is fixed to build against real types.
14. **Multi-tenancy type mismatch**: `ITenantEntity.TenantId` is `string`; `ApplicationDbContext`'s reflective query filter and `SaveChangesAsync` treat `BaseEntity.TenantId` as `Guid`; `Subscription.TenantId` is explicitly `Guid`; `ITenantService.CurrentTenantId` is `string`. These need to be unified to one type before multi-tenancy can work correctly.
15. **No repository/unit-of-work abstraction** beyond direct `DbContext` access (a valid choice, but worth deciding deliberately for Phase 2 rather than by default).
16. **`JournalEntriesController` has no reverse endpoint** despite the README (stale doc) claiming `POST /journalentries/{id}/reverse`, and despite `JournalEntryStatus.Reversed` existing as an enum value — the reversal workflow described by the enum is never implemented anywhere.
17. **README.md is stale/misleading** — describes the old `RayahAccounting.*` project names/paths and a `ZatcaController` that doesn't exist under the current `ERP.*` naming; should be rewritten once Phase 2 stabilizes the structure.
