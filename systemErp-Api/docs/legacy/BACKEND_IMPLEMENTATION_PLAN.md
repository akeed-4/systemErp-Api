# Backend Implementation Plan

Companion to `BACKEND_FRONTEND_MAPPING.md`. Built from a full read-only survey of both
`SystemErp-web` (Angular 19) and `SystemErp-Api/systemErp-Api` (.NET 9, Clean Architecture:
`ERP.Domain` / `ERP.Application` / `ERP.Infrastructure` / `ERP.Api`). Full detail in
`SystemErp-web/.backend-analysis/frontend-inventory.md` and
`SystemErp-Api/systemErp-Api/.backend-analysis/backend-inventory.md`.

## Headline finding

**Phase 0 is now done: `dotnet build ERP.sln` succeeds with 0 errors, 0 warnings**, and the API
starts and serves real requests (verified: `GET /api/v1/accounts`, `/api/v1/dashboard/stats`,
`/api/v1/database/status`, `/api/v1/reports/trial-balance` all return 200 against the new
`AccountingService`). It was not a "90% done, a few gaps" backend when this plan was written —
it was a Clean Architecture skeleton where `ERP.Domain` (the real, consistent entity set) and
`ERP.Application`/`ERP.Infrastructure`/`ERP.Api` (written against a different, richer,
never-materialized model — almost certainly leftovers from an orphaned `RayahAccounting.*`
project no longer on disk) were never reconciled. Nothing past Phase 0 below
can be verified until the build passes.

---

## Architectural decisions (stated up front, per your instruction not to guess silently)

These affect nearly every file that follows, so I'm deciding them now with reasoning rather than
asking mid-implementation. Redirect me if any of these should go the other way.

1. **`BaseEntity.Id` type → `Guid`.** Matches how IDs are treated in most existing code
   (`Invoice.Uuid`, FK fields like `AssetAccountId`, `RequesterUserId` are all `Guid`). Frontend
   sends/receives them as strings over JSON either way, so this is invisible to Angular.
