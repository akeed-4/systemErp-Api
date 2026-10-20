namespace ERP.Core.Models.CarShowroom;

/// <summary>من يتحمل الرسم البيعي (نقل الملكية، اللوحات) افتراضياً.</summary>
public enum FeeBearer
{
    Customer = 1,   // يُحمَّل على العميل كسطر في فاتورته
    Showroom = 2    // مصروف على المعرض
}

/// <summary>مصير العربون عند إلغاء حجز عليه عربون.</summary>
public enum DepositCancellationPolicy
{
    Refund = 1,   // يُرد للعميل
    Forfeit = 2   // يُصادَر ويُقيَّد إيراداً
}

/// <summary>أساس هامش الربح الخاضع للضريبة في نظام هامش الربح للسيارات المستعملة.</summary>
public enum MarginBasis
{
    /// <summary>الهامش = سعر البيع − سعر الشراء من البائع فقط (المصاريف المُرسمَلة لا تخفّض الهامش).</summary>
    PurchasePrice = 1,
    /// <summary>الهامش = سعر البيع − التكلفة الكلية بعد المصاريف المُرسمَلة.</summary>
    TotalCost = 2
}

/// <summary>
/// إعدادات معرض السيارات لكل منشأة (صف واحد). تُنشأ بقيمها الافتراضية عند أول حفظ.
/// ⚠ الإعدادات الضريبية (نسبة الضريبة، أساس هامش الربح) والحسابات المحاسبية هنا يحددها محاسب المنشأة؛
/// القيم الافتراضية نقطة بداية وليست قاعدة ضريبية أو محاسبية ثابتة في النظام.
/// </summary>
public class CarShowroomSettings : BaseEntity
{
    /// <summary>الافتراضي للرسوم البيعية؛ يُغيَّر لكل سطر في العقد.</summary>
    public FeeBearer SaleFeesBorneBy { get; set; } = FeeBearer.Customer;
    /// <summary>مدة الحجز الافتراضية بالأيام حين لا يُحدَّد تاريخ انتهاء.</summary>
    public int ReservationDays { get; set; } = 7;
    /// <summary>الافتراضي عند إلغاء حجز عليه عربون؛ يُغيَّر لكل إلغاء.</summary>
    public DepositCancellationPolicy DepositCancellationPolicy { get; set; } = DepositCancellationPolicy.Refund;
    /// <summary>عند مرتجع البيع: هل تُرد للعميل الرسوم والإضافات المحمّلة عليه؟ يُغيَّر لكل مرتجع.</summary>
    public bool RefundCustomerChargesOnReturn { get; set; }

    // ---------- الضريبة (يحددها المحاسب) ----------
    /// <summary>نسبة الفئة الضريبية «خاضع»: تُطبَّق على السيارات بالنظام القياسي، وعلى هامش الربح، وعلى ما يُحمَّل على العميل.</summary>
    public decimal StandardVatRate { get; set; } = 15m;
    public MarginBasis MarginBasis { get; set; } = MarginBasis.PurchasePrice;

    // ---------- الحسابات (أكواد من شجرة الحسابات؛ القيود تقرأ منها ولا حساب ثابت في الكود) ----------
    public string VehicleInventoryAccount { get; set; } = "1142";
    public string VehiclesInTransitAccount { get; set; } = "1143";
    public string CarSalesRevenueAccount { get; set; } = "412";
    public string CarCogsAccount { get; set; } = "512";
    /// <summary>مستحقات مصاريف لم يُحدَّد موردها أو حساب دفعها (جمارك وموانئ...).</summary>
    public string AccruedChargesAccount { get; set; } = "212";
    /// <summary>رصيد محفظة تم: يُشحن من البنك وتُخصم منه رسوم العمليات.</summary>
    public string TammWalletAccount { get; set; } = "119";
    /// <summary>مبالغ مدفوعة نيابة عن العملاء: تُقيَّد عند الدفع وتُقفل عند تحميلها على العميل في فاتورته (لا تدخل المبيعات).</summary>
    public string CustomerRechargeAccount { get; set; } = "116";
    public string AddOnRevenueAccount { get; set; } = "413";
    public string AddOnCostAccount { get; set; } = "515";
    /// <summary>مصروف المعرض الافتراضي لنوع بلا حساب خاص.</summary>
    public string VehicleExpenseAccount { get; set; } = "529";
    public string CustomerDepositsAccount { get; set; } = "216";
    public string PrepaidExpensesAccount { get; set; } = "117";
    /// <summary>مصاريف شراء أُلغي: خسارة، أو مطالبة على المورد.</summary>
    public string PurchaseCancellationLossAccount { get; set; } = "530";
    public string SupplierClaimsAccount { get; set; } = "118";
    public string ForfeitedDepositsAccount { get; set; } = "423";
    /// <summary>تكاليف استيراد مدفوعة مقدماً لسيارات لم تصل: تُقيَّد عند الدفع وتُقفل بسحبها في سند التكاليف الإضافية.</summary>
    public string ImportCostsClearingAccount { get; set; } = "1144";
}
