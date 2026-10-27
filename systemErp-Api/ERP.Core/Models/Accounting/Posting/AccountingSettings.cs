namespace ERP.Core.Models.Accounting;

/// <summary>
/// إعدادات الحسابات المحاسبية للمنشأة (صف واحد). كل حساب فارغ يعود لحسابه الافتراضي في الشجرة، فالمنشأة بلا إعداد
/// تُرحِّل كما كانت. الحساب المحدد يدوياً في المستند يتقدم دائماً على الإعداد.
/// </summary>
public class AccountingSettings : BaseEntity
{
    public InventorySystem InventoryMethod { get; set; } = InventorySystem.Perpetual;
    /// <summary>أول يوم تسري فيه الطريقة الحالية (بداية سنة مالية)؛ فارغ = منذ البداية.</summary>
    public DateTime? InventoryMethodEffectiveFrom { get; set; }
    /// <summary>الطريقة السارية قبل InventoryMethodEffectiveFrom.</summary>
    public InventorySystem? PreviousInventoryMethod { get; set; }

    // الجرد المستمر
    public string? InventoryAccountCode { get; set; }
    public string? CogsAccountCode { get; set; }
    public string? InventoryShortageAccountCode { get; set; }
    public string? InventorySurplusAccountCode { get; set; }

    // الجرد الدوري (إلزامية عند اختياره)
    public string? PurchasesAccountCode { get; set; }
    public string? PurchaseReturnsAccountCode { get; set; }
    public string? OpeningInventoryAccountCode { get; set; }
    public string? EndingInventoryAccountCode { get; set; }

    // المبيعات
    public string? SalesRevenueAccountCode { get; set; }
    public string? SalesReturnsAccountCode { get; set; }

    // ضريبة القيمة المضافة
    public string? OutputVatAccountCode { get; set; }
    public string? InputVatAccountCode { get; set; }
}
