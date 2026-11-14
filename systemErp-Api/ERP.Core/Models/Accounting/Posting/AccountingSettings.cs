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
    public string? OutputExciseAccountCode { get; set; }

    // ---- عقود جدول الكميات (المستخلصات): بلا افتراضي؛ الفوترة التي تحتاج حساباً غير محدد تُرفض برسالة الإعداد الناقص ----
    /// <summary>دفعات مقدمة مستلمة من العملاء على العقود (التزام): يُقيَّد عليها إيراد فاتورة الدفعة، ويُسترد منها بالمستخلصات.</summary>
    public string? ContractAdvancesReceivedAccountCode { get; set; }
    /// <summary>دفعات مقدمة مدفوعة لمقاولي الباطن (أصل).</summary>
    public string? ContractAdvancesPaidAccountCode { get; set; }
    /// <summary>محتجزات ضمان حسن التنفيذ لدى العملاء (أصل).</summary>
    public string? ContractRetentionReceivableAccountCode { get; set; }
    /// <summary>محتجزات ضمان حسن التنفيذ لمقاولي الباطن (التزام).</summary>
    public string? ContractRetentionPayableAccountCode { get; set; }
    /// <summary>غرامات تأخير مفروضة علينا في عقود العملاء (مصروف).</summary>
    public string? ContractPenaltiesExpenseAccountCode { get; set; }
    /// <summary>غرامات تأخير نفرضها على مقاولي الباطن (إيراد).</summary>
    public string? ContractPenaltiesIncomeAccountCode { get; set; }
    /// <summary>الخصومات الأخرى على المستخلصات (مصروف في عقود العملاء، إيراد/استرداد في عقود الموردين).</summary>
    public string? ContractOtherDeductionsAccountCode { get; set; }
    /// <summary>
    /// مواد مشاريع تحت التنفيذ (أصل): يُحمَّل عليها صرف مواد المشروع من المستودع (بدل المصروف)، وتُخفَّض بتكلفة المواد
    /// المختارة في المستخلص عند فوترته (إلى تكلفة الإيرادات).
    /// </summary>
    public string? ProjectMaterialsWipAccountCode { get; set; }
    /// <summary>تكلفة إيرادات العقود (مصروف): التكلفة الفعلية لمواد المستخلص المفوتر، مقابل المواد تحت التنفيذ.</summary>
    public string? ContractCostOfRevenueAccountCode { get; set; }
    /// <summary>تكلفة عمالة المشاريع (مصروف بمركز المشروع): ساعات العمالة والمهندسين المعتمدة في التقارير اليومية بسعرها المعياري.</summary>
    public string? ProjectLaborCostAccountCode { get; set; }
    /// <summary>تكلفة معدات المشاريع المملوكة (مصروف بمركز المشروع): ساعات تشغيلها المعتمدة بسعرها المعياري.</summary>
    public string? ProjectEquipmentCostAccountCode { get; set; }
    /// <summary>
    /// تكاليف محمّلة على المشاريع (مصروف دائن الطبيعة بلا مركز): مقابل قيد التحميل، فلا يتغير إجمالي مصروف المنشأة؛
    /// الرواتب والإهلاك الفعلية تبقى على حساباتها، والفرق بينها وبين المحمّل انحراف سعر.
    /// </summary>
    public string? ProjectAbsorbedCostAccountCode { get; set; }
    /// <summary>تكلفة معدات مستأجرة للمشاريع (مصروف بمركز المشروع): استحقاق ساعاتها المعتمدة في التقارير اليومية بشروط إيجارها.</summary>
    public string? ProjectRentedEquipmentCostAccountCode { get; set; }
    /// <summary>
    /// إيجار معدات مستحق (التزام بلا مركز، منفصل عن حسابات الموردين): مقابل الاستحقاق حتى تصل فاتورة المؤجر فتسوّيه.
    /// ليس ذمة مورد ولا فاتورة ضريبية.
    /// </summary>
    public string? ProjectAccruedRentalAccountCode { get; set; }
    /// <summary>
    /// بضاعة مستلمة لم تُفوتر (التزام): سند استلام مشتريات المشروع يقيّد مدين المواد تحت التنفيذ / دائن هذا الحساب بسعره، وفاتورة
    /// المورد من السند تسوّيه (مدين هذا الحساب / دائن المورد). مواد المشروع لا تدخل المستودع العام ولا متوسط تكلفة الصنف.
    /// </summary>
    public string? ProjectGoodsReceivedNotInvoicedAccountCode { get; set; }

    // فترة الإرجاع: أقصى عدد أيام بين تاريخ الفاتورة وتاريخ مرتجعها (فارغ = بلا حد). منع نهائي لكل مسارات الإرجاع.
    public int? SalesReturnPeriodDays { get; set; }
    public int? PurchaseReturnPeriodDays { get; set; }
}