2. **`TenantId` type → `Guid`, unified everywhere.** Today there are three disagreeing versions:
   `ITenantEntity.TenantId: string`, `ICurrentTenantService` (undefined, implied `Guid` by
   `ApplicationDbContext`'s reflective query filter), and `Subscription.TenantId: Guid`. Since
   `TenantId` is a foreign key to `Tenant.Id` (which becomes `Guid` under decision #1), `Guid` is
   the relationally-correct choice. The `X-Tenant-Id` HTTP header stays a string on the wire and
   gets parsed to `Guid` in `TenantResolverMiddleware`.
3. **Keep one tenant-context abstraction, not two.** Delete `ICurrentTenantService` (never
   defined, and redundant with the working `ITenantService`/`TenantService`/
   `TenantResolverMiddleware` trio). Update it to expose `Guid CurrentTenantId`.
4. **MediatR: keep, fix, and use consistently for business operations — not for plain CRUD.**
   It's already the pattern started for car-showroom stage transitions; the package is just
   missing from the `.csproj`. Rule going forward: simple CRUD stays on `BaseCrudController<T>`
   (no ceremony needed); anything that's a named business operation (advance a stage, post a
   journal entry, checkout a POS cart, convert a quotation to an invoice) becomes a
   Command+Handler. This keeps business logic in one discoverable place per operation instead of
   scattered across controller actions.
5. **One accounting engine, one inventory engine — delete the duplicates.** `IAccountingService`
   and `IInventoryService` are each implemented twice (`ERP.Application` and
   `ERP.Infrastructure`), and neither implementation's property names match the real `ERP.Domain`
   entities. Since both only need `IApplicationDbContext` (no framework-specific dependency),
   they belong in `ERP.Application` per Clean Architecture (Infrastructure should hold
   EF/external-integration concerns, not business rules). Plan: delete
   `ERP.Infrastructure/Services/AccountingService.cs` and `InventoryService.cs`, rewrite the
   `ERP.Application` versions from scratch against the real `Account`/`JournalEntry`/
   `JournalEntryLine`/`Product`/`StockMovement` fields, and make every module (sales, purchases,
   POS, car showroom) call these instead of hand-rolling journal entries inline the way
   `erp.service.ts` does today on the frontend.
6. **Chart-of-accounts codes become seeded configuration, not literals.** The frontend hardcodes
   `112`=customers, `211`=suppliers, `213`=output VAT, `411`=revenue in >10 places. Backend will
   seed these same codes as real `Account` rows on tenant creation and read them by a small
   `IDefaultAccountsProvider` (keyed by `AccountUsage.CustomerReceivable`, `.SupplierPayable`,
   `.OutputVat`, `.ContractRevenue`, etc.) rather than hardcoding the codes again in C#.
7. **Real auth: ASP.NET Core Identity + JWT bearer**, roles mapped 1:1 to the frontend's closed
   `UserRole` enum (`owner|admin|general_manager|chief_accountant|sales_rep`), policy-based
   authorization keyed by `screenId`+`action` to match `ScreenPermission` exactly (so the
   Angular `PermissionService.hasPermission` model needs zero changes, just a real server behind
   it). `AuthController.Login`/`register-company` get rewritten to actually persist and issue
   real tokens.
8. **Database: switch off `UseInMemoryDatabase` onto the existing SQL Server connection string**,
   generate a first real EF Core migration once entities stabilize. Never modify a migration
   after it's applied — always add a new one.
9. **Real ZATCA cryptographic signing is explicitly deferred and flagged, not silently stubbed
   forever.** The Phase-1 TLV/QR code generator (`zatca-tlv.util.ts` on the frontend,
   mirrored server-side) is genuinely correct and will be finished. Real Phase-2 signing (CSR
   generation, compliance CSID, ECDSA invoice-hash chaining, UBL 2.1 XML against ZATCA's schema,
   and actual HTTP submission to ZATCA's sandbox/production API) requires **real ZATCA onboarding
   credentials/certificates that only you can provide** — this is genuinely not inferable from
   the code, so it's scheduled as Phase 12 and will surface as an explicit question when we get
   there, rather than shipping another fake "always succeeds" simulation.
10. **Orphaned code cleanup**: delete the dead `RayahAccounting.Application` folder (not part of
    `ERP.sln`, depends on a `RayahAccounting.Domain` project that doesn't exist on disk) and
    rewrite the stale root `README.md` (currently describes the old project names/paths) once
    Phase 2 stabilizes structure. Low risk, done opportunistically, not blocking.
11. **`agreements` frontend screen has no matching backend concept and no clear spec** (see
    mapping doc §3) — deferred until product confirms whether it's a thin variant of
    `CommercialOrder`/`Quotation` or needs its own entity. Not blocking other phases.

---

## Phase 0 — Make it compile ✅ DONE (prerequisite to everything else; not in the original 14-phase list because nothing in that list is reachable without it)

**Status: complete.** What actually shipped, beyond the checklist below as originally planned:
- `BaseEntity` created; `TenantId` unified to `Guid` everywhere (`ITenantEntity` deleted, folded
  into `BaseEntity`); `ICurrentTenantService` deleted, `ITenantService` is now the single
  tenant-context abstraction (`Guid? CurrentTenantId`), registered in DI and used by
  `TenantResolverMiddleware` (parses `X-Tenant-Id` header to `Guid`) and `ApplicationDbContext`.
- 7 car-showroom entities created (`Vehicle`, `CarBrand`, `CarAgent`, `CarModel`, `CarTrim`,
  `CarProcurementOrder`+`CarProcurementOrderItem`, `CarSalesContract`) plus the ~10 enums they
  need, as plain settable POCOs matching the rest of `ERP.Domain`'s style (the original code at
  the call sites used 24–35-argument positional constructors against a fictional model — rewritten
  to object-initializer syntax throughout `CarShowroomService.cs` and the 4 MediatR command
  handlers instead of preserved as-is).
- `Invoice` gained `ReferenceType`/`ReferenceId`/`ReferenceNumber` (mirrors the existing
  `JournalEntry` pattern) so auto-generated invoices can link back to their source order/contract.
- `Subscription`'s duplicate local enums deleted in favor of `AppEnums.cs` (added
  `SubscriptionBillingCycle`, added `Suspended` to the shared `SubscriptionStatus`).
- MediatR package added to `ERP.Application.csproj`; `Microsoft.EntityFrameworkCore.InMemory`
  added to `ERP.Api.csproj` (was called in `Program.cs` but never referenced).
- Duplicate `IAccountingService`/`IInventoryService` implementations in `ERP.Infrastructure`
  deleted; the `ERP.Application` versions rewritten from scratch against the real entity fields
  (per decision #5) and wired up in `Program.cs`. `IAccountingService` gained
  `ReverseJournalEntryAsync` (closes the "no `/reverse` endpoint" gap from the inventory — now
  exposed at `POST /api/v1/journalentries/{id}/reverse`).
- Beyond the 30 originally-reported `BaseEntity` errors, the real error count was much higher once
  those were fixed: ~40 more property/type mismatches surfaced across `ReportsController`,
  `DatabaseSyncController`, `AuthController`, `ZatcaPhase2Service`, `InvoiceDtos.cs`, and a missing
  `using ERP.Application.Interfaces;` in ~15 controller files (a second wave of errors the initial
  30-error report didn't reach). All fixed against the real `ERP.Domain` shape — see the commit
  diff for the full list; not re-enumerated here since the mapping doc's per-area tables already
  reflect the corrected reality.
- Verified at runtime, not just compile time: the app starts, DI resolves, and
  `GET /api/v1/accounts`, `/api/v1/dashboard/stats`, `/api/v1/database/status`, and
  `/api/v1/reports/trial-balance` all return `200` against a running instance.
- Still on `UseInMemoryDatabase` intentionally — switching to real SQL Server + first migration is
  Phase 3 as planned, not pulled forward, since it needs the entity set to be stable first (which
  it now is).

Original checklist (kept for reference — all items done):

1. Create `ERP.Domain/Common/BaseEntity.cs`: `Id (Guid)`, `TenantId (Guid)`, `CreatedAt (DateTime)`,
   `UpdatedAt (DateTime?)`. Have `ITenantEntity` either fold into this or stay as a marker — decide
   once touching the file (likely just delete `ITenantEntity` since `TenantId` moves onto
   `BaseEntity` directly; simpler than two mechanisms for the same thing).
2. Fix `TenantId` type drift: `Subscription.TenantId` (`Guid`→stays `Guid`, now inherited),
   `ApplicationDbContext`'s reflective query filter, `SaveChangesAsync` stamping logic.
3. Delete `ICurrentTenantService` references; standardize on `ITenantService.CurrentTenantId: Guid`
   (was `string`) + update `TenantResolverMiddleware` to parse the `X-Tenant-Id` header to `Guid`.
4. Add `MediatR` + `MediatR.Extensions.Microsoft.DependencyInjection` (or current MediatR's
   built-in `AddMediatR`) to `ERP.Application.csproj`.
5. Create the 7 missing car-showroom entities + `CarProcurementOrderItem` in `ERP.Domain/Entities`,
   and the ~12 missing enums in `ERP.Domain/Enums`, using `CarShowroomService.cs`
   (Infrastructure) and the frontend's `car-showroom.models.ts` as the two specs to reconcile
   (Infrastructure's version is closer to production-ready business logic; frontend's is the
   source of truth for exact field/status names Angular expects).
6. Delete `ERP.Infrastructure/Services/AccountingService.cs` and `InventoryService.cs` (the
   duplicate, non-compiling implementations); rewrite `ERP.Application/Accounting/AccountingService.cs`
   and `ERP.Application/Inventory/InventoryService.cs` against the real entity fields (decision #5).
7. Fix `Subscription.cs`'s duplicate/conflicting local enum declarations (decision: keep
   `AppEnums.cs` versions, delete the local ones, reconcile `SubscriptionStatus.Suspended` into
   the shared enum if it's actually needed).
8. `dotnet build ERP.sln` clean. This is the Phase 0 exit criterion — no warnings-as-errors yet,
   just zero `CS****` errors.

## Phase 1 — Analyze ✅ (this document + the mapping doc + the two inventory files)

## Phase 2 — Architecture
Apply decisions #3, #4, #5, #6 structurally: `IDefaultAccountsProvider`, MediatR pipeline
behaviors (validation, logging) if useful, confirm final DI wiring in `Program.cs` matches the
single (Application-layer) service implementations, not the deleted Infrastructure ones.

## Phase 3 — Database / Entities
- Add the ~25 missing entities identified in the mapping doc's summary (Vehicle/car-showroom set
  from Phase 0 step 5; `CommercialContract`+`ContractMilestone`+`ContractClause`;
  `DeliveryNote`+`DeliveryReturnNote`; `ApprovalPolicy`; `ScreenPermission`/`UserRolePermission`;
  full POS set: `PosShift`, `PosTransaction`+Item, `PosCoupon`, `PosOffer`, `CustomerLoyalty`,
  `PosHeldCart`+Item, `PosInvoiceSettings`, `PosSalesReturn`+Item; `CarColor`).
- Switch off `UseInMemoryDatabase`, point at the real SQL Server connection string, generate the
  first migration, apply it to a real dev database.
- Seed default chart-of-accounts + demo tenant matching what the frontend currently fabricates
  client-side (so early manual QA against Angular doesn't regress).

## Phase 4 — Core Services
Real `IAccountingService` (create/post/reverse journal entry, trial balance, account statement,
automatic posting templates driven by `IDefaultAccountsProvider` not literals) and real
`IInventoryService` (stock in/out, moving-average costing via the already-correct
`MovingAverageCalculator`, low-stock alerts) — both properly unit-testable against real entity
shapes. Every later module (Sales, Purchases, POS, Car Showroom) calls these rather than posting
journals inline.

## Phase 5 — Accounting
Wire `JournalEntriesController`'s missing `/reverse` endpoint; `VouchersController`'s `/post`
route onto the real engine; trial balance / account statement / financial summary reports for
real.

## Phase 6 — Inventory
`StockMovementsController`'s `/record` route onto the real `IInventoryService`; real
`ReportsController` inventory-audit / item-ledger-detail (currently fabricates plausible numbers
instead of reading `StockMovement` history — needs to become real once Sales/Purchases actually
write movements).

## Phase 7 — Sales
`Invoice` creation/posting/ZATCA-QR (server-side, matching `zatca-tlv.util.ts`) through the real
accounting+inventory engines; `Quotation`→Invoice conversion endpoint; `CommercialOrder` CRUD
already exists, just needs conversion wiring.

## Phase 8 — Purchases
`MaterialRequisition`→Invoice conversion endpoint; purchase-side `Invoice` posting through the
same engines (Dr inventory/expense + input VAT, Cr cash/bank or supplier, per the user's own
worked examples) rather than the currently-mocked path.

## Phase 9 — POS
Build from scratch: shift lifecycle, held carts, coupon/loyalty rules, checkout (creates one
`Invoice`-equivalent record through the real engines — not a parallel hand-rolled duplicate the
way `erp.service.ts`'s `completePosCheckout` does today), returns.

## Phase 10 — Vehicles (Car Showroom)
Once Phase 0/3 entities exist: brands/models/trims/years/agents/colors CRUD; the 7-stage
procurement state machine (port `CarShowroomService.cs`'s Infrastructure logic, validated against
the frontend's exact stage names/order); the 5-stage sales contract lifecycle including the
ZATCA margin-scheme VAT calculation (`VatCalculationMode`) that's a real business rule, not
incidental.

## Phase 11 — Reports
Wire the 8 car-showroom report actions in `ReportsController` (blocked until Phase 10 entities
exist); make `trade-commercial`/`inventory-audit`/`item-ledger-detail` read real posted data
instead of synthesizing plausible numbers.

## Phase 12 — ZATCA
Register `ZatcaPhase2Service` in DI, call it from `InvoicesController`/POS checkout. Real
cryptographic signing/CSID/UBL-XML submission blocked on decision #9 (needs your ZATCA
credentials) — Phase-1 TLV/QR path can be finished and correct without them.

## Phase 13 — Testing
No test project exists today. Add `ERP.Application.Tests` / `ERP.Api.IntegrationTests` starting
with the accounting engine (balance validation, posting) and inventory engine (moving-average
math) since those are the highest-blast-radius pieces if wrong.

## Phase 14 — Frontend Integration
Point Angular's services at real HTTP calls behind the existing `backend-api.models.ts` DTO
shapes (already designed for this), removing the in-memory `signal()` seed data module by module,
starting with whichever module we build first end-to-end.

---

## Suggested order for actually starting Phase 2+ work

Given the mapping doc, the modules with the most downstream dependents and the least remaining
ambiguity are, in order: **Phase 0 (compile) → Accounting/Inventory core engines → Master data
(already mostly fine) → Sales/Purchases (reuses the engines) → Car Showroom (biggest missing
chunk, but self-contained) → POS → Contracts/Delivery Notes → Reports → Auth/Permissions
(can be layered in parallel once the User/Role model is confirmed) → ZATCA**. I'll proceed in
this order, building and fixing after each module per your instruction, rather than generating
everything speculatively before the first one is verified to build.
