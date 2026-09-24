# API endpoints (generated from swagger)

كل المسارات تحت `/api/v1` وتتطلب JWT ما عدا: تسجيل الدخول/التسجيل/استعادة كلمة المرور/الباقات/الشاشات.
الاستجابة: `{ success, message, data, errors, statusCode, timestamp }` (مسارات auth العامة مسطّحة).

## Accounts

| Method | Path |
|---|---|
| GET | `/accounts` |
| POST | `/accounts` |
| DELETE | `/accounts/{id}` |
| GET | `/accounts/{id}` |
| PUT | `/accounts/{id}` |
| GET | `/accounts/by-code/{code}` |
| GET | `/accounts/tree` |

## Agreements

| Method | Path |
|---|---|
| GET | `/agreements` |
| POST | `/agreements` |
| DELETE | `/agreements/{id}` |
| GET | `/agreements/{id}` |
| PUT | `/agreements/{id}` |

## ApprovalPolicies

| Method | Path |
|---|---|
| GET | `/approval-policies` |
| POST | `/approval-policies` |
| DELETE | `/approval-policies/{id}` |
| GET | `/approval-policies/{id}` |
| PUT | `/approval-policies/{id}` |
| PATCH | `/approval-policies/{id}/active` |

## Approvals

| Method | Path |
|---|---|
| GET | `/approvals` |
| DELETE | `/approvals/{id}` |
| GET | `/approvals/{id}` |
| POST | `/approvals/{id}/approve` |
| POST | `/approvals/{id}/reject` |
| POST | `/approvals/check` |

## Auth

| Method | Path |
|---|---|
| POST | `/auth/forgot-password/request` |
| POST | `/auth/forgot-password/reset` |
| POST | `/auth/forgot-password/verify-otp` |
| POST | `/auth/login` |
| GET | `/auth/me` |
| PUT | `/auth/profile` |
| POST | `/auth/register-company` |
| GET | `/auth/users` |

## Banks

| Method | Path |
|---|---|
| GET | `/banks` |
| POST | `/banks` |
| DELETE | `/banks/{id}` |
| GET | `/banks/{id}` |
| PUT | `/banks/{id}` |

## CarAgents

| Method | Path |
|---|---|
| GET | `/caragents` |
| POST | `/caragents` |
| DELETE | `/caragents/{id}` |
| GET | `/caragents/{id}` |
| PUT | `/caragents/{id}` |

## CarBrands

| Method | Path |
|---|---|
| GET | `/carbrands` |
| POST | `/carbrands` |
| DELETE | `/carbrands/{id}` |
| GET | `/carbrands/{id}` |
| PUT | `/carbrands/{id}` |

## CarColors

| Method | Path |
|---|---|
| GET | `/carcolors` |
| POST | `/carcolors` |
| DELETE | `/carcolors/{id}` |
| GET | `/carcolors/{id}` |
| PUT | `/carcolors/{id}` |

## CarModels

| Method | Path |
|---|---|
| GET | `/carmodels` |
| POST | `/carmodels` |
| DELETE | `/carmodels/{id}` |
| GET | `/carmodels/{id}` |
| PUT | `/carmodels/{id}` |

## CarProcurementOrders

| Method | Path |
|---|---|
| GET | `/carprocurementorders` |
| POST | `/carprocurementorders` |
| DELETE | `/carprocurementorders/{id}` |
| GET | `/carprocurementorders/{id}` |
| PUT | `/carprocurementorders/{id}` |
| POST | `/carprocurementorders/{id}/advance-stage` |
| POST | `/carprocurementorders/{id}/reject` |

## CarSalesContracts

| Method | Path |
|---|---|
| GET | `/carsalescontracts` |
| POST | `/carsalescontracts` |
| DELETE | `/carsalescontracts/{id}` |
| GET | `/carsalescontracts/{id}` |
| PUT | `/carsalescontracts/{id}` |
| POST | `/carsalescontracts/{id}/advance-status` |
| POST | `/carsalescontracts/{id}/cancel` |

## CarShowroomReports

| Method | Path |
|---|---|
| GET | `/reports/car/daily-monthly-sales` |
| GET | `/reports/car/installments-receivable` |
| GET | `/reports/car/procurement-tracking` |
| GET | `/reports/car/profit-loss` |
| GET | `/reports/car/sales-performance` |
| GET | `/reports/car/suppliers-procurement` |
| GET | `/reports/car/vin-inventory` |
| GET | `/reports/car/zatca-margin-tax` |

## CarTrims

| Method | Path |
|---|---|
| GET | `/cartrims` |
| POST | `/cartrims` |
| DELETE | `/cartrims/{id}` |
| GET | `/cartrims/{id}` |
| PUT | `/cartrims/{id}` |

## CarYears

| Method | Path |
|---|---|
| GET | `/caryears` |
| POST | `/caryears` |
| DELETE | `/caryears/{id}` |
| GET | `/caryears/{id}` |
| PUT | `/caryears/{id}` |

