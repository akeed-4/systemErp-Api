namespace ERP.Core.Models.Shared;

/// <summary>
/// مستند مخزني مستقل: فاتورة صرف (إخراج) أو فاتورة استلام (إدخال). ليس بيعاً ولا شراءً: بلا ضريبة ولا عميل ولا مورد ولا هيئة،
/// والحساب المقابل بحسب غرض المستند (<see cref="CounterAccountCode"/>).
/// - الصرف: يخرج الأصناف بتكلفتها الفعلية حسب سياسة التكلفة. القيد: مدين الحساب المقابل / دائن مخزون موقع كل سطر.
/// - الاستلام: يدخلها بسعر الشراء الصافي بعد الخصم (قواعد الشراء في التكلفة). القيد: مدين مخزون الموقع / دائن الحساب المقابل،
///   وما يختلف عن قيمة المخزون الفعلية (المعيارية أو إيقاف تحديث التكلفة) على حساب الانحرافات.
/// الجرد الدوري: لا يُمس حساب المخزون؛ الصرف مدين المقابل / دائن المشتريات، والاستلام عكسه.
/// مسودة ← مرحّل ← ملغى (عكس الحركات والقيد، وفرق إعادة البناء إعادة تقييم).
/// </summary>
public class InventoryVoucher : BaseEntity, IScopedRecord
{
    /// <summary><see cref="InventoryVoucherKinds"/>.</summary>
    public string Kind { get; set; } = InventoryVoucherKinds.Issue;
    public string VoucherNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    /// <summary>موقع المستند (الافتراضي لأسطره)، ونطاق الوصول.</summary>
    public Guid? WarehouseId { get; set; }
    /// <summary><see cref="InventoryVoucherStatuses"/>.</summary>
    public string Status { get; set; } = InventoryVoucherStatuses.Draft;
    /// <summary>سبب الحركة (<see cref="InventoryVoucherReasons"/>): يحدد الحساب المقابل من إعدادات الحسابات دون تدخل المستخدم.</summary>
    public string Reason { get; set; } = InventoryVoucherReasons.InternalUse;
    /// <summary>
    /// الحساب المقابل المستخدم فعلاً في القيد لموقع المستند، يُثبَّت عند الترحيل (فارغ في المسودة). تعديل الإعدادات بعده لا يغيّره،
    /// والإلغاء يعكس القيد نفسه. الأسطر تحفظ حسابها (قد يختلف بموقع السطر في التسويات).
    /// </summary>
    public string CounterAccountCode { get; set; } = string.Empty;
    public Guid? CostCenterId { get; set; }
    /// <summary>مرجع خارجي اختياري (رقم طلب الصرف أو مستند المورد).</summary>
    public string? Reference { get; set; }
    public string? Notes { get; set; }

    /// <summary>إجمالي الكمية × السعر قبل الخصم (الاستلام؛ الصرف صفر).</summary>
    public decimal Subtotal { get; set; }
    /// <summary>خصم المستند (الاستلام): يوزَّع على الأسطر بنسبة صافيها كالمشتريات.</summary>
    public decimal DocumentDiscount { get; set; }
    /// <summary>مجموع الخصومات (الأسطر + المستند).</summary>
    public decimal DiscountTotal { get; set; }
    /// <summary>صافي المستند: الاستلام = مجموع صافي الأسطر؛ الصرف = قيمة ما خرج (بعد الترحيل).</summary>
    public decimal NetTotal { get; set; }
    /// <summary>تغيّر قيمة المخزون الفعلي عند الترحيل (موجب للاستلام، موجب كقيمة خروج للصرف).</summary>
    public decimal InventoryValue { get; set; }

