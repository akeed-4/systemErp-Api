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

    // ---- ورش السيارات (إضافة workshop): تكلفة أمر العمل تحت التنفيذ حتى فوترته (لا افتراضي؛ تُهيّأ بزرها أو عند أول ترحيل) ----
    /// <summary>أعمال ورشة تحت التنفيذ (أصل): قطع الغيار المصروفة والعمالة المحمّلة والخدمات الخارجية لأوامر العمل المفتوحة.</summary>
    public string? WorkshopWipAccountCode { get; set; }
    /// <summary>عمالة ورشة محمّلة (مصروف دائن الطبيعة): مقابل تحميل ساعات الفنيين بتكلفتها المعيارية؛ الرواتب الفعلية على حساباتها.</summary>
    public string? WorkshopAbsorbedLaborAccountCode { get; set; }
    /// <summary>تكلفة إيرادات الورشة (مصروف): تكلفة أسطر أمر العمل المفوترة للعميل، تُنقل من تحت التنفيذ عند الفوترة.</summary>
    public string? WorkshopCostOfRevenueAccountCode { get; set; }
    /// <summary>مصروف ضمان أعمال الورشة (مصروف): تكلفة أسطر الضمان والأعمال الداخلية غير المفوترة.</summary>
    public string? WorkshopWarrantyExpenseAccountCode { get; set; }

    // ---- تأجير المعدات (إضافة equipment_rental) ----
    /// <summary>إيراد تأجير المعدات (إيراد): الإيجار والساعات الإضافية والمشغّل والنقل في فواتير التأجير.</summary>
    public string? RentalRevenueAccountCode { get; set; }
    /// <summary>إيراد تعويضات وغرامات التأجير (إيراد): التلفيات والتأخير؛ طبيعتها الضريبية تُحدَّد لكل رسم.</summary>
    public string? RentalCompensationIncomeAccountCode { get; set; }
    /// <summary>تأمينات عملاء التأجير (التزام): التأمين النقدي المستلم حتى خصمه أو رده؛ غير خاضع للضريبة.</summary>
    public string? RentalDepositsLiabilityAccountCode { get; set; }

    // فترة الإرجاع: أقصى عدد أيام بين تاريخ الفاتورة وتاريخ مرتجعها (فارغ = بلا حد). منع نهائي لكل مسارات الإرجاع.
    public int? SalesReturnPeriodDays { get; set; }
    public int? PurchaseReturnPeriodDays { get; set; }

    /// <summary>
    /// فصل المُعِد عن المعتمِد في المقاولات: من قدّم خطة التنفيذ أو أعدّ التقرير اليومي أو قدّمه لا يعتمده. معطّل افتراضياً (منشأة يعمل
    /// فيها شخص واحد)، ويُفعَّل من الإعدادات.
    /// </summary>
    public bool ConstructionSeparateApprover { get; set; }
}