## CommercialContracts

| Method | Path |
|---|---|
| GET | `/commercialcontracts` |
| POST | `/commercialcontracts` |
| DELETE | `/commercialcontracts/{id}` |
| GET | `/commercialcontracts/{id}` |
| PUT | `/commercialcontracts/{id}` |
| POST | `/commercialcontracts/{id}/advance-stage` |
| POST | `/commercialcontracts/{id}/milestones/{milestoneId}/bill` |

## CommercialOrders

| Method | Path |
|---|---|
| GET | `/commercialorders` |
| POST | `/commercialorders` |
| DELETE | `/commercialorders/{id}` |
| GET | `/commercialorders/{id}` |
| PUT | `/commercialorders/{id}` |
| POST | `/commercialorders/{id}/convert-to-invoice` |

## Company

| Method | Path |
|---|---|
| GET | `/company` |
| PUT | `/company` |
| PUT | `/company/zatca-config` |
| POST | `/company/zatca-test` |

## CostCenters

| Method | Path |
|---|---|
| GET | `/costcenters` |
| POST | `/costcenters` |
| DELETE | `/costcenters/{id}` |
| GET | `/costcenters/{id}` |
| PUT | `/costcenters/{id}` |

## Costing

| Method | Path |
|---|---|
| GET | `/costing/policy` |
| PUT | `/costing/policy` |
| POST | `/costing/recalculate-all` |

## Currencies

| Method | Path |
|---|---|
| GET | `/currencies` |
| POST | `/currencies` |
| DELETE | `/currencies/{id}` |
| GET | `/currencies/{id}` |
| PUT | `/currencies/{id}` |

## Customers

| Method | Path |
|---|---|
| GET | `/customers` |
| POST | `/customers` |
| DELETE | `/customers/{id}` |
| GET | `/customers/{id}` |
| PUT | `/customers/{id}` |

## DeliveryNotes

| Method | Path |
|---|---|
| GET | `/deliverynotes` |
| POST | `/deliverynotes` |
| DELETE | `/deliverynotes/{id}` |
| GET | `/deliverynotes/{id}` |
| PUT | `/deliverynotes/{id}` |
| POST | `/deliverynotes/{id}/milestone-invoice` |
| GET | `/deliverynotes/returns` |
| POST | `/deliverynotes/returns` |
| DELETE | `/deliverynotes/returns/{id}` |
| GET | `/deliverynotes/returns/{id}` |

## FixedAssets

| Method | Path |
|---|---|
| GET | `/fixedassets` |
| POST | `/fixedassets` |
| DELETE | `/fixedassets/{id}` |
| GET | `/fixedassets/{id}` |
| PUT | `/fixedassets/{id}` |

## Invoices

| Method | Path |
|---|---|
| GET | `/invoices` |
| POST | `/invoices` |
| DELETE | `/invoices/{id}` |
| GET | `/invoices/{id}` |
| PUT | `/invoices/{id}` |
| POST | `/invoices/{id}/post` |
| POST | `/invoices/{id}/submit-zatca` |
| POST | `/invoices/returns` |

## JournalEntries

| Method | Path |
|---|---|
| GET | `/journalentries` |
| POST | `/journalentries` |
| DELETE | `/journalentries/{id}` |
| GET | `/journalentries/{id}` |
| PUT | `/journalentries/{id}` |
| POST | `/journalentries/{id}/reverse` |

## MaterialRequisitions

| Method | Path |
|---|---|
| GET | `/materialrequisitions` |
| POST | `/materialrequisitions` |
| DELETE | `/materialrequisitions/{id}` |
| GET | `/materialrequisitions/{id}` |
| PUT | `/materialrequisitions/{id}` |
| POST | `/materialrequisitions/{id}/approve` |
| POST | `/materialrequisitions/{id}/convert-to-invoice` |

## Notifications

| Method | Path |
|---|---|
| DELETE | `/notifications` |
| GET | `/notifications` |
| POST | `/notifications` |
| DELETE | `/notifications/{id}` |
| GET | `/notifications/{id}` |
| POST | `/notifications/{id}/read` |
| POST | `/notifications/read-all` |

## PaymentMethods

| Method | Path |
|---|---|
| GET | `/paymentmethods` |
| POST | `/paymentmethods` |
| DELETE | `/paymentmethods/{id}` |
| GET | `/paymentmethods/{id}` |
| PUT | `/paymentmethods/{id}` |

## Permissions

| Method | Path |
|---|---|
| GET | `/permissions` |
| POST | `/permissions` |
| GET | `/permissions/me` |
| GET | `/permissions/screens` |

## PosCoupons

| Method | Path |
|---|---|
| GET | `/pos/coupons` |
| POST | `/pos/coupons` |
| DELETE | `/pos/coupons/{id}` |
| GET | `/pos/coupons/{id}` |
| PUT | `/pos/coupons/{id}` |
| POST | `/pos/coupons/validate` |

## PosHeldCarts

