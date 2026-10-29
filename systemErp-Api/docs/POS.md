# توثيق نقاط البيع والورديات

## مجالات البيانات

| المجال | الكيانات الأساسية | الجداول |
|---|---|---|
| الورديات والدرج | `PosShift`, `PosCashMovement` | `PosShift`, `PosCashMovement` |
| المبيعات | `PosTransaction`, `PosTransactionItem` | `PosTransaction`, `PosTransactionItem` |
| المرتجعات | `PosSalesReturn`, `PosSalesReturnItem` | `PosSalesReturn`, `PosSalesReturnItem` |
| السلال المعلقة | `PosHeldCart`, `PosHeldCartItem` | `PosHeldCart`, `PosHeldCartItem` |
| العروض والكوبونات | `PosOffer`, `PosCoupon` | `PosOffer`, `PosCoupon` |
| الولاء والإعدادات | `CustomerLoyalty`, `PosInvoiceSettings` | `CustomerLoyalty`, `PosInvoiceSettings` |

## ملفات النماذج

- `ERP.Core/Models/POS/Shifts/PosShift.cs`
- `ERP.Core/Models/POS/Shifts/PosCashMovement.cs`
- `ERP.Core/Models/POS/Sales/PosTransaction.cs`
- `ERP.Core/Models/POS/Sales/PosTransactionItem.cs`
- `ERP.Core/Models/POS/Returns/PosSalesReturn.cs`
- `ERP.Core/Models/POS/Returns/PosSalesReturnItem.cs`
- `ERP.Core/Models/POS/Carts/PosHeldCart.cs`
- `ERP.Core/Models/POS/Carts/PosHeldCartItem.cs`
- `ERP.Core/Models/POS/Promotions/PosOffer.cs`
- `ERP.Core/Models/POS/Promotions/PosCoupon.cs`
- `ERP.Core/Models/POS/Loyalty/CustomerLoyalty.cs`
- `ERP.Core/Models/POS/Settings/PosInvoiceSettings.cs`
- التعدادات: `ERP.Core/Models/POS/Enums/`

## طبقة الخدمة

- منطق الوردية والدرج: `ERP.Service/Services/POS/Shifts/PosShiftService.cs`
- عقد الخدمة: `ERP.Core/Contracts/POS/Shifts/IPosShiftService.cs`
- كائنات النقل: `ERP.Core/DTOs/POS/Shifts/`
- مخطط EF Core المعتمد: `ERP.Service/Data/Migrations/ErpDbContextModelSnapshot.cs`

## قواعد تشغيل الدرج

- لا يسمح بفتح ورديتين متزامنتين للكاشير أو للطرفية.
- الحركة النقدية تتطلب مبلغًا موجبًا وسببًا وحسابًا نقديًا.
- يمنع الصرف إذا تجاوز الرصيد النقدي المتوقع في الدرج.
- إغلاق الوردية يسجل النقد المعدود والتباين، ويزامن قيد التباين المحاسبي.
- تقرير X للوردية المفتوحة وتقرير Z للوردية المغلقة.

## مراجع النظام

- [فهرس الجداول ونماذج الخادم](SYSTEM_CATALOG.md)
- [نقاط ربط API](API_ENDPOINTS.md)
- [ربط الواجهة بالخادم](FRONTEND_MAPPING.md)