    public Guid? JournalEntryId { get; set; }
    public DateTime? PostedAt { get; set; }
    public string? PostedBy { get; set; }
    public string? CancelReason { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancelledBy { get; set; }

    public Guid? CreatedByUserId { get; set; }
    public Guid? AssignedToUserId { get; set; }
    /// <summary>تزامن متفائل: ترحيلان أو إلغاءان متزامنان للمستند نفسه لا يُنشئان حركة مكررة.</summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public virtual ICollection<InventoryVoucherLine> Lines { get; set; } = new List<InventoryVoucherLine>();
}

/// <summary>سطر صنف في مستند الصرف/الاستلام. الكمية والسعر بوحدة السطر، والمخزون بالأساسية (× المعامل).</summary>
public class InventoryVoucherLine : BaseEntity
{
    public Guid VoucherId { get; set; }
    public int LineNo { get; set; }
    public Guid ItemId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? Unit { get; set; }
    public decimal UnitFactor { get; set; } = 1;
    public decimal Quantity { get; set; }
    /// <summary>موقع السطر (فارغ = موقع المستند).</summary>
    public Guid? WarehouseId { get; set; }
    public Guid? CostCenterId { get; set; }
    /// <summary>الاستلام: سعر الشراء لوحدة السطر. الصرف: صفر (التكلفة من المخزون).</summary>
    public decimal UnitPrice { get; set; }
    /// <summary>خصم السطر مبلغاً (الاستلام).</summary>
    public decimal Discount { get; set; }
    /// <summary>حصة السطر من خصم المستند.</summary>
    public decimal AllocatedDiscount { get; set; }
    /// <summary>الاستلام: الكمية × السعر − الخصمين. الصرف: قيمة الخروج بعد الترحيل.</summary>
    public decimal LineTotal { get; set; }
    /// <summary>تكلفة وحدة السطر كما دخلت/خرجت من المخزون (بعد الترحيل).</summary>
    public decimal UnitCost { get; set; }
    /// <summary>القيمة التي دخلت/خرجت فعلاً من المخزون (تغيّر قيمته الدفترية، موجبة).</summary>
    public decimal TotalCost { get; set; }
    /// <summary>الصرف: تشغيلة مختارة (فارغ = الأقرب انتهاءً). الاستلام: لا يُستخدم.</summary>
    public Guid? ItemBatchId { get; set; }
    /// <summary>الاستلام: رقم التشغيلة الواردة. الصرف: ما خُصِّص فعلاً (للعرض والطباعة).</summary>
    public string? BatchNumber { get; set; }
    public DateTime? ProductionDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    /// <summary>الحساب المقابل المستخدم فعلاً في قيد هذا السطر (يُثبَّت عند الترحيل).</summary>
    public string? CounterAccountCode { get; set; }
    /// <summary>حركة المخزون عند الترحيل (تربط سند التكاليف الإضافية بحركة الاستلام).</summary>
    public Guid? StockMovementId { get; set; }
    public string? Notes { get; set; }

    public virtual InventoryVoucher Voucher { get; set; } = null!;
}

public static class InventoryVoucherKinds
{
    /// <summary>فاتورة صرف: إخراج مخزني.</summary>
    public const string Issue = "issue";
    /// <summary>فاتورة استلام: إدخال مخزني.</summary>
    public const string Receipt = "receipt";

    public static bool IsValid(string? kind) => kind is Issue or Receipt;
    /// <summary>مصدر الحركات والقيد ومفتاح الترقيم ونوع مالك المرفقات.</summary>
    public static string SourceOf(string kind) => kind == Receipt ? "inventory_receipt" : "inventory_issue";
    /// <summary>شاشة الصلاحيات.</summary>
    public static string ScreenOf(string kind) => kind == Receipt ? "inventory-receipts" : "inventory-issues";
}

/// <summary>
/// أسباب الحركة وحساب كل منها في إعدادات الحسابات (قسم «ربط الحسابات بحركات المخزون»). التحويل بين المواقع ليس سبباً هنا: يبقى
/// في التحويلات المخزنية بلا مصروف ولا إيراد.
/// </summary>
public static class InventoryVoucherReasons
{
    /// <summary>صرف داخلي: مصروف أو تكلفة (InventoryIssueAccountCode، الافتراضي 532).</summary>
    public const string InternalUse = "internal_use";
    /// <summary>صرف لمشروع: تكلفة المشروع أو الأعمال تحت التنفيذ (InventoryProjectIssueAccountCode، الافتراضي 517).</summary>
    public const string Project = "project";
    /// <summary>تسوية نقص: حساب عجز المخزون لموقع السطر (فروقات المخزون).</summary>
    public const string Shortage = "shortage";
    /// <summary>استلام رصيد افتتاحي: الأرصدة الافتتاحية (InventoryOpeningReceiptAccountCode، الافتراضي 33).</summary>
    public const string OpeningBalance = "opening_balance";
    /// <summary>تسوية زيادة: حساب زيادة المخزون لموقع السطر (فروقات المخزون).</summary>
    public const string Surplus = "surplus";
    /// <summary>استلام آخر بلا فاتورة ولا مورد (InventoryReceiptAccountCode، الافتراضي 424).</summary>
    public const string OtherReceipt = "other_receipt";

    public static readonly string[] Issue = { InternalUse, Project, Shortage };
    public static readonly string[] Receipt = { OpeningBalance, Surplus, OtherReceipt };

    public static string[] Of(string kind) => kind == InventoryVoucherKinds.Receipt ? Receipt : Issue;
    public static string DefaultOf(string kind) => kind == InventoryVoucherKinds.Receipt ? OtherReceipt : InternalUse;
    public static bool IsValid(string kind, string? reason) => reason != null && Of(kind).Contains(reason);
}

public static class InventoryVoucherStatuses
{
    public const string Draft = "draft";
    public const string Posted = "posted";
    public const string Cancelled = "cancelled";
}