| Method | Path |
|---|---|
| GET | `/pos/held-carts` |
| POST | `/pos/held-carts` |
| DELETE | `/pos/held-carts/{id}` |
| GET | `/pos/held-carts/{id}` |
| PUT | `/pos/held-carts/{id}` |

## PosLoyalty

| Method | Path |
|---|---|
| GET | `/pos/loyalty` |
| GET | `/pos/loyalty/{id}` |
| GET | `/pos/loyalty/customer/{customerId}` |

## PosOffers

| Method | Path |
|---|---|
| GET | `/pos/offers` |
| POST | `/pos/offers` |
| DELETE | `/pos/offers/{id}` |
| GET | `/pos/offers/{id}` |
| PUT | `/pos/offers/{id}` |

## PosReturns

| Method | Path |
|---|---|
| GET | `/pos/returns` |
| POST | `/pos/returns` |
| GET | `/pos/returns/{id}` |

## PosSettings

| Method | Path |
|---|---|
| GET | `/pos/settings` |
| PUT | `/pos/settings` |

## PosShifts

| Method | Path |
|---|---|
| GET | `/pos/shifts` |
| GET | `/pos/shifts/{id}` |
| GET | `/pos/shifts/active` |
| POST | `/pos/shifts/close` |
| POST | `/pos/shifts/open` |

## PosTransactions

| Method | Path |
|---|---|
| GET | `/pos/transactions` |
| GET | `/pos/transactions/{id}` |
| POST | `/pos/transactions/{id}/submit-zatca` |
| GET | `/pos/transactions/by-invoice/{invoiceNumber}` |
| POST | `/pos/transactions/checkout` |

## ProductCategories

| Method | Path |
|---|---|
| GET | `/productcategories` |
| POST | `/productcategories` |
| DELETE | `/productcategories/{id}` |
| GET | `/productcategories/{id}` |
| PUT | `/productcategories/{id}` |

## Products

| Method | Path |
|---|---|
| GET | `/products` |
| POST | `/products` |
| DELETE | `/products/{id}` |
| GET | `/products/{id}` |
| PUT | `/products/{id}` |

## Quotations

| Method | Path |
|---|---|
| GET | `/quotations` |
| POST | `/quotations` |
| DELETE | `/quotations/{id}` |
| GET | `/quotations/{id}` |
| PUT | `/quotations/{id}` |
| POST | `/quotations/{id}/convert-to-invoice` |
| POST | `/quotations/{id}/convert-to-order` |

## Reports

| Method | Path |
|---|---|
| GET | `/reports/account-statement/{code}` |
| GET | `/reports/financial-stats` |
| GET | `/reports/financial-summary` |
| GET | `/reports/inventory-audit` |
| GET | `/reports/item-ledger` |
| GET | `/reports/item-movements` |
| GET | `/reports/trade-commercial` |
| GET | `/reports/trial-balance` |
| GET | `/reports/vat-return` |

## StockMovements

| Method | Path |
|---|---|
| GET | `/stockmovements` |
| GET | `/stockmovements/{id}` |
| POST | `/stockmovements/adjust` |

## Subscriptions

| Method | Path |
|---|---|
| GET | `/subscriptions/current` |
| GET | `/subscriptions/plans` |
| POST | `/subscriptions/upgrade` |

## Suppliers

| Method | Path |
|---|---|
| GET | `/suppliers` |
| POST | `/suppliers` |
| DELETE | `/suppliers/{id}` |
| GET | `/suppliers/{id}` |
| PUT | `/suppliers/{id}` |

## UnitsOfMeasure

| Method | Path |
|---|---|
| GET | `/unitsofmeasure` |
| POST | `/unitsofmeasure` |
| DELETE | `/unitsofmeasure/{id}` |
| GET | `/unitsofmeasure/{id}` |
| PUT | `/unitsofmeasure/{id}` |

## Users

| Method | Path |
|---|---|
| GET | `/users` |
| POST | `/users` |
| DELETE | `/users/{id}` |
| GET | `/users/{id}` |
| PUT | `/users/{id}` |

## Vehicles

| Method | Path |
|---|---|
| GET | `/vehicles` |
| POST | `/vehicles` |
| DELETE | `/vehicles/{id}` |
| GET | `/vehicles/{id}` |
| PUT | `/vehicles/{id}` |
| GET | `/vehicles/available` |
| POST | `/vehicles/calculate-vat` |

## Vouchers

| Method | Path |
|---|---|
| GET | `/vouchers` |
| POST | `/vouchers` |
| DELETE | `/vouchers/{id}` |
| GET | `/vouchers/{id}` |
| PUT | `/vouchers/{id}` |

## Warehouses

| Method | Path |
|---|---|
| GET | `/warehouses` |
| POST | `/warehouses` |
| DELETE | `/warehouses/{id}` |
| GET | `/warehouses/{id}` |
| PUT | `/warehouses/{id}` |

**الإجمالي: 274 نقطة في 50 controller.**
